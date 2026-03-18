from datetime import datetime
from pydantic import BaseModel


class ChatCreate(BaseModel):
    name: str


class ChatUpdate(BaseModel):
    name: str | None = None


class MemberAdd(BaseModel):
    bot_id: str | None = None
    user_id: str | None = None


class MemberOut(BaseModel):
    id: str
    chat_id: str
    user_id: str | None = None
    bot_id: str | None = None
    role: str
    joined_at: datetime
    # Inline user/bot info
    display_name: str | None = None
    avatar_url: str | None = None
    member_type: str  # "user" or "bot"

    model_config = {"from_attributes": True}


class ChatOut(BaseModel):
    id: str
    name: str
    created_by: str
    created_at: datetime
    members: list[MemberOut] = []

    model_config = {"from_attributes": True}
