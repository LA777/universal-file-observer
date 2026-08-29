# UfoClient — UFO Front-end

Angular **21** single-page application for UFO (Universal File Observer). Built with Angular Material + CDK, it provides login/registration and a tabbed dashboard for browsing the file system, creating and inspecting snapshots, and managing labels.

## How it's wired

- **Bootstrap:** standalone components via `src/main.ts` (`bootstrapApplication`). The legacy `app.module.ts` / `app-routing.module.ts` files are **not** the active bootstrap path — routes, providers, and the HTTP interceptor are configured in `main.ts`.
- **Auth:** JWT stored in `localStorage` (`auth_token`); `JwtInterceptor` attaches `Authorization: Bearer <token>` to every request; `AuthGuard` protects `/dashboard`.
- **API:** all calls use relative `/api/...` paths. In development, `src/proxy.conf.js` proxies them to the .NET back-end (`https://localhost:7150` by default). In production the app is served by `Ufo.Server` from the same origin.

## Routes

| Path | Component | Guard |
|---|---|---|
| `/login` | LoginComponent | — |
| `/register` | RegisterComponent | — |
| `/dashboard` | DashboardComponent | AuthGuard |
| `/` | redirect to `/login` | — |

## Structure (`src/app/`)

- `components/` — `dashboard` (tab host), `files` + `file-panel` (dual-pane file browser), `folder-details`, `folder-tree`, `snapshot`, `snapshots`, `login`, `register`, `dialog`, `forecast` (scaffold demo)
- `services/` — `AuthService` (`/api/auth`, `/api/user`), `FileService` (`/api/filesystem`), `SnapshotService` (`/api/snapshot`), `TabChangeService`, `AuthInitService`
- `guards/` — `AuthGuard`
- `interceptors/` — `JwtInterceptor` (active), `AuthInterceptor` (legacy, only referenced by the inactive NgModule)
- `models/models.ts` — shared interfaces (FsItem, Folder, File, Snapshot, Label, Pc, StorageDrive, Volume, …)

## Environment setup

`src/environments/environment.ts` is **gitignored**. On a fresh clone, copy the template first:

```
copy src\environments\environment.example.ts src\environments\environment.ts
```

Optionally fill `devLogin` to prefill the login form during development.

## Development server

```
npm install
```

```
npm start
```

`npm start` runs `ng serve --ssl` on `https://127.0.0.1:4200` with the API proxy. Start the .NET back-end (`dotnet run --project ..\Ufo.Server --launch-profile https`) so `/api` calls succeed.

## Build & deploy into the .NET server

```
npm run build
```

Output goes to `dist/ufo.client`. To deploy into `Ufo.Server/wwwroot` (so the back-end serves the SPA), run the root-level script instead:

```
powershell -File ..\build-frontend.ps1
```

Verify the result with `..\check-wwwroot.bat`.

## Tests

```
npm test
```

Karma + Jasmine (Chrome, coverage report enabled). Only a couple of spec files exist so far (`app.component.spec.ts`, `forecast.component.spec.ts`).

## Scaffolding

Standard Angular CLI applies, e.g. `ng generate component components/my-component`. New components should be **standalone** and registered via routes/imports in `main.ts` — do not add them to the legacy `app.module.ts`.
