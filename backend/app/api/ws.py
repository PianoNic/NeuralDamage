import asyncio
import json
import logging

from fastapi import APIRouter, WebSocket, WebSocketDisconnect
from sqlalchemy import select

from app.database import async_session
from app.models.user import User
from app.services.auth_service import decode_app_token
from app.ws.manager import manager
from app.ws.handlers import handle_message, handle_reaction, handle_slash_command

logger = logging.getLogger(__name__)
router = APIRouter()


@router.websocket("/api/ws/{chat_id}")
async def websocket_endpoint(websocket: WebSocket, chat_id: str, token: str = ""):
    # Must accept the connection before we can close it or send messages
    await websocket.accept()

    # Authenticate via JWT token in query param
    try:
        payload = decode_app_token(token)
    except Exception as e:
        print(f"[WS] AUTH FAILED: {e}", flush=True)
        await websocket.close(code=4001, reason="Invalid token")
        return

    user_id = payload.get("sub")
    if not user_id:
        await websocket.close(code=4001, reason="Invalid token")
        return

    # Get user from DB
    async with async_session() as db:
        result = await db.execute(select(User).where(User.id == user_id))
        user = result.scalar_one_or_none()

    if not user:
        await websocket.close(code=4001, reason="User not found")
        return

    # Register connection (already accepted above, so skip accept in manager)
    if chat_id not in manager.active_connections:
        manager.active_connections[chat_id] = []
    manager.active_connections[chat_id].append(websocket)

    print(f"[WS] CONNECTED: user={user.display_name} chat={chat_id[:8]}... "
          f"total_connections={len(manager.active_connections.get(chat_id, []))}", flush=True)

    # Server-side heartbeat to keep the connection alive
    async def heartbeat():
        try:
            while True:
                await asyncio.sleep(10)
                await websocket.send_json({"type": "ping"})
        except Exception:
            pass

    heartbeat_task = asyncio.create_task(heartbeat())

    try:
        while True:
            data = await websocket.receive_text()
            msg = json.loads(data)

            if msg.get("type") == "message.send":
                content = msg.get("content", "").strip()
                mentions = msg.get("mentions", [])
                reply_to_id = msg.get("reply_to_id")
                print(f"[WS] MESSAGE from {user.display_name}: {content[:80]}", flush=True)
                if content:
                    # Check for slash commands first
                    if content.startswith("/"):
                        handled = await handle_slash_command(chat_id, user, content)
                        if handled:
                            continue
                    await handle_message(chat_id, user, content, mentions, reply_to_id=reply_to_id)

            elif msg.get("type") == "reaction.toggle":
                message_id = msg.get("message_id", "")
                emoji = msg.get("emoji", "")
                if message_id and emoji:
                    await handle_reaction(chat_id, user_id=user.id, bot_id=None, message_id=message_id, emoji=emoji)

            elif msg.get("type") == "typing.start":
                await manager.broadcast(chat_id, {
                    "type": "typing.indicator",
                    "user_id": user.id,
                    "display_name": user.display_name,
                })

            elif msg.get("type") == "pong":
                pass  # Client responded to ping, connection is alive

    except WebSocketDisconnect as e:
        print(f"[WS] DISCONNECTED: user={user.display_name} code={e.code} reason={e.reason}", flush=True)
    except Exception as e:
        print(f"[WS] ERROR: user={user.display_name} error={type(e).__name__}: {e}", flush=True)
    finally:
        heartbeat_task.cancel()
        manager.disconnect(chat_id, websocket)
