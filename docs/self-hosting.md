# Self-hosting

Neural Damage runs as two containers: the app (the .NET API, which also serves the web app) and PostgreSQL. It needs an OpenID Connect provider for sign-in and an [OpenRouter](https://openrouter.ai/keys) key for the bots.

## Docker Compose (recommended)

The repository's `compose.yml` builds the image from source:

```bash
git clone https://github.com/PianoNic/NeuralDamage.git && cd NeuralDamage
cp .env.example .env
docker compose up -d --build
```

Open <http://localhost:3000>. The app listens on port 8080 inside the container, published as 3000.

Before exposing it, change the database password `changeme` in `compose.yml`. It appears twice: `POSTGRES_PASSWORD` on the `db` service and the connection string on the `api` service.

Database migrations run on startup, so there is nothing to apply by hand.

## Your OIDC provider

Create a **public** client (authorization code with PKCE, no client secret) and register:

| Setting | Value |
|---|---|
| Redirect URI | `https://your-domain/callback` |
| Post-logout redirect URI | `https://your-domain/` |
| Scopes | `openid profile email` |

Put the issuer URL and client id into `.env` as `Oidc__Authority` and `Oidc__ClientId`. The API validates access tokens against the issuer's discovery document and checks that their audience is the client id, so the provider has to issue tokens for that client. Pocket ID, Authentik and Keycloak all work.

## Configuration

Everything is read from environment variables (the `.env` file next to `compose.yml`). A double underscore separates sections, so `Oidc__Authority` is `Oidc:Authority` in `appsettings.json` or user secrets.

### Sign-in

| Variable | Default | Description |
|---|---|---|
| `Oidc__Authority` | required | Issuer URL of your provider. The web app signs in against it, and the API validates tokens with it. |
| `Oidc__ClientId` | required | Client id of the public client. Also the expected token audience. |
| `Oidc__RedirectUri` | required | Where the provider sends the browser back after sign-in: `<your URL>/callback`. |
| `Oidc__PostLogoutRedirectUri` | required | Where the browser lands after sign-out, usually the app's root URL. |
| `Oidc__Scope` | required | Scopes the web app asks for: `openid profile email`. |
| `Oidc__RequireHttpsMetadata` | | Set to `false` only for a provider served over plain HTTP, such as a local one. |
| `Oidc__InternalAuthority` | not set | How the API reaches the provider when that differs from the public URL, for example a container name on the same Docker network. Tokens keep the public issuer. |
| `Oidc__ValidAudiences` | the client id | Other accepted token audiences, as an array (`Oidc__ValidAudiences__0`, `__1`, ...). |
| `Oidc__NameClaim` | | Claim holding the display name, if your provider does not use the usual one. |

The web app reads the sign-in settings from `GET /api/app` at startup, so none of them are baked into the build. The remaining `Oidc` options come from [Toamaisutaa](https://github.com/PianoNic/Toamaisutaa), the authentication package the API uses.

### Bots (OpenRouter)

| Variable | Default | Description |
|---|---|---|
| `OpenRouter__ApiKey` | required | Your [OpenRouter key](https://openrouter.ai/keys). Bots cannot reply without it. |
| `OpenRouter__MaxPromptPrice` | `0.25` | Highest prompt price a bot's model may have, in $ per million tokens. `0` means no limit. |
| `OpenRouter__MaxCompletionPrice` | `0.60` | Highest completion price a bot's model may have, in $ per million tokens. `0` means no limit. |
| `OpenRouter__ZdrOnly` | `true` | Only offer models with a zero-data-retention endpoint, and only route requests to such endpoints. |
| `OpenRouter__ExcludeBatchModels` | `true` | Hide `:batch` model variants, which answer asynchronously and are useless in a live chat. |
| `OpenRouter__DisableReasoning` | `true` | Turn reasoning off: models that take OpenRouter's `reasoning` parameter are sent effort `none`, and models that always reason are refused. `false` restores low-effort reasoning and allows those models. |
| `OpenRouter__MaxOutputTokens` | `1500` | Output token budget per bot reply. Reasoning models spend part of it thinking, so leave headroom. |
| `OpenRouter__BaseUrl` | `https://openrouter.ai/api/v1` | Point at a gateway or proxy that speaks the OpenRouter API. |
| `App__TimeZone` | `Europe/Zurich` | IANA time zone for the time the bots see. They get only the day and part of day ("Wednesday afternoon": night until 5, morning until 12, afternoon until 17, evening until 22) and are told not to bring it up unless it matters. An unknown zone falls back to UTC with a warning in the log. Stored timestamps stay UTC. |

Out of the box, bots can only use cheap models whose providers keep no data. A model that fails any of these rules (over a price cap, without a fixed price such as `openrouter/auto`, without a zero-data-retention endpoint, a batch variant, or a model that always reasons while `DisableReasoning` is on) is hidden from the model picker and refused when a bot is created or switched to it. Every reply request also sends the caps as OpenRouter's `provider.max_price` and, with `ZdrOnly`, `provider.zdr`, so a request is never routed to a provider that charges more or retains data.

### Images

People can attach PNG, JPEG, WebP and GIF images to a message. Bots on a model that can see get the picture itself. For every other bot, a describer model writes a description of each image once, right after upload, and those bots (and the ranking below) read that instead.

| Variable | Default | Description |
|---|---|---|
| `OpenRouter__VisionModel` | `google/gemini-2.5-flash-lite` | The describer. It is held to the same policy as the bots (price caps, zero data retention, reasoning off), and `:free` variants are never used. |
| `Attachments__Path` | `data/attachments` | Where uploads are stored, relative to the app. `compose.yml` mounts the `attachments` volume there. Deleting or clearing a chat deletes its files. |
| `Attachments__MaxBytes` | `10485760` | Largest image, in bytes (10 MB). |
| `Attachments__MaxPerMessage` | `4` | Most images on one message. |
| `Attachments__DescriptionWaitSeconds` | `20` | How long a reply waits for a description that is still being written, before text-only bots answer with just "[image from alice]". |

Images are only served to members of their chat.

### Rate limits

Each signed-in user gets a fixed window per endpoint; past it the API answers `429` and the app shows why.

| Variable | Default | Description |
|---|---|---|
| `RateLimits__Messages__PermitLimit` / `__WindowSeconds` | `20` / `60` | Messages sent. Each one can wake paid bots. |
| `RateLimits__Uploads__PermitLimit` / `__WindowSeconds` | `20` / `60` | Images uploaded. Each one is described by a paid model. |
| `RateLimits__BotCreation__PermitLimit` / `__WindowSeconds` | `10` / `600` | Bots created. |

A `PermitLimit` of `0` turns that limit off.

### Who replies (bot ranking)

Every message, from a person or a bot, gets one call to Jev on the OpenRouter Decisions API. It asks, for each bot in the chat, what that bot would do going only by its persona: reply, react with 😂, ❤️, 😮 or 👍, or stay quiet. Muted bots and bots under `/stop` are left out. A bot replies when Jev chose `reply` with at least the reply threshold, and reacts when it chose a reaction with at least the react threshold. Every bot that replies starts at the same time, and each reply is itself a message the bots can answer, up to three hops.

A person who @mentions bots, or replies to a bot's message, is directing it: only those bots may reply, and Jev still decides whether each of them replies, reacts or stays quiet. The other bots are asked as usual but cannot reply; one that Jev would have had reply takes its likeliest reaction instead if that clears the react threshold, and stays quiet otherwise. A bot @mentioning another bot is left to Jev and the conversation health.

| Variable | Default | Description |
|---|---|---|
| `BotRanking__ApiKey` | `OpenRouter__ApiKey` | Key for the Decisions API. It lives on the same OpenRouter account, so it normally falls back to the main key. |
| `BotRanking__Model` | `~typesafe/jev-latest` | Decision model. |
| `BotRanking__Endpoint` | `https://openrouter.ai/api/alpha/decisions` | Decisions API endpoint. |
| `BotRanking__ReplyThreshold` | `0.6` | Lowest probability of `reply` for a bot to write. |
| `BotRanking__ReactThreshold` | `0.5` | Lowest probability of the chosen reaction for a bot to react. |
| `BotRanking__MaxReplies` | `5` | Safety cap on bots replying to one message, the likeliest first. Guards cost in chats with many bots. |
| `BotRanking__Emojis__react_laugh` / `__react_love` / `__react_wow` / `__react_thumbs` | `😂` / `❤️` / `😮` / `👍` | The emoji each reaction puts on the message. |
| `BotRanking__CautiousHealth` | `0.5` | From this conversation health score, bots answering a bot need `CautiousReplyThreshold` and at most `CautiousMaxReplies` of them reply. |
| `BotRanking__CautiousReplyThreshold` | `0.85` | Reply threshold in that band. |
| `BotRanking__CautiousMaxReplies` | `1` | Most bots replying to a bot in that band. |
| `BotRanking__SilentHealth` | `1.0` | From this score, bots stop answering bots and only react, until a person writes. |
| `BotRanking__HumansActiveMinutes` | `5` | A person who wrote within this many minutes counts as active, for the health question. |
| `BotRanking__TimeoutSeconds` | `5` | How long to wait for Jev. |
| `Bots__MaxBotMessagesPerPersonMessage` | `10` | Safety net: after this many bot messages since a person last wrote, no bot replies until a person writes again. |

The same call also scores the conversation's health, from 0 ("people are in the conversation and the bots add to it") through 1 ("the bots are mostly talking among themselves, but it is still on topic") to 2 ("the bots are going in circles or drowning out the people"), given how many bot messages followed the last person's message and how long ago that was. The score only holds back bots answering other bots; a person's message is always answered normally. Every decision logs the score, so the boundaries can be tuned.

Without a key, or when a call fails or times out, only the bots that are @mentioned or replied to by a person answer, nobody reacts, and bot messages get no bot replies.

### Database

| Variable | Default | Description |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | set in `compose.yml` | PostgreSQL connection string. Only needed in `.env` when you run the app outside the bundled compose file. |

## Behind a reverse proxy

Forward everything to the app's port. Live updates use SignalR on `/hubs/chat` and `/hubs/user`, so the proxy has to pass WebSocket upgrades. Set `Oidc__RedirectUri` and `Oidc__PostLogoutRedirectUri` to the public HTTPS URL, and register the same URLs with your provider.

`GET /api/health` answers `200` without signing in; the compose file uses it as the container health check.

## Updating

```bash
git pull
docker compose up -d --build
```

Migrations for the new version run when the container starts. The database lives in the `db-data` volume and uploaded images in the `attachments` volume; both survive rebuilds.
