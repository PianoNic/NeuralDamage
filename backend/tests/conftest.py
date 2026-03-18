import asyncio
import pytest
import pytest_asyncio
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from app.database import Base, get_db
from app.models import User, Bot, Chat, Message, ChatMember
from app.main import app


@pytest.fixture(scope="session")
def event_loop():
    loop = asyncio.new_event_loop()
    yield loop
    loop.close()


@pytest_asyncio.fixture
async def db_engine():
    engine = create_async_engine("sqlite+aiosqlite:///:memory:")
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield engine
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await engine.dispose()


@pytest_asyncio.fixture
async def db_session(db_engine):
    session_factory = async_sessionmaker(db_engine, class_=AsyncSession, expire_on_commit=False)
    async with session_factory() as session:
        yield session


@pytest_asyncio.fixture
async def test_user(db_session: AsyncSession):
    user = User(
        email="test@example.com",
        display_name="Test User",
        oidc_sub="test-sub-123",
    )
    db_session.add(user)
    await db_session.commit()
    await db_session.refresh(user)
    return user


@pytest_asyncio.fixture
async def test_bot(db_session: AsyncSession, test_user: User):
    bot = Bot(
        name="TestBot",
        model_id="openai/gpt-4o-mini",
        system_prompt="You are a helpful test bot that knows about Python programming.",
        personality="friendly Python expert",
        created_by=test_user.id,
    )
    db_session.add(bot)
    await db_session.commit()
    await db_session.refresh(bot)
    return bot


@pytest_asyncio.fixture
async def test_chat(db_session: AsyncSession, test_user: User):
    chat = Chat(name="Test Chat", created_by=test_user.id)
    db_session.add(chat)
    await db_session.commit()
    await db_session.refresh(chat)

    member = ChatMember(chat_id=chat.id, user_id=test_user.id, role="owner")
    db_session.add(member)
    await db_session.commit()

    return chat
