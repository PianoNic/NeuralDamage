import pytest
import pytest_asyncio
from httpx import AsyncClient, ASGITransport
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from app.database import Base, get_db
from app.models import User
from app.main import app
from app.services.auth_service import create_app_token


@pytest_asyncio.fixture
async def test_db():
    engine = create_async_engine("sqlite+aiosqlite:///:memory:")
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    session_factory = async_sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)

    async def override_get_db():
        async with session_factory() as session:
            try:
                yield session
                await session.commit()
            except Exception:
                await session.rollback()
                raise

    app.dependency_overrides[get_db] = override_get_db

    yield session_factory

    app.dependency_overrides.clear()
    await engine.dispose()


@pytest_asyncio.fixture
async def test_user(test_db):
    async with test_db() as session:
        user = User(
            email="test@example.com",
            display_name="Test User",
            oidc_sub="test-sub-123",
        )
        session.add(user)
        await session.commit()
        await session.refresh(user)
        return user


@pytest_asyncio.fixture
async def auth_headers(test_user):
    token = create_app_token(test_user.id, test_user.email)
    return {"Authorization": f"Bearer {token}"}


@pytest_asyncio.fixture
async def client(test_db):
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as ac:
        yield ac


@pytest.mark.asyncio
async def test_health(client: AsyncClient):
    resp = await client.get("/api/health")
    assert resp.status_code == 200
    assert resp.json() == {"status": "ok"}


@pytest.mark.asyncio
async def test_me_unauthorized(client: AsyncClient):
    resp = await client.get("/api/auth/me")
    assert resp.status_code in (401, 403)


@pytest.mark.asyncio
async def test_me_authorized(client: AsyncClient, auth_headers):
    resp = await client.get("/api/auth/me", headers=auth_headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["email"] == "test@example.com"
    assert data["display_name"] == "Test User"


@pytest.mark.asyncio
async def test_create_chat(client: AsyncClient, auth_headers):
    resp = await client.post("/api/chats", json={"name": "Test Chat"}, headers=auth_headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["name"] == "Test Chat"
    assert len(data["members"]) == 1
    assert data["members"][0]["member_type"] == "user"


@pytest.mark.asyncio
async def test_list_chats(client: AsyncClient, auth_headers):
    await client.post("/api/chats", json={"name": "Chat 1"}, headers=auth_headers)
    await client.post("/api/chats", json={"name": "Chat 2"}, headers=auth_headers)

    resp = await client.get("/api/chats", headers=auth_headers)
    assert resp.status_code == 200
    data = resp.json()
    assert len(data) == 2


@pytest.mark.asyncio
async def test_create_bot(client: AsyncClient, auth_headers):
    resp = await client.post("/api/bots", json={
        "name": "TestBot",
        "model_id": "openai/gpt-4o-mini",
        "system_prompt": "You are a test bot.",
    }, headers=auth_headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["name"] == "TestBot"
    assert data["is_active"] is True


@pytest.mark.asyncio
async def test_add_bot_to_chat(client: AsyncClient, auth_headers):
    # Create chat
    chat_resp = await client.post("/api/chats", json={"name": "Bot Chat"}, headers=auth_headers)
    chat_id = chat_resp.json()["id"]

    # Create bot
    bot_resp = await client.post("/api/bots", json={
        "name": "ChatBot",
        "model_id": "openai/gpt-4o-mini",
        "system_prompt": "Test bot.",
    }, headers=auth_headers)
    bot_id = bot_resp.json()["id"]

    # Add bot to chat
    member_resp = await client.post(
        f"/api/chats/{chat_id}/members",
        json={"bot_id": bot_id},
        headers=auth_headers,
    )
    assert member_resp.status_code == 200
    member_data = member_resp.json()
    assert member_data["member_type"] == "bot"
    assert member_data["bot_id"] == bot_id

    # Verify in chat details
    chat_detail = await client.get(f"/api/chats/{chat_id}", headers=auth_headers)
    members = chat_detail.json()["members"]
    assert len(members) == 2  # user + bot
    bot_members = [m for m in members if m["member_type"] == "bot"]
    assert len(bot_members) == 1
