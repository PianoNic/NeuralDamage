from datetime import datetime
from pydantic import BaseModel


class UserOut(BaseModel):
    id: str
    email: str
    display_name: str
    avatar_url: str | None = None
    created_at: datetime

    model_config = {"from_attributes": True}
