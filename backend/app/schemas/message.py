from datetime import datetime
from pydantic import BaseModel


class ReactionOut(BaseModel):
    emoji: str
    count: int
    user_ids: list[str]
    bot_ids: list[str]
    names: list[str] = []


class ReplyInfo(BaseModel):
    id: str
    sender_name: str
    sender_type: str
    content: str  # truncated preview


class MessageOut(BaseModel):
    id: str
    chat_id: str
    sender_user_id: str | None = None
    sender_bot_id: str | None = None
    sender_name: str
    sender_avatar: str | None = None
    sender_type: str  # "user" or "bot"
    content: str
    mentions: list[str] = []
    reactions: list[ReactionOut] = []
    reply_to: ReplyInfo | None = None
    created_at: datetime

    model_config = {"from_attributes": True}


class MessageSend(BaseModel):
    content: str
    mentions: list[str] = []
