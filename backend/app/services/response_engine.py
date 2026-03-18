import json
import logging
from datetime import datetime, UTC

from langchain_openai import ChatOpenAI
from langchain_core.messages import SystemMessage, HumanMessage
from langchain_core.output_parsers import JsonOutputParser

from app.models.bot import Bot
from app.models.message import Message
from app.config import settings

logger = logging.getLogger(__name__)

# LangChain LLM that talks to OpenRouter (OpenAI-compatible API)
_judge_llm: ChatOpenAI | None = None


def get_judge_llm() -> ChatOpenAI:
    global _judge_llm
    if _judge_llm is None:
        _judge_llm = ChatOpenAI(
            model=settings.JUDGE_MODEL,
            openai_api_key=settings.OPENROUTER_API_KEY,
            openai_api_base="https://openrouter.ai/api/v1",
            temperature=0.1,
            max_tokens=256,
            default_headers={
                "HTTP-Referer": settings.APP_URL,
                "X-Title": "Neural Damage",
            },
        )
    return _judge_llm


JUDGE_SYSTEM_PROMPT = """You are a conversation moderator for a group chat that includes both humans and AI bots.
Your job is to decide whether a specific bot should respond to the latest message.

You will receive:
- The bot's name, personality, and system prompt
- The recent conversation history
- The latest message that triggered this evaluation
- Metadata: whether the bot was @mentioned, how recently the bot last spoke, how many of the last 10 messages are from this bot

Rules for deciding:
- If the bot is directly @mentioned or its name appears in the message, it should almost always respond (score 0.8-1.0)
- If the message addresses ALL bots or everyone (e.g. "all bots", "everyone", "you guys", "can all of you", "bots"), ALL bots should respond (score 0.8-1.0)
- If the message is a question directed at the group (contains "?" with no specific @mention), the bot should respond — this is a group chat and bots should be active participants (score 0.5-0.8)
- If the bot just spoke recently (< 30 seconds ago), it should usually stay quiet unless directly addressed (small penalty)
- If the bot dominates the conversation (>3 of last 10 messages), it should back off unless addressed
- If the message is a bare acknowledgment with no substance (like "ok", "k", "thanks"), bots should stay quiet (score 0.0-0.2)
- If the topic matches the bot's expertise/personality, lean strongly toward responding
- IMPORTANT: If the message is FROM ANOTHER BOT and does NOT @mention or name this bot, this bot should almost NEVER respond (score 0.0-0.1). Bots should only talk to each other when explicitly addressed. This prevents bot pile-ons.
- If the message is from a human, bots are meant to be active participants — when in doubt, respond rather than stay silent
- Consider natural conversation flow but err on the side of engagement for human messages

Respond with ONLY a JSON object (no markdown, no explanation):
{"should_respond": true/false, "confidence": 0.0-1.0, "reasoning": "brief explanation"}"""


def _build_conversation_summary(recent_messages: list[Message]) -> str:
    lines = []
    for msg in recent_messages[-15:]:
        if msg.sender_user_id:
            name = msg.sender_user.display_name if msg.sender_user else "Unknown"
            prefix = f"[User: {name}]"
        elif msg.sender_bot_id:
            name = msg.sender_bot.name if msg.sender_bot else "Unknown"
            prefix = f"[Bot: {name}]"
        else:
            prefix = "[Unknown]"
        lines.append(f"{prefix}: {msg.content}")
    return "\n".join(lines)


def _build_evaluation_prompt(
    message: Message,
    bot: Bot,
    recent_messages: list[Message],
) -> str:
    mentions = json.loads(message.mentions) if message.mentions else []
    is_mentioned = bot.id in mentions
    name_in_text = bot.name.lower() in message.content.lower()

    # Compute recency and dominance metadata
    bot_last_spoke_seconds = None
    bot_msg_count = 0
    now = datetime.utcnow()
    for msg in reversed(recent_messages[-10:]):
        if msg.sender_bot_id == bot.id:
            if bot_last_spoke_seconds is None:
                # Use naive UTC to match SQLAlchemy's naive created_at
                created = msg.created_at.replace(tzinfo=None) if msg.created_at.tzinfo else msg.created_at
                bot_last_spoke_seconds = (now - created).total_seconds()
            bot_msg_count += 1

    sender_name = "Unknown"
    if message.sender_user_id and message.sender_user:
        sender_name = message.sender_user.display_name
    elif message.sender_bot_id and message.sender_bot:
        sender_name = message.sender_bot.name

    conversation = _build_conversation_summary(recent_messages)

    sender_type = "bot" if message.sender_bot_id else "human"

    return f"""## Bot Under Evaluation
- Name: {bot.name}
- Personality: {bot.personality or 'not specified'}
- System prompt summary: {bot.system_prompt[:200]}

## Metadata
- Bot is @mentioned: {is_mentioned}
- Bot's name appears in message text: {name_in_text}
- Seconds since bot last spoke: {bot_last_spoke_seconds or 'never spoke'}
- Bot messages in last 10: {bot_msg_count}/10
- Message is from: {sender_type}

## Recent Conversation
{conversation}

## Latest Message (evaluate whether the bot should respond to THIS)
[{sender_name} ({sender_type})]: {message.content}"""


async def should_bot_respond(
    message: Message,
    bot: Bot,
    recent_messages: list[Message],
) -> tuple[bool, float]:
    """Use a LangChain LLM agent to decide if a bot should respond."""
    try:
        llm = get_judge_llm()
        parser = JsonOutputParser()

        prompt_text = _build_evaluation_prompt(message, bot, recent_messages)

        response = await llm.ainvoke([
            SystemMessage(content=JUDGE_SYSTEM_PROMPT),
            HumanMessage(content=prompt_text),
        ])

        result = parser.parse(response.content)

        should_respond = result.get("should_respond", False)
        confidence = float(result.get("confidence", 0.0))
        reasoning = result.get("reasoning", "")

        logger.info(
            f"Judge decision for bot '{bot.name}': "
            f"respond={should_respond}, confidence={confidence:.2f}, "
            f"reason={reasoning}"
        )

        return should_respond and confidence >= settings.RESPONSE_THRESHOLD, confidence

    except Exception as e:
        logger.error(f"Judge agent failed for bot '{bot.name}': {e}")
        # Fallback: only respond if directly mentioned
        mentions = json.loads(message.mentions) if message.mentions else []
        if bot.id in mentions or bot.name.lower() in message.content.lower():
            return True, 0.7
        return False, 0.0
