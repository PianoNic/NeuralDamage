from datetime import datetime
from pydantic import BaseModel


class BotCreate(BaseModel):
    name: str
    model_id: str
    system_prompt: str
    personality: str | None = None
    avatar_url: str | None = None
    temperature: float = 0.7


class BotUpdate(BaseModel):
    name: str | None = None
    model_id: str | None = None
    system_prompt: str | None = None
    personality: str | None = None
    avatar_url: str | None = None
    temperature: float | None = None


class BotOut(BaseModel):
    id: str
    name: str
    avatar_url: str | None = None
    model_id: str
    system_prompt: str
    personality: str | None = None
    temperature: float
    created_by: str
    created_at: datetime
    is_active: bool

    model_config = {"from_attributes": True}
