from datetime import datetime
from uuid import uuid4

from sqlalchemy import String, DateTime, Text, ForeignKey
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.database import Base


class Message(Base):
    __tablename__ = "messages"

    id: Mapped[str] = mapped_column(String(36), primary_key=True, default=lambda: str(uuid4()))
    chat_id: Mapped[str] = mapped_column(String(36), ForeignKey("chats.id"), nullable=False, index=True)
    sender_user_id: Mapped[str | None] = mapped_column(String(36), ForeignKey("users.id"), nullable=True)
    sender_bot_id: Mapped[str | None] = mapped_column(String(36), ForeignKey("bots.id"), nullable=True)
    content: Mapped[str] = mapped_column(Text, nullable=False)
    mentions: Mapped[str | None] = mapped_column(Text, nullable=True)  # JSON array of bot IDs
    reply_to_id: Mapped[str | None] = mapped_column(String(36), ForeignKey("messages.id", ondelete="SET NULL"), nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow, index=True)

    chat: Mapped["Chat"] = relationship("Chat", back_populates="messages")
    sender_user: Mapped["User | None"] = relationship("User")
    sender_bot: Mapped["Bot | None"] = relationship("Bot")
    reply_to: Mapped["Message | None"] = relationship("Message", remote_side=[id], foreign_keys=[reply_to_id])
    reactions: Mapped[list["Reaction"]] = relationship("Reaction", back_populates="message", cascade="all, delete-orphan")
