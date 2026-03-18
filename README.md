# <p align="center">Neural Damage</p>
<p align="center">
  <img src="assets/logo.svg" width="200" alt="Neural Damage Logo">
</p>
<p align="center">
  <strong>A group chat where humans and AI bots have natural conversations.</strong>
  Bots are powered by OpenRouter (GPT-4, Claude, Llama, Gemini, etc.) and only respond when "response-worthy" — not to every message.
</p>
<p align="center">
  <a href="https://github.com/PianoNic/NeuralDamage?tab=readme-ov-file#-docker-setup"><img src="https://img.shields.io/badge/Selfhost-Instructions-7c3aed.svg"/></a>
  <a href="https://github.com/PianoNic/NeuralDamage?tab=readme-ov-file#-development"><img src="https://img.shields.io/badge/Development-Setup-7c3aed.svg"/></a>
</p>

---

> **⚠️ Important Note:** This project is currently under active development. For a stable version, check the [Releases tab](https://github.com/PianoNic/NeuralDamage/releases).

## ✨ Features

- **Multi-provider AI bots** — Create bots using any model on [OpenRouter](https://openrouter.ai/)
- **Smart response engine** — Bots score each message and only chime in when appropriate
- **Real-time WebSocket sync** — All state changes broadcast instantly across tabs/clients
- **Bot emoji reactions** — Non-responding bots can still react to messages
- **Slash commands** — `/stop`, `/mute`, `/unmute`, `/kick`, `/clear`, `/rename`, `/bots`, `/help`
- **Reply threading** — Reply to specific messages with quoted context
- **Content moderation** — Built-in guardrails prevent bots from engaging with harmful content
- **OIDC authentication** — Login via any OpenID Connect provider (Google, Pocket ID, etc.)
- **Cost protection** — History truncation and token budgets keep API costs in check
- **Docker ready** — Single `docker compose up` to run everything

## 📸 Screenshots

<!-- Add screenshots here -->
<!-- ![Neural Damage Chat](./assets/screenshot-chat.png) -->

## 🐳 Docker Setup

1. **Create a `.env` file:**

```env
# OIDC Provider (Pocket ID, Google, etc.)
OIDC_ISSUER=https://your-oidc-provider.com
OIDC_CLIENT_ID=your-client-id
OIDC_REDIRECT_URI=http://localhost:3000/callback
OIDC_TOKEN_ENDPOINT=https://your-oidc-provider.com/api/oidc/token
OIDC_USERINFO_ENDPOINT=https://your-oidc-provider.com/api/oidc/userinfo
OIDC_JWKS_URI=https://your-oidc-provider.com/.well-known/jwks.json
OIDC_AUTHORIZE_ENDPOINT=https://your-oidc-provider.com/authorize

# App
JWT_SECRET=generate-a-random-secret-here
DATABASE_URL=sqlite+aiosqlite:///./neuraldamage.db
APP_URL=http://localhost:3000

# OpenRouter (AI models)
OPENROUTER_API_KEY=sk-or-v1-your-key-here

# Response engine
RESPONSE_THRESHOLD=0.35
JUDGE_MODEL=google/gemini-2.0-flash-001
```

2. **Start it:**

```bash
docker compose up --build -d
```

The application will be available at `http://localhost:3000`.

Get an OpenRouter API key from [openrouter.ai/keys](https://openrouter.ai/keys).

## 🛠️ Usage

1. Navigate to `http://localhost:3000`
2. Log in with your OIDC provider
3. Create a chat and add some AI bots
4. Start chatting — bots will join in naturally

## 🤖 How Bot Responses Work

When a message is sent, each bot gets a **response-worthiness score** (0.0–1.0). A bot responds if its score exceeds the threshold.

| Signal | Impact |
|--------|--------|
| Direct @mention | +0.6 |
| Name in message | +0.5 |
| Group question (no @mention) | +0.15 |
| Topic relevance | up to +0.3 |
| Spoke < 30s ago | -0.4 |
| Spoke < 2min ago | -0.2 |
| Dominates conversation | -0.15 per extra |
| Bot-to-bot (no @mention) | Nearly 0 |

Multiple responding bots are staggered with random delays. Bot-to-bot chains are capped at depth 3.

## 📋 Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | React 19, TypeScript, Vite, Tailwind CSS v4, shadcn/ui, Zustand |
| Backend | Python 3.13, FastAPI, SQLAlchemy (async), aiosqlite |
| Auth | OIDC + PKCE, JWT |
| Real-time | WebSockets |
| AI | OpenRouter API |
| Deploy | Docker Compose (nginx reverse proxy) |

## 💻 Development

**Backend:**
```bash
cd backend
uv sync
uv run uvicorn app.main:app --reload --port 8000
```

**Frontend:**
```bash
cd frontend
bun install
bun dev
```

The frontend dev server runs on `http://localhost:5173` and proxies `/api` requests to the backend.

### Project Structure

```
NeuralDamage/
├── .env.example
├── docker-compose.yml
├── backend/
│   ├── Dockerfile
│   ├── pyproject.toml
│   └── app/
│       ├── main.py              # FastAPI app, CORS, lifespan
│       ├── config.py            # pydantic-settings
│       ├── database.py          # Async SQLAlchemy engine
│       ├── models/              # SQLAlchemy models
│       ├── schemas/             # Pydantic request/response schemas
│       ├── api/                 # REST + WebSocket endpoints
│       ├── services/            # Auth, bot, response engine
│       └── ws/                  # WebSocket manager + handlers
└── frontend/
    ├── Dockerfile
    ├── nginx.conf               # Reverse proxy config
    ├── package.json
    └── src/
        ├── components/
        │   ├── chat/            # Message list, input, bubbles, reactions
        │   ├── bots/            # Bot manager, creation form
        │   ├── layout/          # App shell, sidebar
        │   └── ui/              # shadcn/ui primitives
        ├── hooks/               # useWebSocket
        ├── stores/              # Zustand (auth, chat, ui)
        ├── lib/                 # API client, auth helpers
        └── pages/               # Home, login, callback
```

---

**Made with ❤️ by [PianoNic](https://github.com/PianoNic)**
