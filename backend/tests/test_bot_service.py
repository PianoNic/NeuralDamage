"""Tests for bot_service: reply target picking and history formatting."""
import json
from dataclasses import dataclass, field
from datetime import datetime, UTC
from unittest.mock import AsyncMock, patch, MagicMock

import pytest

from app.services.bot_service import (
    pick_reply_target,
    get_sender_name,
    format_history_for_bot,
)


@dataclass
class FakeUser:
    id: str = "user-1"
    display_name: str = "TestUser"
    avatar_url: str | None = None


@dataclass
class FakeBot:
    id: str = "bot-1"
    name: str = "TestBot"
    system_prompt: str = "You are a helpful assistant"
    personality: str | None = "helpful"
    temperature: float = 0.7
    is_active: bool = True
    avatar_url: str | None = None
    model_id: str = "openai/gpt-4o-mini"


@dataclass
class FakeMessage:
    id: str = "msg-1"
    chat_id: str = "chat-1"
    sender_user_id: str | None = "user-1"
    sender_bot_id: str | None = None
    content: str = ""
    mentions: str | None = None
    reply_to_id: str | None = None
    created_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    sender_user: FakeUser | None = field(default_factory=FakeUser)
    sender_bot: FakeBot | None = None


class TestGetSenderName:
    def test_user_sender(self):
        msg = FakeMessage(content="hi")
        assert get_sender_name(msg) == "TestUser"

    def test_bot_sender(self):
        bot = FakeBot(name="Snoop Dogg")
        msg = FakeMessage(
            sender_user_id=None, sender_bot_id="bot-1",
            content="yo", sender_user=None, sender_bot=bot,
        )
        assert get_sender_name(msg) == "Snoop Dogg"

    def test_unknown_user(self):
        msg = FakeMessage(sender_user_id="x", sender_user=None, content="hi")
        assert get_sender_name(msg) == "Unknown"


class TestPickReplyTarget:
    @pytest.mark.asyncio
    async def test_single_candidate_returns_it(self):
        bot = FakeBot()
        msg = FakeMessage(content="hello")
        result = await pick_reply_target(bot, "hey back!", [msg])
        assert result is msg

    @pytest.mark.asyncio
    async def test_llm_picks_index(self):
        bot = FakeBot()
        msg1 = FakeMessage(id="msg-1", content="What's Python?")
        msg2 = FakeMessage(id="msg-2", content="I like cats",
                          sender_user_id=None, sender_bot_id="bot-2",
                          sender_user=None, sender_bot=FakeBot(id="bot-2", name="CatBot"))

        mock_resp = MagicMock()
        mock_resp.status_code = 200
        mock_resp.raise_for_status = MagicMock()
        mock_resp.json.return_value = {
            "choices": [{"message": {"content": '{"index": 0}'}}]
        }

        with patch("app.services.bot_service.httpx.AsyncClient") as mock_client_cls:
            mock_client = AsyncMock()
            mock_client.post = AsyncMock(return_value=mock_resp)
            mock_client.__aenter__ = AsyncMock(return_value=mock_client)
            mock_client.__aexit__ = AsyncMock(return_value=False)
            mock_client_cls.return_value = mock_client

            result = await pick_reply_target(bot, "Python is great!", [msg1, msg2])
            assert result is msg1

    @pytest.mark.asyncio
    async def test_fallback_picks_human_message(self):
        bot = FakeBot()
        bot_msg = FakeMessage(
            id="msg-1", content="bot stuff",
            sender_user_id=None, sender_bot_id="bot-2",
            sender_user=None, sender_bot=FakeBot(id="bot-2", name="OtherBot"),
        )
        human_msg = FakeMessage(id="msg-2", content="human stuff")

        with patch("app.services.bot_service.httpx.AsyncClient") as mock_client_cls:
            mock_client = AsyncMock()
            mock_client.post = AsyncMock(side_effect=Exception("API error"))
            mock_client.__aenter__ = AsyncMock(return_value=mock_client)
            mock_client.__aexit__ = AsyncMock(return_value=False)
            mock_client_cls.return_value = mock_client

            result = await pick_reply_target(bot, "responding", [bot_msg, human_msg])
            assert result is human_msg


class TestFormatHistoryForBot:
    def test_own_messages_are_assistant_role(self):
        bot = FakeBot(id="bot-1", name="TestBot")
        user_msg = FakeMessage(id="m1", content="hello")
        bot_msg = FakeMessage(
            id="m2", content="hi there!",
            sender_user_id=None, sender_bot_id="bot-1",
            sender_user=None, sender_bot=bot,
        )
        history = format_history_for_bot([user_msg, bot_msg], bot)
        assert history[0]["role"] == "user"
        assert history[1]["role"] == "assistant"

    def test_includes_reply_context(self):
        bot = FakeBot()
        msg1 = FakeMessage(id="m1", content="What is Python?")
        msg2 = FakeMessage(id="m2", content="It's a language", reply_to_id="m1")
        history = format_history_for_bot([msg1, msg2], bot)
        assert "replying to" in history[1]["content"]

    def test_trims_to_token_budget(self):
        bot = FakeBot()
        # Create many long messages that exceed MAX_HISTORY_CHARS
        msgs = [FakeMessage(id=f"m{i}", content="x" * 2000) for i in range(10)]
        history = format_history_for_bot(msgs, bot)
        total = sum(len(m["content"]) for m in history)
        assert total <= 12_000
