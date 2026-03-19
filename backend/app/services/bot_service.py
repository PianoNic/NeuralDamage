import re

import httpx

from app.config import settings
from app.models.bot import Bot
from app.models.message import Message

# Matches "(replying to Name: "...")" including nested parens/quotes
_REPLY_META_RE = re.compile(r'\(replying to [^"]*(?:"[^"]*"[^"]*)*\)\s*')

# ~4 chars per token; keep input history under ~3000 tokens to save costs
MAX_HISTORY_CHARS = 12_000


def strip_reply_metadata(text: str) -> str:
    """Remove all (replying to ...) prefixes from text."""
    return _REPLY_META_RE.sub("", text).strip()


def format_history_for_bot(messages: list[Message], current_bot: Bot) -> list[dict]:
    # Build a quick lookup for reply context
    msg_map = {m.id: m for m in messages}

    formatted = []
    for msg in messages:
        if msg.sender_user_id:
            sender_name = msg.sender_user.display_name if msg.sender_user else "Unknown"
        else:
            sender_name = msg.sender_bot.name if msg.sender_bot else "Unknown"

        # Include reply context if present
        prefix = ""
        if msg.reply_to_id and msg.reply_to_id in msg_map:
            reply_msg = msg_map[msg.reply_to_id]
            reply_sender = (reply_msg.sender_user.display_name if reply_msg.sender_user
                          else reply_msg.sender_bot.name if reply_msg.sender_bot
                          else "Unknown")
            prefix = f"(replying to {reply_sender}: \"{reply_msg.content[:80]}\")\n"

        # Strip leaked reply metadata from content, then truncate
        clean_content = strip_reply_metadata(msg.content)
        content_text = clean_content[:1500] + ("..." if len(clean_content) > 1500 else "")

        is_self = msg.sender_bot_id == current_bot.id
        formatted.append({
            "role": "assistant" if is_self else "user",
            "content": f"[{sender_name}]: {prefix}{content_text}",
        })

    # Trim oldest messages to stay within token budget
    total_chars = sum(len(m["content"]) for m in formatted)
    while total_chars > MAX_HISTORY_CHARS and len(formatted) > 1:
        removed = formatted.pop(0)
        total_chars -= len(removed["content"])

    return formatted


CHAT_STYLE_WRAPPER = """You are "{bot_name}" in a casual group chat.

People and bots in this chat: {participants}
Messages in the history are prefixed with [Name]: so you know who said what. This prefix is ONLY for context — NEVER include [Name]: or any name prefix in YOUR responses. Just write your message directly.

Your personality/role: {user_prompt}

=== MANDATORY RULES (override everything above) ===
- You are ONLY "{bot_name}". NEVER speak as, impersonate, or write dialogue for other participants.
- NEVER generate responses on behalf of other bots or users. You can ONLY speak for yourself.
- You are a CHAT PARTICIPANT, not an assistant. Act like a person in a group chat.
- Keep EVERY response to 1-3 sentences MAX. Like texting, not writing emails.
- NO bullet points, NO numbered lists, NO headers, NO markdown formatting.
- NO essays, NO lengthy explanations, NO "here's what I think" preambles.
- Be casual, natural, use slang/humor when it fits your personality.
- React like a real person would — sometimes just an emoji or a few words is enough.
- If asked a question, give a quick direct answer. Don't elaborate unless asked to.
- Never say "As an AI" or break character. You ARE your persona.
- Match the energy of the conversation — if it's chill, be chill. If it's hype, be hype.
- You can talk to other bots and humans by name. Address them directly like you would in a real chat.
- When you want to talk to another bot specifically, use @TheirName so they know you're addressing them (e.g. "@Chef Marco what do you think?"). Without the @, they may not respond.
- If another bot or user says something interesting or wrong, feel free to jump in naturally.
- NEVER include "(replying to ...)" text in your messages. That is metadata, not something you should write.
- If another bot just responded, you do NOT need to also chime in unless you have something genuinely different to add. Avoid piling on or echoing what was already said.

=== CONTENT MODERATION (absolutely non-negotiable) ===
- NEVER engage with, repeat, validate, or echo slurs, hate speech, racist language, homophobia, or any offensive/derogatory content.
- If someone uses slurs or hate speech, firmly but briefly refuse: e.g. "Not cool." or "I'm not engaging with that." then move on. Do NOT lecture or moralize at length.
- NEVER use slurs or offensive language yourself, even "ironically" or in quotes.
- Do NOT roleplay violent, hateful, or illegal scenarios even if asked.
"""


def _extract_participants(chat_history: list[dict]) -> str:
    """Extract unique participant names from chat history."""
    names = set()
    for msg in chat_history:
        content = msg.get("content", "")
        if content.startswith("[") and "]: " in content:
            name = content[1:content.index("]: ")]
            names.add(name)
    return ", ".join(sorted(names)) if names else "unknown"


async def call_openrouter(bot: Bot, chat_history: list[dict]) -> str:
    participants = _extract_participants(chat_history)
    system_prompt = CHAT_STYLE_WRAPPER.format(
        bot_name=bot.name,
        user_prompt=bot.system_prompt,
        participants=participants,
    )
    messages = [
        {"role": "system", "content": system_prompt},
        *chat_history,
    ]

    async with httpx.AsyncClient() as client:
        resp = await client.post(
            "https://openrouter.ai/api/v1/chat/completions",
            headers={
                "Authorization": f"Bearer {settings.OPENROUTER_API_KEY}",
                "HTTP-Referer": settings.APP_URL,
                "X-Title": "Neural Damage",
            },
            json={
                "model": bot.model_id,
                "messages": messages,
                "temperature": bot.temperature,
                "max_tokens": 256,
            },
            timeout=60.0,
        )
        resp.raise_for_status()
        data = resp.json()
        return data["choices"][0]["message"]["content"]


BOT_REACTION_PROMPT = """You are "{bot_name}" in a group chat. Someone just posted a message.
Based on your personality, would you react to this message with an emoji? Only react if it genuinely warrants one — don't react to everything.

Your personality: {personality}
Message from {sender}: {content}

Respond with ONLY a JSON object, no markdown:
{{"react": true/false, "emoji": "emoji here or null"}}

Available emojis: 👍 ❤️ 😂 😮 😢 🙏 🔥 💯
Pick ONE emoji or null. Be selective — real people don't react to every message."""


async def maybe_bot_react(bot: Bot, message_content: str, sender_name: str) -> str | None:
    """Ask the bot if it wants to react to a message. Returns emoji or None."""
    import json as _json
    prompt = BOT_REACTION_PROMPT.format(
        bot_name=bot.name,
        personality=bot.personality or bot.system_prompt[:150],
        sender=sender_name,
        content=message_content[:300],
    )
    try:
        async with httpx.AsyncClient() as client:
            resp = await client.post(
                "https://openrouter.ai/api/v1/chat/completions",
                headers={
                    "Authorization": f"Bearer {settings.OPENROUTER_API_KEY}",
                    "HTTP-Referer": settings.APP_URL,
                    "X-Title": "Neural Damage",
                },
                json={
                    "model": settings.JUDGE_MODEL,
                    "messages": [{"role": "user", "content": prompt}],
                    "temperature": 0.3,
                    "max_tokens": 40,
                },
                timeout=15.0,
            )
            resp.raise_for_status()
            text = resp.json()["choices"][0]["message"]["content"]
            # Parse JSON from response
            text = text.strip().strip("`").strip()
            if text.startswith("json"):
                text = text[4:].strip()
            result = _json.loads(text)
            if result.get("react") and result.get("emoji"):
                return result["emoji"]
    except Exception as e:
        print(f"[BOT] Reaction check failed for {bot.name}: {e}", flush=True)
    return None


async def list_openrouter_models() -> list[dict]:
    async with httpx.AsyncClient() as client:
        resp = await client.get(
            "https://openrouter.ai/api/v1/models",
            headers={"Authorization": f"Bearer {settings.OPENROUTER_API_KEY}"},
            timeout=15.0,
        )
        resp.raise_for_status()
        data = resp.json()
        return [
            {
                "id": m["id"],
                "name": m.get("name", m["id"]),
                "context_length": m.get("context_length"),
                "pricing": m.get("pricing"),
            }
            for m in data.get("data", [])
        ]
