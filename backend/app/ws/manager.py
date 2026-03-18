import json
from fastapi import WebSocket


class ConnectionManager:
    def __init__(self):
        self.active_connections: dict[str, list[WebSocket]] = {}
        # Chats where bot responses have been stopped (cleared on next human message)
        self.stopped_chats: set[str] = set()
        # Chats where bots are muted (persistent until /unmute)
        self.muted_chats: set[str] = set()

    async def connect(self, chat_id: str, websocket: WebSocket):
        await websocket.accept()
        if chat_id not in self.active_connections:
            self.active_connections[chat_id] = []
        self.active_connections[chat_id].append(websocket)

    def disconnect(self, chat_id: str, websocket: WebSocket):
        if chat_id in self.active_connections:
            self.active_connections[chat_id] = [
                ws for ws in self.active_connections[chat_id] if ws != websocket
            ]
            if not self.active_connections[chat_id]:
                del self.active_connections[chat_id]

    async def broadcast(self, chat_id: str, message: dict):
        if chat_id not in self.active_connections:
            return
        data = json.dumps(message, default=str)
        disconnected = []
        for ws in self.active_connections[chat_id]:
            try:
                await ws.send_text(data)
            except Exception:
                disconnected.append(ws)
        for ws in disconnected:
            self.disconnect(chat_id, ws)

    async def send_personal(self, websocket: WebSocket, message: dict):
        data = json.dumps(message, default=str)
        await websocket.send_text(data)


manager = ConnectionManager()
