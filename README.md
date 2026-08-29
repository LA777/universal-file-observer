# UFO — Universal File Observer

UFO is a self-hosted web application for **snapshot-based observation of your file system**. Pick a folder, create a snapshot, and UFO indexes the whole tree — folder structure, per-file SHA-256 hashes, sizes, and the identity of the PC / storage drive / volume it lives on. Snapshots can then be browsed, searched, labeled, compared over time (shared hashes make duplicates and changes detectable), and deleted.

There is no background watcher — observation is explicit, point-in-time snapshotting.

![UFO Dashboard](Screenshot%202026-06-03%20214932.png)

## Tech stack

| Layer | Technology |
|---|---|
| Back-end | ASP.NET Core Web API (**.NET 10**), JWT auth (Bearer), Serilog, Swagger/OpenAPI |
| Database | **SQLite** via **Dapper** (raw SQL, no EF Core, schema created at startup) |
| Front-end | **Angular 21** + Angular Material (standalone bootstrap), served as SPA from `Ufo.Server/wwwroot` |
| Tests | xUnit: unit (Moq/AutoFixture), integration (in-memory SQLite), functional (`WebApplicationFactory`) |

## Solution structure

| Project | Purpose |
|---|---|
| `Ufo.Abstractions` | Shared contracts: entities, DTOs, requests/responses, repository interfaces, options |
| `Ufo.Database` | Dapper repositories, SQLite connection factory, schema DDL (`SqlScripts.cs`) |
| `Ufo.Server` | Web API: controllers, services, JWT, Swagger, SPA hosting (`Program.cs`) |
| `Ufo.UnitTests` | Unit tests |
| `Ufo.IntegrationTests` | Repository tests against real in-memory SQLite |
| `Ufo.FunctionalTests` | End-to-end HTTP tests over the real app |
| `ufo.client` | Angular SPA — see [ufo.client/README.md](ufo.client/README.md) |

## Getting started

### Prerequisites
- .NET 10 SDK
- Node.js (Angular CLI 21: `npm install -g @angular/cli`)

### Run in development (two processes)

Back-end (Swagger UI at `https://localhost:7150/swagger`):

```
cd Ufo.Server
```

```
dotnet run --launch-profile https
```

Front-end dev server with HTTPS + API proxy to the back-end:

```
cd ufo.client
```

```
npm install
```

```
npm start
```

### Run as a single app (production-style)

Build the Angular app and copy it into the server's `wwwroot`:

```
cd c:\GitHub\LA777\universal-file-observer
```

```
powershell -File build-frontend.ps1
```

Then run the server — it serves the SPA and opens the browser at `https://localhost:55000`:

```
dotnet run --project Ufo.Server
```

### First run
1. The SQLite database (`Ufo.Server/ufo.db`) and its schema are created automatically at startup.
2. Open the app — it detects that no user exists and routes you to **Register**.
3. Sign up, log in, go to **Files**, pick a folder, and create your first snapshot.

## Tests

```
dotnet test Ufo.sln
```

Front-end (Karma/Jasmine):

```
cd ufo.client
```

```
npm test
```

## API overview

All endpoints are under `/api` and (except auth/first-run) require a JWT Bearer token:

- `POST /api/auth/signup`, `POST /api/auth/login`, `GET /api/user/is-created`
- `GET /api/FileSystem/root`, `POST /api/FileSystem/folder`, `POST /api/FileSystem/parent` — live browsing
- `POST /api/Snapshot/create`, `GET /api/Snapshot/{id}`, `GET /api/Snapshot/latest`, `GET /api/Snapshot/all/summary`, `DELETE /api/Snapshot/delete/{id}`
- `GET/POST/PUT/DELETE /api/Label/...` — labels and label↔snapshot associations
- `POST /api/Search` — search indexed files/folders by name
- `GET /api/Video?filePath=...` — stream video files with range support

OpenAPI document: `/openapi/v1.json` (Swagger UI in Development).

## Repository conventions

- IDs are **Ulid** everywhere (custom Dapper type handler, JSON converter, and OpenAPI schema filter).
- All data is per-user: every entity carries a `UserId`, enforced in every query.
- AI-generated documentation lives in `_docs/` and is prefixed `AI_` — both are gitignored. See `_docs/AI_CODEBASE_INDEX.md` for a full back-end/front-end code index.
