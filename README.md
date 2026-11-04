# MahjongTutor

An American Mahjong coach for the web — it doesn't just track your game, it explains the moves you missed and the ones that will get you in trouble.

![status](https://img.shields.io/badge/status-scaffold-orange)

Play full National Mah Jongg League–rules games against bots or up to 3 friends over your home network, with a coach that grades your discards, warns about dangerous tiles, and reviews every game afterward. Enter your own yearly card — no subscription, and no card data ships with the app.

## Features (roadmap)

| | |
|---|---|
| Full NMJL rules | Deal, Charleston (both passes + courtesy), calls, joker rules & exchanges, dead hands, scoring |
| Coach | Hand-fit rankings, discard grading (`!`/`✓`/`?!`/`?`/`??`), missed-call alerts, opponent-hand inference & blocking advice |
| Modes | Solo vs bots, network play up to 4 humans (bots fill seats), analysis tool, practice drills |
| Cards | Guided editor + JSON import/export for yearly NMJL cards you own |
| Tilesets | Bundled public-domain set + upload your own |
| Extras | Post-game review, stats & history, save/resume, optional Ollama narration, local neural-TTS voice |

Development is phased — each phase is a [GitHub issue](https://github.com/JRAdams472/mahjong-tutor/issues?q=label%3Aphase) landed via PR.

## Stack

- **UI**: Blazor Server (Interactive Server render mode) — real-time via SignalR
- **Backend**: C# / .NET 10
- **DB**: PostgreSQL 17 (EF Core + Npgsql)
- **Voice (later)**: Piper local neural TTS sidecar
- **Narration (later)**: optional local Ollama

## Run it (Docker)

```powershell
docker compose up --build
```

Then browse to `http://localhost:8420` — or `http://<your-LAN-IP>:8420` from any other machine on the network (e.g. your wife's computer). The compose stack starts Postgres automatically.

> **Security note for public/self-hosted use:** this is a LAN/home-lab app with no user accounts. The default `POSTGRES_PASSWORD` is `mahjong` (override it via env). **No National Mah Jongg League card data ships** — cards are copyrighted; users enter their own.

## Develop

```powershell
# terminal 1: database only
docker compose up -d postgres

# terminal 2: the app (hot reload)
dotnet watch --project src/MahjongTutor.Server
```

Postgres is published on host port **5433** (not the default 5432, to avoid colliding with a local Postgres install) — point EF tools / DB clients at `localhost:5433`. **EF Core migrations apply automatically at app startup.** To iterate on the schema:

```powershell
dotnet ef migrations add <Name> -p src/MahjongTutor.Server -o Data/Migrations
```

Set `POSTGRES_PASSWORD` in your environment or a `.env` file to change the default dev password (`mahjong`). For anything beyond a home network, pick a real password.

## Tests

```powershell
dotnet test
```

`MahjongTutor.Core` (tiles, card model, rules, coach, bots) is pure logic with no web/DB dependencies — that's where the xUnit coverage lives.

## Layout

```
src/MahjongTutor.Core/    Tiles/ Card/ Rules/ Coach/ Bots/   (pure logic)
src/MahjongTutor.Server/  Blazor app, EF Core, lobby/rooms   (the web app)
tests/                    xUnit: Core.Tests + Server.Tests
docker-compose.yml        app + postgres (+ piper-tts later)
```

## License

MIT — see [LICENSE](LICENSE).

> The NMJL card is the League's copyrighted work. This app never ships card data: you enter the card you own, it stays in your database.
