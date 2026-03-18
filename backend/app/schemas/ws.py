from pydantic import BaseModel


class WSMessage(BaseModel):
    type: str
    content: str | None = None
    mentions: list[str] = []


class WSOutMessage(BaseModel):
    type: str
    message: dict | None = None
    bot_id: str | None = None
    bot_name: str | None = None
    user_id: str | None = None
    display_name: str | None = None
    member: dict | None = None
    member_id: str | None = None
    detail: str | None = None
