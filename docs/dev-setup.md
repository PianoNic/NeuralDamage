# Development

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), [Bun](https://bun.sh/) and Docker (for the database), plus an OIDC provider and an OpenRouter key as described in [Self-hosting](self-hosting.md#your-oidc-provider).

## Run from source

### 1. Database

```bash
docker compose -f compose.dev.yml up -d
```

PostgreSQL on `localhost:5434`, database `neuraldamage`, user `postgres`, password `postgres`.

### 2. API settings

Keep them in user secrets so nothing lands in the repository:

```bash
cd src/NeuralDamage.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5434;Database=neuraldamage;Username=postgres;Password=postgres"
dotnet user-secrets set "Oidc:Authority" "https://your-oidc-provider.com"
dotnet user-secrets set "Oidc:ClientId" "your-client-id"
dotnet user-secrets set "Oidc:RedirectUri" "http://localhost:4200/callback"
dotnet user-secrets set "Oidc:PostLogoutRedirectUri" "http://localhost:4200/"
dotnet user-secrets set "Oidc:Scope" "openid profile email"
dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-v1-your-key-here"
```

Register `http://localhost:4200/callback` as a redirect URI with your provider. Every other setting is optional; see the [configuration reference](self-hosting.md#configuration). `dotnet user-secrets list` shows what is set.

### 3. Start both halves

```bash
dotnet run --project src/NeuralDamage.API
```

The API runs on <http://localhost:5012> in the `Development` environment, applies migrations on startup and serves Swagger UI at <http://localhost:5012/swagger>.

```bash
cd src/NeuralDamage.Frontend
bun install
bun run start
```

The web app runs on <http://localhost:4200> and talks to the API on port 5012, which allows it through CORS in development.

## Tests

The tests use [TUnit](https://tunit.dev/), which runs as an executable:

```bash
dotnet run --project src/NeuralDamage.Tests
```

Frontend unit tests: `bun run test` in `src/NeuralDamage.Frontend`.

## Generated API client

The frontend's services and models in `src/NeuralDamage.Frontend/src/app/api` are generated from the API's OpenAPI document. After changing a controller or DTO, start the API and regenerate them:

```bash
cd src/NeuralDamage.Frontend
bun run apigen
```

It reads `http://localhost:5012/swagger/v1/swagger.json` (see `openapitools.json`). Commit the generated files with the change.

## Migrations

Migrations live in `src/NeuralDamage.Infrastructure/Migrations` and are applied when the API starts. To add one after changing an entity:

```bash
dotnet tool install --global dotnet-ef   # once
dotnet ef migrations add <Name> --project src/NeuralDamage.Infrastructure --startup-project src/NeuralDamage.API
```

## Production build

```bash
docker compose up -d --build
```

`src/NeuralDamage.API/Dockerfile` builds the Angular app with Bun and copies it into the API's `wwwroot`, so one image serves both on port 8080.

## Architecture

The API follows Clean Architecture; controllers stay thin and send Mediator commands and queries.

| Project | Purpose |
|---|---|
| `NeuralDamage.Domain` | Entities (chats, members, bots, messages, reactions, users). |
| `NeuralDamage.Application` | Commands, queries and validators, including the slash commands. |
| `NeuralDamage.Infrastructure` | EF Core and PostgreSQL, the OpenRouter client, the bot decision engine and the background reply queue. |
| `NeuralDamage.API` | ASP.NET Core host: controllers, SignalR hubs (`/hubs/chat`, `/hubs/user`), authentication, and the SPA in production. |
| `NeuralDamage.Frontend` | Angular 22 app with Spartan UI and ngx-prompt-kit. |
| `NeuralDamage.Tests` | TUnit tests. |

### Sign-in

1. The web app reads the OIDC settings from `GET /api/app` and sends the user to the provider.
2. The provider returns to `/callback` with a code, which the web app exchanges for an access token (PKCE).
3. Every API call and the SignalR connection carry that token; SignalR passes it in the query string, which is only accepted on `/hubs`.
4. [Toamaisutaa](https://github.com/PianoNic/Toamaisutaa) validates it against the provider, and the first request from a new user creates their row in the database.

## Troubleshooting

- **Sign-in loops or 401s**: check that `Oidc:Authority` is reachable from the API and that the token's audience is `Oidc:ClientId`. For a provider on plain HTTP, set `Oidc:RequireHttpsMetadata` to `false`.
- **Bots never reply**: check `OpenRouter:ApiKey`, and that the bot's model is under the price caps if you set any.
- **Port in use**: the API port is in `src/NeuralDamage.API/Properties/launchSettings.json`; the database port is in `compose.dev.yml`.
