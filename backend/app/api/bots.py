from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.models.bot import Bot
from app.models.user import User
from app.schemas.bot import BotCreate, BotUpdate, BotOut
from app.api.deps import get_current_user
from app.services.bot_service import list_openrouter_models

router = APIRouter(prefix="/api/bots", tags=["bots"])

# Map OpenRouter provider prefixes to LobeHub icon slugs (color variant where available)
_PROVIDER_ICON_MAP = {
    "openai": "openai",           # no -color, but distinctive enough
    "anthropic": "claude-color",
    "google": "gemini-color",
    "meta-llama": "meta-color",
    "meta": "meta-color",
    "mistralai": "mistral-color",
    "mistral": "mistral-color",
    "deepseek": "deepseek-color",
    "cohere": "cohere-color",
    "perplexity": "perplexity-color",
    "microsoft": "microsoft-color",
    "nvidia": "nvidia-color",
    "qwen": "qwen-color",
    "x-ai": "grok",              # no color variant, use grok icon
    "together": "together-ai",
    "fireworks": "fireworks-ai",
    "groq": "groq",
}

_ICON_CDN = "https://raw.githubusercontent.com/lobehub/lobe-icons/refs/heads/master/packages/static-png/light/{slug}.png"


def _avatar_from_model_id(model_id: str) -> str | None:
    provider = model_id.split("/")[0] if "/" in model_id else model_id
    slug = _PROVIDER_ICON_MAP.get(provider.lower())
    if slug:
        return _ICON_CDN.format(slug=slug)
    return None


@router.get("", response_model=list[BotOut])
async def list_bots(db: AsyncSession = Depends(get_db), _: User = Depends(get_current_user)):
    result = await db.execute(select(Bot).where(Bot.is_active == True))
    bots = result.scalars().all()
    # Backfill avatars for bots that don't have one
    for b in bots:
        if not b.avatar_url:
            b.avatar_url = _avatar_from_model_id(b.model_id)
    await db.flush()
    return [BotOut.model_validate(b) for b in bots]


@router.post("", response_model=BotOut)
async def create_bot(
    data: BotCreate,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    bot = Bot(**data.model_dump(), created_by=user.id)
    if not bot.avatar_url:
        bot.avatar_url = _avatar_from_model_id(bot.model_id)
    db.add(bot)
    await db.flush()
    return BotOut.model_validate(bot)


@router.get("/models")
async def get_models(_: User = Depends(get_current_user)):
    try:
        return await list_openrouter_models()
    except Exception as e:
        raise HTTPException(status_code=502, detail=f"OpenRouter error: {e}")


@router.get("/{bot_id}", response_model=BotOut)
async def get_bot(bot_id: str, db: AsyncSession = Depends(get_db), _: User = Depends(get_current_user)):
    result = await db.execute(select(Bot).where(Bot.id == bot_id, Bot.is_active == True))
    bot = result.scalar_one_or_none()
    if not bot:
        raise HTTPException(status_code=404, detail="Bot not found")
    return BotOut.model_validate(bot)


@router.put("/{bot_id}", response_model=BotOut)
async def update_bot(
    bot_id: str,
    data: BotUpdate,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    result = await db.execute(select(Bot).where(Bot.id == bot_id, Bot.is_active == True))
    bot = result.scalar_one_or_none()
    if not bot:
        raise HTTPException(status_code=404, detail="Bot not found")
    if bot.created_by != user.id:
        raise HTTPException(status_code=403, detail="Not your bot")

    for key, value in data.model_dump(exclude_unset=True).items():
        setattr(bot, key, value)

    # Update avatar if model changed and no custom avatar was set
    if "model_id" in data.model_dump(exclude_unset=True):
        bot.avatar_url = _avatar_from_model_id(bot.model_id)

    await db.flush()
    return BotOut.model_validate(bot)


@router.delete("/{bot_id}")
async def delete_bot(
    bot_id: str,
    db: AsyncSession = Depends(get_db),
    user: User = Depends(get_current_user),
):
    result = await db.execute(select(Bot).where(Bot.id == bot_id))
    bot = result.scalar_one_or_none()
    if not bot:
        raise HTTPException(status_code=404, detail="Bot not found")
    if bot.created_by != user.id:
        raise HTTPException(status_code=403, detail="Not your bot")

    bot.is_active = False
    return {"ok": True}
