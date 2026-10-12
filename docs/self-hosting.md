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
| `OpenRouter__DisableReasoning` | `true` | Turn reasoning off on every request: models that take OpenRouter's `reasoning` parameter are sent effort `none`, all others `reasoning.enabled: false`, and models that always reason are refused. Leave it on: reasoning makes replies slower and dearer and does nothing for chat. `false` restores low-effort reasoning and allows those models. |
| `OpenRouter__MaxOutputTokens` | `1500` | Output token budget per bot reply. Reasoning models spend part of it thinking, so leave headroom. |
| `OpenRouter__BaseUrl` | `https://openrouter.ai/api/v1` | Point at a gateway or proxy that speaks the OpenRouter API. |
| `App__TimeZone` | `Europe/Zurich` | IANA time zone for the time the bots see. They get only the day and part of day ("Wednesday afternoon": night until 5, morning until 12, afternoon until 17, evening until 22) and are told not to bring it up unless it matters. An unknown zone falls back to UTC with a warning in the log. Stored timestamps stay UTC. |

Out of the box, bots can only use cheap models whose providers keep no data. A model that fails any of these rules (over a price cap, without a fixed price such as `openrouter/auto`, without a zero-data-retention endpoint, a batch variant, or a model that always reasons while `DisableReasoning` is on) is hidden from the model picker and refused when a bot is created or switched to it. Every reply request also sends the caps as OpenRouter's `provider.max_price` and, with `ZdrOnly`, `provider.zdr`, so a request is never routed to a provider that charges more or retains data.

### Custom providers

Bots can also run on any OpenAI-compatible endpoint next to OpenRouter, for example a gateway that hosts DeepSeek for free. Each provider gets a name of your choice, and its models are addressed as `<Name>:<model>`, e.g. `gateway:sgl/deepseek`. Several providers can be configured side by side.

| Variable | Default | Description |
|---|---|---|
| `Providers__<Name>__BaseUrl` | required | The endpoint's OpenAI-compatible base URL, e.g. `https://gateway.example.com/v1`. |
| `Providers__<Name>__ApiKey` | none | Sent as the bearer token. Leave it out for an endpoint that takes no key. |
| `Providers__<Name>__Models` | everything `/models` lists | Comma-separated list of the models to offer, e.g. `sgl/deepseek`. Set it when the endpoint also lists embedding, reranking or speech models. Without it, the provider's `/models` is read once an hour; a provider that cannot be reached is left out of the picker (with a warning in the log) and tried again after five minutes. |
| `Providers__<Name>__ContextLength` | see below | The context of the provider's models, in tokens. Without it, the `context_length` or `max_model_len` its `/models` gives is used, and failing that the app asks the model once (a request too long for it, with one token of output) and reads the limit from the refusal. When that says nothing, 32k is assumed. |
| `App__DefaultModel` | the first custom model | The model a new bot starts on, e.g. `gateway:sgl/deepseek` or an OpenRouter id. Without it and without a custom provider, the picker starts empty. |

Custom models show in the model picker under the provider's name and as free. The OpenRouter policy above (price caps, ZDR, batch, reasoning) does not apply to them: whoever configured the provider chose it. Replies go to the provider with only the messages, temperature, token budget and `chat_template_kwargs: {"thinking": false, "enable_thinking": false}`, which keeps models that think by default (such as Qwen3) from reasoning; none of OpenRouter's extra fields. Nothing says whether a custom model can see, so its bots get image descriptions like any text-only bot. A bot whose custom model is no longer offered is flagged like one whose OpenRouter model disappeared. Jev and the image describer stay on OpenRouter.

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

### Long chats

A bot reads the latest stretch of the chat word for word: a share of its model's context, at least 12,000 characters (about 50 messages). When the chat outgrows that, the oldest quarter drops out at once, and in the background the default model folds what dropped into a running summary of the chat: who said what, facts people shared about themselves, running jokes, nicknames and open arguments. Every bot reads that summary before the messages, as its memory of the earlier chat. It only changes when a chunk drops, so the prompt cache keeps working in between. When it grows past its cap, it is compacted again. If writing it fails, the old summary stays and the next drop tries again. Clearing the chat clears it too.

The chat owner can also fold the chat on purpose with `/compact`: everything but the newest few messages goes into the summary at once, so the bots' prompt is small from the next reply on. The messages stay visible. Anyone can ask for `/summarize`, a short recap of the chat so far that only they see; the bots never do.

| Variable | Default | Description |
|---|---|---|
| `History__ContextShare` | `0.25` | Share of the model's context the raw history may fill. |
| `History__MaxTokens` | `32000` | Upper cap on the raw history, in tokens (about 4 characters each), so a model with a huge context is not sent huge prompts. |
| `History__MinChars` | `12000` | Lower bound on the raw history, in characters. |
| `History__SummaryMaxChars` | `4000` | Longest summary before it is compacted again. |
| `History__MaxFoldChars` | `60000` | Most characters of dropped messages folded in at once, newest first. |
| `History__SummaryModel` | `App__DefaultModel` | The model that writes the summary and the `/summarize` recap. Without either, `google/gemini-2.5-flash-lite`. |
| `History__CompactKeep` | `10` | Newest messages `/compact` leaves out of the summary. |

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
