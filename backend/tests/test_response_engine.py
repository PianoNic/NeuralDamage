"""Tests for the LangChain-based response engine.

These tests mock the LLM call to verify prompt construction and decision parsing
without hitting OpenRouter. Integration tests with a real LLM are separate.
"""
import json
from dataclasses import dataclass, field
from datetime import datetime, UTC
from unittest.mock import AsyncMock, patch, MagicMock

import pytest

from app.services.response_engine import (
    should_bot_respond,
    _build_evaluation_prompt,
    _build_conversation_summary,
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
    system_prompt: str = "You are a helpful Python programming assistant"
    personality: str | None = "friendly Python expert"
    temperature: float = 0.7
    is_active: bool = True
    avatar_url: str | None = None


@dataclass
class FakeMessage:
    id: str = "msg-test"
    chat_id: str = "chat-1"
    sender_user_id: str | None = "user-1"
    sender_bot_id: str | None = None
    content: str = ""
    mentions: str | None = None
    created_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    sender_user: FakeUser | None = field(default_factory=FakeUser)
    sender_bot: FakeBot | None = None


class TestBuildEvaluationPrompt:
    def test_includes_bot_info(self):
        bot = FakeBot()
        msg = FakeMessage(content="Help me with Python")
        prompt = _build_evaluation_prompt(msg, bot, [msg])
        assert "TestBot" in prompt
        assert "friendly Python expert" in prompt

    def test_detects_mention(self):
        bot = FakeBot()
        msg = FakeMessage(content="@TestBot help", mentions=json.dumps(["bot-1"]))
        prompt = _build_evaluation_prompt(msg, bot, [msg])
        assert "Bot is @mentioned: True" in prompt

    def test_detects_name_in_text(self):
        bot = FakeBot()
        msg = FakeMessage(content="Hey TestBot what do you think?")
        prompt = _build_evaluation_prompt(msg, bot, [msg])
        assert "name appears in message text: True" in prompt

    def test_no_mention(self):
        bot = FakeBot()
        msg = FakeMessage(content="nice weather")
        prompt = _build_evaluation_prompt(msg, bot, [msg])
        assert "Bot is @mentioned: False" in prompt
        assert "name appears in message text: False" in prompt


class TestConversationSummary:
    def test_formats_user_messages(self):
        msg = FakeMessage(content="Hello everyone")
        summary = _build_conversation_summary([msg])
        assert "[User: TestUser]: Hello everyone" in summary

    def test_formats_bot_messages(self):
        bot = FakeBot(name="Sage")
        msg = FakeMessage(
            sender_user_id=None,
            sender_bot_id="bot-1",
            content="Hi there!",
            sender_user=None,
            sender_bot=bot,
        )
        summary = _build_conversation_summary([msg])
        assert "[Bot: Sage]: Hi there!" in summary


def _mock_llm_response(should_respond: bool, confidence: float, reasoning: str):
    """Create a mock LLM response with the expected JSON format."""
    mock_response = MagicMock()
    mock_response.content = json.dumps({
        "should_respond": should_respond,
        "confidence": confidence,
        "reasoning": reasoning,
    })
    return mock_response


class TestShouldBotRespond:
    @pytest.mark.asyncio
    async def test_mentioned_bot_responds(self):
        bot = FakeBot()
        msg = FakeMessage(content="@TestBot help", mentions=json.dumps(["bot-1"]))

        mock_resp = _mock_llm_response(True, 0.9, "Bot was directly mentioned")
        with patch("app.services.response_engine.get_judge_llm") as mock_llm:
            mock_llm.return_value.ainvoke = AsyncMock(return_value=mock_resp)
            should, score = await should_bot_respond(msg, bot, [msg])
            assert should is True
            assert score >= 0.5

    @pytest.mark.asyncio
    async def test_irrelevant_message_no_respond(self):
        bot = FakeBot()
        msg = FakeMessage(content="lol nice")

        mock_resp = _mock_llm_response(False, 0.1, "Casual acknowledgment, no response needed")
        with patch("app.services.response_engine.get_judge_llm") as mock_llm:
            mock_llm.return_value.ainvoke = AsyncMock(return_value=mock_resp)
            should, score = await should_bot_respond(msg, bot, [msg])
            assert should is False

    @pytest.mark.asyncio
    async def test_low_confidence_no_respond(self):
        """Even if should_respond=True, low confidence below threshold means no."""
        bot = FakeBot()
        msg = FakeMessage(content="Anyone know about databases?")

        mock_resp = _mock_llm_response(True, 0.3, "Maybe relevant but not sure")
        with patch("app.services.response_engine.get_judge_llm") as mock_llm:
            mock_llm.return_value.ainvoke = AsyncMock(return_value=mock_resp)
            should, score = await should_bot_respond(msg, bot, [msg])
            assert should is False  # 0.3 < 0.45 threshold

    @pytest.mark.asyncio
    async def test_fallback_on_llm_error_with_mention(self):
        """If the LLM call fails and bot is mentioned, fallback to True."""
        bot = FakeBot()
        msg = FakeMessage(content="@TestBot help", mentions=json.dumps(["bot-1"]))

        with patch("app.services.response_engine.get_judge_llm") as mock_llm:
            mock_llm.return_value.ainvoke = AsyncMock(side_effect=Exception("API error"))
            should, score = await should_bot_respond(msg, bot, [msg])
            assert should is True
            assert score == 0.7

    @pytest.mark.asyncio
    async def test_fallback_on_llm_error_no_mention(self):
        """If the LLM call fails and bot is NOT mentioned, fallback to False."""
        bot = FakeBot()
        msg = FakeMessage(content="random message")

        with patch("app.services.response_engine.get_judge_llm") as mock_llm:
            mock_llm.return_value.ainvoke = AsyncMock(side_effect=Exception("API error"))
            should, score = await should_bot_respond(msg, bot, [msg])
            assert should is False
            assert score == 0.0

    @pytest.mark.asyncio
    async def test_group_question_relevant_bot(self):
        bot = FakeBot()
        msg = FakeMessage(content="What's the best way to learn Python?")

        mock_resp = _mock_llm_response(True, 0.7, "Question about Python matches bot expertise")
        with patch("app.services.response_engine.get_judge_llm") as mock_llm:
            mock_llm.return_value.ainvoke = AsyncMock(return_value=mock_resp)
            should, score = await should_bot_respond(msg, bot, [msg])
            assert should is True
            assert score >= 0.45
