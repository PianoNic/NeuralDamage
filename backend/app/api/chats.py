from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from app.database import get_db
from app.models.chat import Chat
from app.models.chat_member import ChatMember
from app.models.bot import Bot
from app.models.user import User
from app.models.message import Message
from app.models.reaction import Reaction
from app.schemas.chat import ChatCreate, ChatUpdate, ChatOut, MemberAdd, MemberOut
from app.schemas.message import MessageOut, ReactionOut, ReplyInfo
from app.api.deps import get_current_user
from app.ws.manager import manager

router = APIRouter(prefix="/api/chats", tags=["chats"])


def _build_reply_info(msg: Message) -> ReplyInfo | None:
    if not msg.reply_to:
        return None
    rt = msg.reply_to
    sender_name = (
        rt.sender_user.display_name if rt.sender_user
        else rt.sender_bot.name if rt.sender_bot
        else "Unknown"
    )
    return ReplyInfo(
        id=rt.id,
        sender_name=sender_name,
        sender_type="user" if rt.sender_user_id else "bot",
        content=rt.content[:150],
    )


def _aggregate_reactions(reactions: list[Reaction]) -> list[ReactionOut]:
    grouped: dict[str, dict] = {}
    for r in reactions:
        if r.emoji not in grouped:
            grouped[r.emoji] = {"emoji": r.emoji, "count": 0, "user_ids": [], "bot_ids": [], "names": []}
        grouped[r.emoji]["count"] += 1
        if r.user_id:
            grouped[r.emoji]["user_ids"].append(r.user_id)
            if r.user:
                grouped[r.emoji]["names"].append(r.user.display_name)
        if r.bot_id:
            grouped[r.emoji]["bot_ids"].append(r.bot_id)
            if r.bot:
                grouped[r.emoji]["names"].append(r.bot.name)
    return [ReactionOut(**v) for v in grouped.values()]


def member_to_out(m: ChatMember) -> MemberOut:
    if m.user_id:
        return MemberOut(
            id=m.id, chat_id=m.chat_id, user_id=m.user_id,
            role=m.role, joined_at=m.joined_at,
            display_name=m.user.display_name if m.user else None,
            avatar_url=m.user.avatar_url if m.user else None,
            member_type="user",
        )
    else:
        return MemberOut(
            id=m.id, chat_id=m.chat_id, bot_id=m.bot_id,
            role=m.role, joined_at=m.joined_at,
            display_name=m.bot.name if m.bot else None,
            avatar_url=m.bot.avatar_url if m.bot else None,
            member_type="bot",
        )


@router.get("", response_model=list[ChatOut])
async def list_chats(db: AsyncSession = Depends(get_db), user: User = Depends(get_current_user)):
    result = await db.execute(
        select(Chat)
        .join(ChatMember, ChatMember.chat_id == Chat.id)
        .where(ChatMember.user_id == user.id)
        .options(selectinload(Chat.members).selectinload(ChatMember.user))
        .options(selectinload(Chat.members).selectinload(ChatMember.bot))
    )
    chats = result.unique().scalars().all()
    return [
        ChatOut(
            id=c.id, name=c.name, created_by=c.created_by, created_at=c.created_at,
            members=[member_to_out(m) for m in c.members],
        )
        for c in chats
    ]


@router.post("", response_model=ChatOut)
async def create_chat(
    data: ChatCreate,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    chat = Chat(name=data.name, created_by=user.id)
    db.add(chat)
    await db.flush()

    member = ChatMember(chat_id=chat.id, user_id=user.id, role="owner")
    db.add(member)
    await db.flush()

    return ChatOut(
        id=chat.id, name=chat.name, created_by=chat.created_by, created_at=chat.created_at,
        members=[MemberOut(
            id=member.id, chat_id=chat.id, user_id=user.id,
            role="owner", joined_at=member.joined_at,
            display_name=user.display_name, avatar_url=user.avatar_url,
            member_type="user",
        )],
    )


@router.get("/{chat_id}", response_model=ChatOut)
async def get_chat(chat_id: str, db: AsyncSession = Depends(get_db), _: User = Depends(get_current_user)):
    result = await db.execute(
        select(Chat).where(Chat.id == chat_id)
        .options(selectinload(Chat.members).selectinload(ChatMember.user))
        .options(selectinload(Chat.members).selectinload(ChatMember.bot))
    )
    chat = result.unique().scalar_one_or_none()
    if not chat:
        raise HTTPException(status_code=404, detail="Chat not found")
    return ChatOut(
        id=chat.id, name=chat.name, created_by=chat.created_by, created_at=chat.created_at,
        members=[member_to_out(m) for m in chat.members],
    )


@router.put("/{chat_id}", response_model=ChatOut)
async def update_chat(
    chat_id: str,
    data: ChatUpdate,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    result = await db.execute(select(Chat).where(Chat.id == chat_id))
    chat = result.scalar_one_or_none()
    if not chat:
        raise HTTPException(status_code=404, detail="Chat not found")
    if chat.created_by != user.id:
        raise HTTPException(status_code=403, detail="Not your chat")

    if data.name is not None:
        chat.name = data.name
    await db.flush()

    # Broadcast rename to all connected clients
    if data.name is not None:
        await manager.broadcast(chat_id, {
            "type": "chat.renamed",
            "chat_id": chat_id,
            "name": data.name,
        })

    return await get_chat(chat_id, db, user)


@router.delete("/{chat_id}")
async def delete_chat(
    chat_id: str,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    result = await db.execute(select(Chat).where(Chat.id == chat_id))
    chat = result.scalar_one_or_none()
    if not chat:
        raise HTTPException(status_code=404, detail="Chat not found")
    # Allow any member of the chat to delete it
    result = await db.execute(
        select(ChatMember).where(ChatMember.chat_id == chat_id, ChatMember.user_id == user.id)
    )
    if not result.scalar_one_or_none():
        raise HTTPException(status_code=403, detail="Not a member of this chat")

    # Broadcast deletion to all connected clients before deleting
    await manager.broadcast(chat_id, {
        "type": "chat.deleted",
        "chat_id": chat_id,
    })

    await db.delete(chat)
    return {"ok": True}


@router.post("/{chat_id}/members", response_model=MemberOut)
async def add_member(
    chat_id: str,
    data: MemberAdd,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(get_current_user),
):
    # Verify chat exists
    result = await db.execute(select(Chat).where(Chat.id == chat_id))
    if not result.scalar_one_or_none():
        raise HTTPException(status_code=404, detail="Chat not found")

    if data.bot_id:
        # Check bot exists
        result = await db.execute(select(Bot).where(Bot.id == data.bot_id, Bot.is_active == True))
        bot = result.scalar_one_or_none()
        if not bot:
            raise HTTPException(status_code=404, detail="Bot not found")

        # Check not already member
        result = await db.execute(
            select(ChatMember).where(ChatMember.chat_id == chat_id, ChatMember.bot_id == data.bot_id)
        )
        if result.scalar_one_or_none():
            raise HTTPException(status_code=400, detail="Bot already in chat")

        member = ChatMember(chat_id=chat_id, bot_id=data.bot_id, role="bot")
        db.add(member)
        await db.flush()

        member_out = MemberOut(
            id=member.id, chat_id=chat_id, bot_id=data.bot_id,
            role="bot", joined_at=member.joined_at,
            display_name=bot.name, avatar_url=bot.avatar_url,
            member_type="bot",
        )
        await manager.broadcast(chat_id, {
            "type": "member.added",
            "chat_id": chat_id,
            "member": member_out.model_dump(mode="json"),
        })
        return member_out
    elif data.user_id:
        result = await db.execute(select(User).where(User.id == data.user_id))
        target_user = result.scalar_one_or_none()
        if not target_user:
            raise HTTPException(status_code=404, detail="User not found")

        member = ChatMember(chat_id=chat_id, user_id=data.user_id, role="member")
        db.add(member)
        await db.flush()

        member_out = MemberOut(
            id=member.id, chat_id=chat_id, user_id=data.user_id,
            role="member", joined_at=member.joined_at,
            display_name=target_user.display_name, avatar_url=target_user.avatar_url,
            member_type="user",
        )
        await manager.broadcast(chat_id, {
            "type": "member.added",
            "chat_id": chat_id,
            "member": member_out.model_dump(mode="json"),
        })
        return member_out
    else:
        raise HTTPException(status_code=400, detail="Must provide bot_id or user_id")


@router.delete("/{chat_id}/members/{member_id}")
async def remove_member(
    chat_id: str,
    member_id: str,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(get_current_user),
):
    result = await db.execute(
        select(ChatMember).where(ChatMember.id == member_id, ChatMember.chat_id == chat_id)
    )
    member = result.scalar_one_or_none()
    if not member:
        raise HTTPException(status_code=404, detail="Member not found")

    await db.delete(member)

    # Broadcast removal to all connected clients
    await manager.broadcast(chat_id, {
        "type": "member.removed",
        "chat_id": chat_id,
        "member_id": member_id,
    })

    return {"ok": True}


@router.get("/{chat_id}/messages", response_model=list[MessageOut])
async def get_messages(
    chat_id: str,
    limit: int = 50,
    before: str | None = None,
    db: AsyncSession = Depends(get_db),
    _: User = Depends(get_current_user),
):
    import json
    query = (
        select(Message)
        .where(Message.chat_id == chat_id)
        .options(
            selectinload(Message.sender_user),
            selectinload(Message.sender_bot),
            selectinload(Message.reactions).selectinload(Reaction.user),
            selectinload(Message.reactions).selectinload(Reaction.bot),
            selectinload(Message.reply_to).selectinload(Message.sender_user),
            selectinload(Message.reply_to).selectinload(Message.sender_bot),
        )
        .order_by(Message.created_at.desc())
        .limit(limit)
    )

    if before:
        result_before = await db.execute(select(Message.created_at).where(Message.id == before))
        before_time = result_before.scalar_one_or_none()
        if before_time:
            query = query.where(Message.created_at < before_time)

    result = await db.execute(query)
    messages = list(reversed(result.scalars().all()))

    return [
        MessageOut(
            id=m.id,
            chat_id=m.chat_id,
            sender_user_id=m.sender_user_id,
            sender_bot_id=m.sender_bot_id,
            sender_name=(m.sender_user.display_name if m.sender_user else m.sender_bot.name if m.sender_bot else "Unknown"),
            sender_avatar=(m.sender_user.avatar_url if m.sender_user else m.sender_bot.avatar_url if m.sender_bot else None),
            sender_type="user" if m.sender_user_id else "bot",
            content=m.content,
            mentions=json.loads(m.mentions) if m.mentions else [],
            reactions=_aggregate_reactions(m.reactions) if m.reactions else [],
            reply_to=_build_reply_info(m),
            created_at=m.created_at,
        )
        for m in messages
    ]
