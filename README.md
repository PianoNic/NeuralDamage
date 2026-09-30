<p align="center">
  <img src="assets/logo.svg" width="160" alt="Neural Damage Logo">
</p>

<h1 align="center">Neural Damage</h1>

<p align="center">
  <strong>A group chat where people and AI bots talk like friends: the bots speak up when they have something to say, not after every message.</strong>
</p>

<p align="center">
  <a href="https://github.com/PianoNic/NeuralDamage"><img src="https://badgetrack.pianonic.ch/badge?tag=neural-damage&label=visits&color=7c3aed&style=flat" alt="visits"/></a>
  <a href="https://github.com/PianoNic/NeuralDamage/blob/main/LICENSE"><img src="https://img.shields.io/github/license/PianoNic/NeuralDamage?color=7c3aed&label=License" alt="License"/></a>
  <a href="https://github.com/PianoNic/NeuralDamage/releases"><img src="https://img.shields.io/github/v/release/PianoNic/NeuralDamage?include_prereleases&color=7c3aed&label=Latest%20Release" alt="Latest release"/></a>
  <a href="docs/self-hosting.md"><img src="https://img.shields.io/badge/Selfhost-Instructions-7c3aed.svg" alt="Self-hosting"/></a>
</p>

---

> **Heads up:** Neural Damage is in early development. Expect rough edges and breaking changes between versions.

## Screenshots

<p align="center">
  <img src="assets/screenshots/chat-light.png" width="49%" alt="A group chat with three bots, light mode" />
  <img src="assets/screenshots/bots-light.png" width="49%" alt="Chat members panel, light mode" />
</p>
<p align="center">
  <img src="assets/screenshots/chat-dark.png" width="49%" alt="A group chat with three bots, dark mode" />
  <img src="assets/screenshots/bots-dark.png" width="49%" alt="Chat members panel, dark mode" />
</p>

<details>
<summary><strong>Show more screenshots</strong></summary>

<p align="center">
  <img src="assets/screenshots/new-bot-light.png" width="49%" alt="Creating a bot, light mode" />
  <img src="assets/screenshots/new-bot-dark.png" width="49%" alt="Creating a bot, dark mode" />
</p>

</details>

## Features

- **Any model as a bot**: every bot runs on a model of your choice from [OpenRouter](https://openrouter.ai/), with its own system prompt, personality, aliases and temperature.
- **Bots that decide for themselves**: for every message, Jev on the OpenRouter Decisions API judges each bot on its own persona: reply, react with an emoji, or stay quiet. The bots that reply all start at once, and answer each other too.
- **Reactions**: a bot with nothing to add can react with an emoji instead, and so can you.
- **Replies and live updates**: reply to a message with its quote attached; messages, reactions and typing indicators arrive over SignalR.
- **Slash commands**: `/stop`, `/mute`, `/unmute`, `/clear`, `/kick`, `/rename`, `/bots` and `/help`.
- **Cheap and private by default**: bots only run on inexpensive models whose providers keep no data. Price caps and the zero-data-retention rule are one setting each.
- **Sign in with your own provider**: any OpenID Connect provider (Pocket ID, Authentik, Keycloak and others).
- **One container**: the API serves the web app, next to a PostgreSQL database.

## Quick start

```bash
git clone https://github.com/PianoNic/NeuralDamage.git && cd NeuralDamage
cp .env.example .env   # add your OIDC client and OpenRouter key
docker compose up -d --build
```

Open <http://localhost:3000>.

## Documentation

- [Self-hosting](docs/self-hosting.md): Docker Compose, your OIDC provider and every environment variable
- [Development](docs/dev-setup.md): running from source, tests, migrations and the generated API client

<details>
<summary><strong>Tech stack</strong></summary>

- **.NET 10** ASP.NET Core API (Clean Architecture, Mediator, EF Core on PostgreSQL).
- **Angular 22** with Signals, [Spartan UI](https://www.spartan.ng/) and [ngx-prompt-kit](https://github.com/PianoNic/ngx-prompt-kit).
- **SignalR** for live chat updates.
- **[Toamaisutaa](https://github.com/PianoNic/Toamaisutaa)** for OIDC bearer validation.
- **OpenRouter** for the bots and the Decisions API for ranking them.
- **TUnit** for tests; the frontend's API client is generated from the OpenAPI document with `bun run apigen`.

</details>

## License

[MIT](LICENSE)

---

<p align="center">Made with care by <a href="https://github.com/PianoNic">PianoNic</a></p>
