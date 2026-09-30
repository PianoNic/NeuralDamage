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
| `OpenRouter__MaxPromptPrice` | `0` (no limit) | Highest prompt price a bot's model may have, in $ per million tokens. |
| `OpenRouter__MaxCompletionPrice` | `0` (no limit) | Highest completion price a bot's model may have, in $ per million tokens. |
| `OpenRouter__MaxOutputTokens` | `1500` | Output token budget per bot reply. Reasoning models spend part of it thinking, so leave headroom. |
| `OpenRouter__BaseUrl` | `https://openrouter.ai/api/v1` | Point at a gateway or proxy that speaks the OpenRouter API. |

With either price cap set, models over it (and models without a fixed price, such as `openrouter/auto`) are hidden from the model picker and refused when a bot is created or switched to them. Every reply request also sends the caps as OpenRouter's `provider.max_price`, so a request is never routed to a provider charging more.

### Who replies (bot ranking)

Every message goes through three tiers. Hard rules decide the clear cases (a bot is @mentioned, muted, or the chat is stopped), a weighted score settles the obvious rest, and the bots still undecided are ranked by Jev on the OpenRouter Decisions API. All of these are optional.

| Variable | Default | Description |
|---|---|---|
| `BotRanking__ApiKey` | `OpenRouter__ApiKey` | Key for the Decisions API. It lives on the same OpenRouter account, so it normally falls back to the main key. |
| `BotRanking__Model` | `~typesafe/jev-latest` | Ranking model. |
| `BotRanking__Endpoint` | `https://openrouter.ai/api/alpha/decisions` | Decisions API endpoint. |
| `BotRanking__Threshold` | `0.6` | Lowest probability for a bot to reply. |
| `BotRanking__MaxResponders` | `2` | Most bots the ranking lets reply to one message. |
| `BotRanking__TimeoutSeconds` | `5` | How long to wait for a ranking. |

Without a key, or when a ranking fails or times out, the weighted scores decide on their own.

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

Migrations for the new version run when the container starts. The database lives in the `db-data` volume and survives rebuilds.
