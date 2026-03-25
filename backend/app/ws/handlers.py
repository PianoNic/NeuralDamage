import asyncio
import json
import random

from sqlalchemy import select, delete
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from app.database import async_session
from app.models.message import Message
from app.models.chat_member import ChatMember
from app.models.chat import Chat
from app.models.bot import Bot
from app.models.user import User
from app.models.reaction import Reaction
from app.services.response_engine import should_bot_respond
from app.services.bot_service import call_openrouter, format_history_for_bot, maybe_bot_react, pick_reply_target, strip_reply_metadata
from app.ws.manager import manager

# Max chain depth for bot-to-bot conversations (prevents infinite loops)
MAX_BOT_CHAIN_DEPTH = 3
# Max bots that can respond to a single trigger at the same depth
MAX_RESPONDERS_PER_DEPTH = 3

SLASH_COMMANDS = {
    "/stop": "Stop all bot responses immediately",
    "/clear": "Clear all messages in this chat",
    "/mute": "Mute all bots (they won't respond until /unmute)",
    "/unmute": "Unmute all bots",
    "/kick": "Remove a bot from the chat — usage: /kick BotName",
    "/rename": "Rename the chat — usage: /rename New Name",
    "/bots": "List all bots in this chat",
    "/help": "Show available slash commands",
}


def _build_reply_to_dict(reply_msg: Message | None) -> dict | None:
    """Build a reply_to dict for broadcast from a Message object."""
    if not reply_msg:
        return None
    sender_name = "Unknown"
    sender_type = "user"
    if reply_msg.sender_user_id:
        sender_name = reply_msg.sender_user.display_name if reply_msg.sender_user else "Unknown"
        sender_type = "user"
    elif reply_msg.sender_bot_id:
        sender_name = reply_msg.sender_bot.name if reply_msg.sender_bot else "Unknown"
        sender_type = "bot"
    return {
        "id": reply_msg.id,
        "sender_name": sender_name,
        "sender_type": sender_type,
        "content": reply_msg.content[:150],
    }


async def _broadcast_system(chat_id: str, content: str):
    """Broadcast a system message (not persisted)."""
    await manager.broadcast(chat_id, {
        "type": "system.message",
        "content": content,
    })


async def handle_slash_command(chat_id: str, user: User, content: str) -> bool:
    """Handle slash commands. Returns True if the content was a command."""
    parts = content.strip().split(maxsplit=1)
    cmd = parts[0].lower()
    arg = parts[1].strip() if len(parts) > 1 else ""

    if cmd not in SLASH_COMMANDS:
        return False

    if cmd == "/stop":
        manager.stopped_chats.add(chat_id)
        await _broadcast_system(chat_id, f"{user.display_name} stopped all bot responses.")

    elif cmd == "/clear":
        async with async_session() as db:
            # Delete reactions first (FK constraint), then messages
            await db.execute(
                delete(Reaction).where(
                    Reaction.message_id.in_(
                        select(Message.id).where(Message.chat_id == chat_id)
                    )
                )
            )
            await db.execute(delete(Message).where(Message.chat_id == chat_id))
            await db.commit()
        await manager.broadcast(chat_id, {
            "type": "chat.cleared",
            "by": user.display_name,
        })

    elif cmd == "/mute":
        manager.muted_chats.add(chat_id)
        await _broadcast_system(chat_id, f"{user.display_name} muted all bots. Use /unmute to re-enable.")

    elif cmd == "/unmute":
        manager.muted_chats.discard(chat_id)
        manager.stopped_chats.discard(chat_id)
        await _broadcast_system(chat_id, f"{user.display_name} unmuted all bots.")

    elif cmd == "/kick":
        if not arg:
            await _broadcast_system(chat_id, "Usage: /kick BotName")
            return True
        async with async_session() as db:
            result = await db.execute(
                select(ChatMember)
                .where(ChatMember.chat_id == chat_id, ChatMember.bot_id.is_not(None))
                .options(selectinload(ChatMember.bot))
            )
            bot_members = result.scalars().all()
            target = next(
                (m for m in bot_members if m.bot and m.bot.name.lower() == arg.lower()),
                None,
            )
            if not target:
                await _broadcast_system(chat_id, f"Bot '{arg}' not found in this chat.")
                return True
            bot_name = target.bot.name
            member_id = target.id
            await db.delete(target)
            await db.commit()
        await manager.broadcast(chat_id, {
            "type": "member.removed",
            "member_id": member_id,
            "chat_id": chat_id,
        })
        await _broadcast_system(chat_id, f"{user.display_name} kicked {bot_name} from the chat.")

    elif cmd == "/rename":
        if not arg:
            await _broadcast_system(chat_id, "Usage: /rename New Chat Name")
            return True
        async with async_session() as db:
            result = await db.execute(select(Chat).where(Chat.id == chat_id))
            chat = result.scalar_one_or_none()
            if chat:
                chat.name = arg
                await db.commit()
        await manager.broadcast(chat_id, {
            "type": "chat.renamed",
            "chat_id": chat_id,
            "name": arg,
        })

    elif cmd == "/bots":
        async with async_session() as db:
            result = await db.execute(
                select(ChatMember)
                .where(ChatMember.chat_id == chat_id, ChatMember.bot_id.is_not(None))
                .options(selectinload(ChatMember.bot))
            )
            bot_members = result.scalars().all()
        if not bot_members:
            await _broadcast_system(chat_id, "No bots in this chat.")
        else:
            lines = [f"• {m.bot.name} ({m.bot.model_id})" for m in bot_members if m.bot]
            await _broadcast_system(chat_id, "Bots in this chat:\n" + "\n".join(lines))

    elif cmd == "/help":
        lines = [f"{cmd} — {desc}" for cmd, desc in SLASH_COMMANDS.items()]
        await _broadcast_system(chat_id, "Available commands:\n" + "\n".join(lines))

    return True


async def handle_message(chat_id: str, user: User, content: str, mentions: list[str], reply_to_id: str | None = None):
    async with async_session() as db:
        # Load the reply-to message if provided
        reply_to_msg = None
        if reply_to_id:
            result = await db.execute(
                select(Message).where(Message.id == reply_to_id)
                .options(selectinload(Message.sender_user), selectinload(Message.sender_bot))
            )
            reply_to_msg = result.scalar_one_or_none()

        # Save user message
        msg = Message(
            chat_id=chat_id,
            sender_user_id=user.id,
            content=content,
            mentions=json.dumps(mentions) if mentions else None,
            reply_to_id=reply_to_id if reply_to_msg else None,
        )
        db.add(msg)
        await db.commit()
        await db.refresh(msg)

        # A new human message clears the /stop flag (but not /mute)
        manager.stopped_chats.discard(chat_id)

        # Broadcast to all
        await manager.broadcast(chat_id, {
            "type": "message.new",
            "message": {
                "id": msg.id,
                "chat_id": chat_id,
                "sender_user_id": user.id,
                "sender_bot_id": None,
                "sender_name": user.display_name,
                "sender_avatar": user.avatar_url,
                "sender_type": "user",
                "content": content,
                "mentions": mentions,
                "reactions": [],
                "reply_to": _build_reply_to_dict(reply_to_msg),
                "created_at": msg.created_at.isoformat(),
            },
        })

        # Trigger bot responses in background (unless muted)
        if chat_id not in manager.muted_chats:
            asyncio.create_task(process_bot_responses(chat_id, msg.id, depth=0))


async def process_bot_responses(chat_id: str, trigger_message_id: str, depth: int = 0, exclude_bot_id: str | None = None):
    """Interleaved evaluate-generate loop: evaluate all → pick best → generate →
    re-evaluate remaining with updated context → repeat.  This creates natural
    conversational threading instead of all bots piling on the same message."""
    if depth >= MAX_BOT_CHAIN_DEPTH:
        print(f"[BOT] Chain depth {depth} reached, stopping.", flush=True)
        return

    if chat_id in manager.stopped_chats or chat_id in manager.muted_chats:
        print(f"[BOT] Chat {chat_id[:8]} is stopped/muted, skipping.", flush=True)
        return

    async with async_session() as db:
        # Get bot members of this chat
        result = await db.execute(
            select(ChatMember)
            .where(ChatMember.chat_id == chat_id, ChatMember.bot_id.is_not(None))
            .options(selectinload(ChatMember.bot))
        )
        bot_members = result.scalars().all()

        if not bot_members:
            return

        # Get recent messages for context
        result = await db.execute(
            select(Message)
            .where(Message.chat_id == chat_id)
            .options(selectinload(Message.sender_user), selectinload(Message.sender_bot))
            .order_by(Message.created_at.desc())
            .limit(30)
        )
        recent_messages = list(reversed(result.scalars().all()))

        trigger_msg = recent_messages[-1] if recent_messages else None
        if not trigger_msg:
            return

        # Get trigger message sender name for reaction prompts
        trigger_sender = "Unknown"
        if trigger_msg.sender_user_id and trigger_msg.sender_user:
            trigger_sender = trigger_msg.sender_user.display_name
        elif trigger_msg.sender_bot_id and trigger_msg.sender_bot:
            trigger_sender = trigger_msg.sender_bot.name

        # Build the pool of candidate bots
        candidate_bots: list[Bot] = []
        for member in bot_members:
            bot = member.bot
            if not bot or not bot.is_active:
                continue
            if bot.id == exclude_bot_id:
                continue
            candidate_bots.append(bot)

        # --- Interleaved evaluate-generate loop ---
        # Each round: evaluate remaining candidates → pick highest scorer →
        # generate response → re-evaluate with updated context
        already_responded_names: list[str] = []
        responded_count = 0
        non_responding_bots: list[Bot] = []
        last_bot_msg = None
        last_bot_id = None

        while candidate_bots and responded_count < MAX_RESPONDERS_PER_DEPTH:
            if chat_id in manager.stopped_chats or chat_id in manager.muted_chats:
                print(f"[BOT] Chat {chat_id[:8]} stopped/muted mid-loop, aborting.", flush=True)
                return

            # Evaluate all remaining candidates (with awareness of who already spoke)
            scored: list[tuple[Bot, float]] = []
            rejected: list[Bot] = []
            for bot in candidate_bots:
                print(f"[BOT] Evaluating '{bot.name}' (depth={depth}, round={responded_count})...", flush=True)
                should, score = await should_bot_respond(
                    trigger_msg, bot, recent_messages[-10:],
                    already_responded=already_responded_names if already_responded_names else None,
                )
                print(f"[BOT] '{bot.name}': should_respond={should}, score={score:.2f}", flush=True)
                if should:
                    scored.append((bot, score))
                else:
                    rejected.append(bot)

            # No more bots want to respond — we're done
            if not scored:
                non_responding_bots.extend(rejected)
                non_responding_bots.extend([b for b, _ in scored])
                break

            # Pick the highest-scoring bot
            scored.sort(key=lambda x: x[1], reverse=True)
            winner_bot, winner_score = scored[0]
            remaining_scored = [b for b, _ in scored[1:]]

            # Stagger delay for natural feel (skip for the first responder)
            if responded_count > 0:
                await asyncio.sleep(random.uniform(1.5, 4.0))

            # Broadcast typing
            await manager.broadcast(chat_id, {
                "type": "message.bot_typing",
                "bot_id": winner_bot.id,
                "bot_name": winner_bot.name,
            })

            try:
                # Generate response with full conversation context
                history = format_history_for_bot(recent_messages, winner_bot)
                response_text = await call_openrouter(winner_bot, history)
                response_text = _strip_name_prefix(response_text, winner_bot.name)

                # Smart reply targeting: pick the most natural reply target
                # First bot replies to trigger; subsequent bots get LLM-picked target
                if responded_count == 0:
                    reply_target_msg = trigger_msg
                else:
                    reply_target_msg = await pick_reply_target(
                        winner_bot, response_text, recent_messages[-6:]
                    )

                # Save bot message
                bot_msg = Message(
                    chat_id=chat_id,
                    sender_bot_id=winner_bot.id,
                    content=response_text,
                    reply_to_id=reply_target_msg.id,
                )
                db.add(bot_msg)
                await db.commit()
                await db.refresh(bot_msg)

                # Broadcast
                await manager.broadcast(chat_id, {
                    "type": "message.new",
                    "message": {
                        "id": bot_msg.id,
                        "chat_id": chat_id,
                        "sender_user_id": None,
                        "sender_bot_id": winner_bot.id,
                        "sender_name": winner_bot.name,
                        "sender_avatar": winner_bot.avatar_url,
                        "sender_type": "bot",
                        "content": response_text,
                        "mentions": [],
                        "reactions": [],
                        "reply_to": _build_reply_to_dict(reply_target_msg),
                        "created_at": bot_msg.created_at.isoformat(),
                    },
                })

                # Update context for next round
                recent_messages.append(bot_msg)
                already_responded_names.append(winner_bot.name)
                last_bot_msg = bot_msg
                last_bot_id = winner_bot.id
                responded_count += 1

            except Exception as e:
                await manager.broadcast(chat_id, {
                    "type": "error",
                    "detail": f"Bot {winner_bot.name} failed to respond: {str(e)}",
                })

            # Update candidate pool: rejected bots are done, remaining scorers
            # plus any from previous rejected rounds go back for re-evaluation
            non_responding_bots.extend(rejected)
            candidate_bots = remaining_scored

        # Any bots still in candidates that weren't picked are non-responders
        non_responding_bots.extend(candidate_bots)

        # Let non-responding bots potentially react with emoji (only at depth 0)
        if depth == 0 and non_responding_bots:
            for bot in non_responding_bots:
                if chat_id in manager.stopped_chats or chat_id in manager.muted_chats:
                    break
                try:
                    emoji = await maybe_bot_react(bot, trigger_msg.content, trigger_sender)
                    if emoji:
                        print(f"[BOT] '{bot.name}' reacts with {emoji}", flush=True)
                        await handle_reaction(chat_id, user_id=None, bot_id=bot.id, message_id=trigger_msg.id, emoji=emoji)
                except Exception as e:
                    print(f"[BOT] Reaction failed for {bot.name}: {e}", flush=True)

        # Chain to next depth for bot-to-bot conversation
        if last_bot_msg and depth + 1 < MAX_BOT_CHAIN_DEPTH:
            print(f"[BOT] Depth {depth} done, chaining to depth {depth+1}", flush=True)
            await asyncio.sleep(random.uniform(1.5, 3.0))
            await process_bot_responses(chat_id, last_bot_msg.id, depth=depth + 1, exclude_bot_id=last_bot_id)


def _strip_name_prefix(text: str, bot_name: str) -> str:
    """Remove [BotName]: prefix and (replying to ...) prefixes the bot copied."""
    import re
    # Strip ALL (replying to ...) metadata anywhere in the text
    text = strip_reply_metadata(text)
    # Match patterns like [Chef Marco]: or [Tech Bro]: at the start
    pattern = rf'^\[{re.escape(bot_name)}\]:\s*'
    text = re.sub(pattern, '', text, flags=re.IGNORECASE)
    # Also match just "BotName: " at the very start
    pattern2 = rf'^{re.escape(bot_name)}:\s*'
    text = re.sub(pattern2, '', text, flags=re.IGNORECASE)
    return text.strip()


def _aggregate_reactions(reactions: list[Reaction]) -> list[dict]:
    """Group reactions by emoji for broadcast."""
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
    return list(grouped.values())


async def handle_reaction(chat_id: str, user_id: str | None, bot_id: str | None, message_id: str, emoji: str):
    """Toggle a reaction on a message (works for both users and bots)."""
    async with async_session() as db:
        # Find existing reaction
        query = select(Reaction).where(Reaction.message_id == message_id, Reaction.emoji == emoji)
        if user_id:
            query = query.where(Reaction.user_id == user_id)
        elif bot_id:
            query = query.where(Reaction.bot_id == bot_id)
        else:
            return

        result = await db.execute(query)
        existing = result.scalar_one_or_none()

        if existing:
            await db.delete(existing)
        else:
            reaction = Reaction(message_id=message_id, user_id=user_id, bot_id=bot_id, emoji=emoji)
            db.add(reaction)

        await db.commit()

        # Fetch updated reactions for this message (with names for tooltips)
        result = await db.execute(
            select(Reaction).where(Reaction.message_id == message_id)
            .options(selectinload(Reaction.user), selectinload(Reaction.bot))
        )
        all_reactions = result.scalars().all()

        await manager.broadcast(chat_id, {
            "type": "reaction.update",
            "message_id": message_id,
            "reactions": _aggregate_reactions(all_reactions),
        })
