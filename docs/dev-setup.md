# Development Environment Setup

## Prerequisites

- Git
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (v20+) or [Bun](https://bun.sh/)
- PostgreSQL 17+ (or use Docker)

## Setting Up the Environment

### 1. Clone the Repository

```bash
git clone https://github.com/PianoNic/NeuralDamage.git
cd NeuralDamage
```

### 2. Install EF Core CLI Tools

```bash
dotnet tool install --global dotnet-ef
```

### 3. Database Setup

#### Option A: Using Docker (Recommended)

```bash
docker compose -f compose.dev.yml up -d
```

This starts a PostgreSQL 18 instance with the following credentials:

| Setting  | Value          |
|----------|----------------|
| Host     | `localhost`    |
| Port     | `5434`         |
| Database | `neuraldamage` |
| Username | `postgres`     |
| Password | `postgres`     |

#### Option B: Local PostgreSQL

Create a database named `neuraldamage` in your existing PostgreSQL installation.

### 4. Configure API Credentials

#### Option A: Using User Secrets (Recommended for Development)

```bash
cd src/NeuralDamage.API
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5434;Database=neuraldamage;Username=postgres;Password=postgres"
dotnet user-secrets set "Oidc:Authority" "https://your-oidc-provider.com"
dotnet user-secrets set "Oidc:ClientId" "your-client-id"
dotnet user-secrets set "Oidc:RedirectUri" "http://localhost:4200/callback"
dotnet user-secrets set "Oidc:PostLogoutRedirectUri" "http://localhost:4200/"
dotnet user-secrets set "Oidc:Scope" "openid profile email"
dotnet user-secrets set "Oidc:RequireHttpsMetadata" "false"
```

**Visual Studio Users:**

Right-click on `NeuralDamage.API` project in Solution Explorer, select **Manage User Secrets**, and replace the contents with:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5434;Database=neuraldamage;Username=postgres;Password=postgres"
  },
  "Oidc": {
    "Authority": "https://your-oidc-provider.com",
    "ClientId": "your-client-id",
    "RedirectUri": "http://localhost:4200/callback",
    "PostLogoutRedirectUri": "http://localhost:4200/",
    "Scope": "openid profile email",
    "RequireHttpsMetadata": false
  }
}
```

#### Option B: Using .env File

Create a `.env` file in the project root:

```env
# Database
ConnectionStrings__DefaultConnection=Host=localhost;Port=5434;Database=neuraldamage;Username=postgres;Password=postgres

# OIDC Provider
Oidc__Authority=https://your-oidc-provider.com
Oidc__ClientId=your-client-id
Oidc__RedirectUri=http://localhost:4200/callback
Oidc__PostLogoutRedirectUri=http://localhost:4200/
Oidc__Scope=openid profile email
Oidc__RequireHttpsMetadata=false
```

To view all configured secrets:

```bash
dotnet user-secrets list --project src/NeuralDamage.API
```

### 5. Install Dependencies

#### Backend

```bash
dotnet restore
```

#### Frontend

```bash
cd frontend
bun install
```

### 6. Apply Database Migrations

```bash
dotnet ef database update --project src/NeuralDamage.Infrastructure --startup-project src/NeuralDamage.API
```

## Running the Application

### 1. Start the Backend

```bash
dotnet run --project src/NeuralDamage.API
# Backend will be available at http://localhost:5012
# Swagger UI at http://localhost:5012/swagger
```

### 2. Start the Frontend

```bash
cd frontend
bun dev
# Frontend will be available at http://localhost:4200
```

### Using Docker Compose

For a complete environment with database:

```bash
# Copy and configure environment variables
cp .env.example .env
# Edit .env with your OIDC credentials

# Start all services
docker compose up --build

# Access at http://localhost:3000
```

## Development Tools

### Entity Framework Commands

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> --project src/NeuralDamage.Infrastructure --startup-project src/NeuralDamage.API

# Apply migrations to database
dotnet ef database update --project src/NeuralDamage.Infrastructure --startup-project src/NeuralDamage.API

# Remove last migration (if not yet applied)
dotnet ef migrations remove --project src/NeuralDamage.Infrastructure --startup-project src/NeuralDamage.API
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Building for Production

```bash
# Build backend
dotnet publish src/NeuralDamage.API -c Release -o ./publish

# Build frontend
cd frontend
bun run build
```

## Project Architecture

The backend follows **Clean Architecture**:

| Layer | Project | Purpose |
|-------|---------|---------|
| Domain | `NeuralDamage.Domain` | Entity models, no dependencies |
| Application | `NeuralDamage.Application` | Interfaces, DTOs, mappers, commands/queries |
| Infrastructure | `NeuralDamage.Infrastructure` | EF Core DbContext, PostgreSQL, service implementations |
| API | `NeuralDamage.API` | ASP.NET Core host, controllers, middleware, DI registration |

### Auth Flow

1. Frontend redirects the user to the OIDC provider for login
2. OIDC provider issues a JWT access token
3. Frontend sends the token in `Authorization: Bearer <token>` headers
4. ASP.NET Core validates the JWT against the OIDC provider's authority
5. On first login, `POST /api/auth/sync` upserts the user in the local database

## Troubleshooting

### Common Issues

**PostgreSQL Connection Failed**

- Ensure PostgreSQL is running on port 5432
- Check credentials in user secrets or .env file
- Verify the `neuraldamage` database exists

**OIDC Authentication Not Working**

- Verify Authority URL is correct and reachable
- Check if Client ID matches your OIDC provider configuration
- Ensure `RequireHttpsMetadata` is `false` for local development with HTTP providers

**EF Migrations Hang After "Build succeeded"**

- The `IDesignTimeDbContextFactory` in `NeuralDamageDbContext` should handle this
- If it still hangs, check for stale `dotnet` processes in Task Manager

**Port Already in Use**

- Backend default: 5012 (change in `Properties/launchSettings.json`)
- Frontend default: 4200
- Database default: 5434 (mapped from container port 5432)

## Additional Resources

- [Entity Framework Core Docs](https://docs.microsoft.com/en-us/ef/core/)
- [ASP.NET Core Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [OpenRouter API](https://openrouter.ai/docs)
