# Neural Damage

A group chat application where humans and AI bots have natural conversations. Bots are powered by [OpenRouter](https://openrouter.ai/) (GPT-4, Claude, Llama, Gemini, etc.) and only respond when "response-worthy" — not to every message.

## Features

- **Multi-provider AI bots** — Create bots using any model available on OpenRouter
- **Smart response engine** — Bots score each message for relevance and only chime in when appropriate (direct @mentions, topic relevance, group questions)
- **Real-time WebSocket sync** — All state changes broadcast instantly across tabs/clients
- **Bot emoji reactions** — Non-responding bots can still react to messages with emojis
- **Slash commands** — `/stop`, `/mute`, `/unmute`, `/kick`, `/clear`, `/rename`, `/bots`, `/help`
- **Reply threading** — Reply to specific messages with quoted context
- **Content moderation** — Built-in guardrails prevent bots from engaging with harmful content
- **OIDC authentication** — Login via any OpenID Connect provider (Google, Pocket ID, etc.) using PKCE
- **Cost protection** — History truncation and token budgets keep API costs in check

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | React 19, TypeScript, Vite, Tailwind CSS v4, shadcn/ui, Zustand |
| Backend | Python 3.13, FastAPI, SQLAlchemy (async), aiosqlite |
| Auth | OIDC + PKCE, JWT |
| Real-time | WebSockets |
| AI | OpenRouter API |
| Deploy | Docker Compose (nginx reverse proxy) |

## Quick Start

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) and Docker Compose
- An [OpenRouter API key](https://openrouter.ai/keys)
- An OIDC provider (e.g. Google OAuth, [Pocket ID](https://github.com/pocket-id/pocket-id))

### Setup

```bash
# Clone the repo
git clone https://github.com/YOUR_USERNAME/NeuralDamage.git
cd NeuralDamage

# Configure environment
cp .env.example .env
# Edit .env with your OIDC provider details and OpenRouter API key

# Run with Docker Compose
docker compose up --build
```

The app will be available at **http://localhost:3000**.

### Local Development (without Docker)

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

## Project Structure

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

## How Bot Responses Work

When a human sends a message, each bot in the chat gets a **response-worthiness score** (0.0–1.0). A bot responds if its score exceeds the threshold (default: 0.35).

| Signal | Score Impact |
|--------|-------------|
| Direct @mention | +0.6 |
| Name in message | +0.5 |
| Group question (no @mention) | +0.15 |
| Topic relevance (keyword overlap) | up to +0.3 |
| Spoke < 30s ago | -0.4 |
| Spoke < 2min ago | -0.2 |
| Dominates conversation (>3 of last 10) | -0.15 per extra |
| Random jitter | -0.05 to +0.10 |

Multiple responding bots are staggered with random 1–4s delays. Bot-to-bot chains are capped at 3 depth to prevent runaway conversations.

## Environment Variables

See [`.env.example`](.env.example) for all available configuration options.

## License

MIT
