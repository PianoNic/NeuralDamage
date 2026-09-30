# NeuralDamage - working conventions

## AI attribution

None, anywhere: not in commits, PR or issue bodies, comments or git metadata. No `Co-Authored-By:`
trailers, no `🤖 Generated with...`, no claude.ai / claude.com links, no mention of AI assistance.

## Workflow

Never work on `main`. Every change goes:

1. **Issue**: `gh issue create` with at least one label from `.github/labels.yml`.
2. **Branch**: `<type>/<issue#>_PascalCase`, where type is `feat`, `fix`, `refactor`, `docs`, `ci` or `chore`.
   - `feat/27_JevClassifier`
   - `fix/22_FixDeployPath`
3. **PR**: `gh pr create` with a label. CI (`.github/workflows/ci.yml`) must be green.
4. **Squash-merge + delete the branch.**

Labels drive the release notes and the next version (`.github/release-drafter.yml`): `breaking` / `major`
bump major, `feature` bumps minor, everything else patch. Run the Setup Labels workflow if one is missing.

## Commits and PRs

Conventional-commit subject, lower case, one line, saying what the change does for the app:

- `feat: let bots react with emoji to messages they don't answer`
- `fix: restore the model price caps lost in the port`

The squash commit gets the PR number appended by GitHub. The PR title mirrors it; the body explains why,
then `Closes #<issue>`. No test plans, no checklists.

## Layout

```
src/NeuralDamage.Domain/          entities
src/NeuralDamage.Application/     commands, queries, validators (Mediator source generator)
src/NeuralDamage.Infrastructure/  EF Core + PostgreSQL, OpenRouter, bot decision engine, Migrations/
src/NeuralDamage.API/             controllers, SignalR hubs, Dockerfile; serves the SPA from wwwroot
src/NeuralDamage.Frontend/        Angular 22 + Spartan, bun (not in the .slnx; see its CLAUDE.md)
src/NeuralDamage.Tests/           TUnit
```

## Local development

```bash
docker compose -f compose.dev.yml up -d          # Postgres 18 on :5434
dotnet run --project src/NeuralDamage.API         # http://localhost:5012, Swagger at /swagger
cd src/NeuralDamage.Frontend && bun run start     # http://localhost:4200
```

Configuration (connection string, `Oidc:*`, OpenRouter) goes in user secrets; see `docs/dev-setup.md`.

## Tests

```bash
dotnet run --project src/NeuralDamage.Tests                    # TUnit
cd src/NeuralDamage.Frontend && bun run test --watch=false     # Vitest
```

`dotnet test` does not work: the .NET 10 SDK dropped VSTest mode, and TUnit runs as an executable.

## Migrations

The API migrates at boot, so `dotnet ef database update` is never needed. After a model change, or after
pulling upstream:

```bash
dotnet ef migrations has-pending-model-changes --project src/NeuralDamage.Infrastructure --startup-project src/NeuralDamage.API
dotnet ef migrations add <Name> --project src/NeuralDamage.Infrastructure --startup-project src/NeuralDamage.API
```

CI fails when a model change has no migration.

## Generated API client

`src/NeuralDamage.Frontend/src/app/api/` is generated from the API's OpenAPI document. After changing a
controller, DTO or endpoint, run the API (Development, port 5012) and:

```bash
cd src/NeuralDamage.Frontend && bun run apigen
```

Commit the result; CI regenerates it and fails on any diff. Never edit that folder by hand.

## Versioning and releases

`application.properties` at the repo root is the single version source; `src/Directory.Build.props` stamps
it into every assembly. Release Drafter keeps a draft release on every push to `main`. Publishing it runs
`release.yaml`: CI, bump `application.properties` on `main` to the tag, then build and push
`ghcr.io/pianonic/neuraldamage` (`X.Y.Z`, plus `X.Y`, `X` and `latest` when it is the highest non-prerelease).

## Before you claim it works

- `dotnet build NeuralDamage.slnx` is 0 warnings, 0 errors, and the TUnit suite is green.
- `bun run test --watch=false` and `bun run build` pass in `src/NeuralDamage.Frontend`.
- The bun version in `package.json`'s `packageManager` matches `oven/bun:<version>` in the API Dockerfile.
