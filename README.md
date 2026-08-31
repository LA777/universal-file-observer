# UFO — Universal File Observer

UFO is a self-hosted web application for **snapshot-based observation of your file system**. Pick a folder, create a snapshot, and UFO indexes the whole tree — folder structure, per-file SHA-256 hashes, sizes, and the identity of the PC / storage drive / volume it lives on. Snapshots can then be browsed, searched, labeled, compared over time (shared hashes make duplicates and changes detectable), and deleted.

There is no background watcher — observation is explicit, point-in-time snapshotting.

![UFO Dashboard](Screenshot%202026-06-03%20214932.png)

## Tech stack

| Layer | Technology |
|---|---|
| Back-end | ASP.NET Core Web API (**.NET 10**), JWT auth (Bearer), Serilog, Swagger/OpenAPI |
| Database | **SQLite** via **Dapper** (raw SQL, no EF Core, schema created at startup) |
| Front-end | **Angular 22** + Angular Material (standalone bootstrap), served from `Ufo.Server/wwwroot` or embedded in the desktop executable |
| Packaging | Single self-contained `ufo.exe` (Windows tray app) and a Linux container image, from one codebase |
| Tests | xUnit: unit (Moq/AutoFixture), integration (in-memory SQLite), functional (`WebApplicationFactory`) |

## Solution structure

| Project | Purpose |
|---|---|
| `Ufo.Abstractions` | Shared contracts: entities, DTOs, requests/responses, repository interfaces, options |
| `Ufo.Database` | Dapper repositories, SQLite connection factory, schema DDL (`SqlScripts.cs`) |
| `Ufo.Server` | Web API: controllers, services, JWT, Swagger, SPA hosting. `Hosting/UfoHost.cs` is the shared composition root; `Program.cs` is the headless/container entry point |
| `Ufo.Platform.Windows` | Windows-only system information (WMI + registry). Referenced by the desktop application, never by the container |
| `Ufo.Desktop` | Windows tray application (`net10.0-windows`, WinForms) — publishes as a single `ufo.exe` |
| `Ufo.UnitTests` | Unit tests |
| `Ufo.IntegrationTests` | Repository tests against real in-memory SQLite |
| `Ufo.FunctionalTests` | End-to-end HTTP tests over the real app |
| `ufo.client` | Angular SPA — see [ufo.client/README.md](ufo.client/README.md) |

## Getting started

### Prerequisites
- .NET 10 SDK
- Node.js `^22.22.3 || ^24.15.0 || >=26.0.0` — the range Angular CLI 22 enforces (odd-numbered releases such as 23.x and 25.x are rejected). CLI: `npm install -g @angular/cli`
- A JWT signing key. None is committed, and the server refuses to start without one, so set it once per clone:

```
dotnet user-secrets set "JWT:Key" "$(openssl rand -hex 32)" --project Ufo.Server
```

On PowerShell, generate the value with `-join ((1..32) | ForEach-Object { '{0:x2}' -f (Get-Random -Max 256) })`.

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

This entry point never generates a signing key, whether it is a local run or a published
headless deployment: it would have nowhere to put one that survives an upgrade, and a
network-reachable host quietly inventing a key is worse than one that refuses to start.
Set `JWT:Key` in user-secrets (see [Prerequisites](#prerequisites)) or supply `JWT__Key`.
Only the installed desktop application generates its own.

`https://localhost:55000` is served with a certificate the application generates and stores
for itself on first run, rather than the ASP.NET Core development certificate. Your browser
will warn about it until you trust it — see
[TLS and certificates](#tls-and-certificates), which applies to every host, not just the
container.

### Run as a Windows desktop application (single `ufo.exe`)

`Ufo.Desktop` hosts the same web application behind a notification-area icon. Publishing
builds the Angular bundle, embeds it into the executable, and produces one self-contained
file (~67 MB) plus an editable `appsettings.json`:

```
cd c:\GitHub\LA777\universal-file-observer
```

```
.\publish-desktop.ps1
```

The database, logs, generated machine id, generated JWT signing key and the key protecting
the stored TLS certificate live in `%LOCALAPPDATA%\UFO`, not beside the executable.

No signing key ships in the executable: one baked in would be shared by every
installation, so a single extracted copy could forge tokens against all of them. On first
run each installation generates its own into `%LOCALAPPDATA%\UFO\jwt-signing-key` and
reuses it afterwards, which is what keeps users signed in across restarts and upgrades.
Deleting that file signs everyone out and a new key is generated. Setting `JWT__Key` (or a
`JWT:Key` in `appsettings.json`) takes precedence and disables the generation.

### Run in a container

The container image is headless: no browser launch, HTTP only (terminate TLS upstream),
and file-system access restricted to what you mount in.

```
cd /path/to/universal-file-observer
```

```
docker compose up -d --build
```

Compose reads these from the environment or a local `.env` file:

| Variable | Required | Purpose |
|---|---|---|
| `UFO_JWT_KEY` | yes | JWT signing key. Compose fails fast if unset, and so does the application: no key ships in `appsettings.json`, because one baked into the image would be shared by every copy of it. |
| `UFO_LIBRARY_PATH` | no (`./library`) | Host folder to index. Mounted **read-only** at `/library`, which is the only path the browse, search and video endpoints will serve. |
| `UFO_MACHINE_ID` | no | Identity recorded against each snapshot. Worth setting: a container's own `/etc/machine-id` is regenerated whenever the container is recreated, which fragments snapshot history. |
| `UFO_ENABLE_HTTPS` | no (`true`) | Whether the application serves TLS itself on `8443`, storing a certificate in its database. Set to `false` behind a reverse proxy that terminates TLS. |

Generate it with `openssl rand -hex 32`.

Create the `.env` beside `docker-compose.yml`. It is gitignored, which is what keeps the
signing key out of source control:

```
printf 'UFO_JWT_KEY=%s\nUFO_LIBRARY_PATH=/srv/code\nUFO_MACHINE_ID=%s\n' "$(openssl rand -hex 32)" "$(cat /etc/machine-id)" > .env
```

Point `UFO_LIBRARY_PATH` at whatever tree you want to index. The mount is read-only, but the
container runs as uid 1654 (`app`), so it can only walk directories that are readable by
others — a root-owned `lost+found` at the top of a mounted filesystem will raise
`Permission denied` during a snapshot walk while the rest of the tree indexes normally.

#### Reaching it from the LAN

Nothing extra is needed. Compose publishes `8080:8080` on all host interfaces, and the image
sets `Kestrel__Endpoints__App__Url=http://0.0.0.0:8080` — this override matters, because
Kestrel endpoint configuration takes precedence over `ASPNETCORE_URLS`, and the `localhost`
endpoint in `appsettings.json` would leave the port unreachable from outside the container.
The app answers at `http://<host-lan-ip>:8080`.

Find the host's LAN address:

```
ip -4 -o addr show scope global | awk '{print $2, $4}'
```

Confirm the published port is bound to all interfaces rather than loopback:

```
ss -lntp | grep 8080
```

If it responds on the host but not from another machine, the host firewall is the usual
cause — where `ufw` is active, open the port:

```
sudo ufw allow 8080/tcp
```

And the HTTPS port:

```
sudo ufw allow 8443/tcp
```

To deliberately narrow the exposure instead, bind the mapping to a single interface by
changing the compose port to `"192.168.1.10:8080:8080"`, or to `"127.0.0.1:8080:8080"` to
take it off the LAN entirely.

The container publishes both ports: `8080` plain HTTP and `8443` HTTPS. Prefer
`https://<host-lan-ip>:8443` — on `8080` credentials and JWTs cross the network unencrypted.
Whichever you use, do not port-forward it to the internet as-is.

#### TLS and certificates

The application serves HTTPS itself; no certificate is baked into the image or mounted in.
On first run it generates a self-signed certificate, stores it, and serves it from then on.
The certificate is a property of the **server**, not of an account: Kestrel presents one
certificate to everybody, so it lives in a server-scoped `ServerSettings` row rather than in
the per-user `UserSettings` table, and only an administrator can replace it.

**Who administers it.** The first account to register is the administrator — it belongs to
whoever stands the server up. Later accounts are plain users. On a database that already had
users before this feature existed, the longest-standing account is promoted once, when the
`Users.IsAdmin` column is added, so an existing installation is not left with nobody able to
reach these settings. There is no promotion UI; to grant it to someone else, set
`IsAdmin = 1` on their row in the `Users` table.

**Replacing the certificate.** An administrator opens **Settings → Security** and either
uploads a PKCS#12 archive (`.pfx`/`.p12`) containing the certificate and its private key, or
regenerates a self-signed one. A replacement is served from the next connection onwards; no
restart is needed. Uploads are rejected if they are expired, not yet valid, carry no private
key, or are marked for something other than server authentication.

**How the private key is stored.** The database is not encrypted, so the archive is sealed
with AES-GCM before it is written, under a key kept beside the database as
`/data/cert-protection-key`. The passphrase you type when uploading is used only to open the
archive and is never stored. Note that the key file and the database sit in the same volume,
so a backup of `/data` captures both — this protects against the database alone leaking, not
against loss of the whole data directory. If the key file goes missing the stored
certificate cannot be decrypted, and the server generates a fresh self-signed one and logs
that it did.

**Naming the address people browse to.** A container cannot discover the host's LAN
address — from inside, the only visible addresses are loopback, the container id and the
docker bridge. A certificate generated from just those would not cover
`https://192.168.x.y:8443`, and clients would report a *host-name mismatch*, which is a
harder failure than an untrusted issuer and is not fixed by trusting the certificate. So the
deployment names it:

| Variable | Purpose |
|---|---|
| `UFO_CERTIFICATE_HOST` | The address or host name people browse to, named in the generated certificate. Add further entries in `docker-compose.yml` as `Ufo__CertificateSubjectAlternativeNames__1`, `__2`, and so on. |

Adding or changing this after the certificate has been generated is enough: on the next
start the server notices that its self-signed certificate does not name what it has been
told to serve and reissues one that does. An uploaded certificate is never reissued this
way — it is used exactly as supplied, so make sure it already names the addresses you serve.

Outside a container the machine's own host name and addresses are detected automatically,
and this is only needed for names it cannot see for itself.

**Trusting a self-signed certificate.** Browsers warn about it until it is trusted on each
device — that warning is expected, not a fault. The generated certificate always names
`localhost`, the loopback addresses and the machine's host name, plus whatever
`UFO_CERTIFICATE_HOST` adds. For anything beyond a trusted LAN, upload a certificate from a
CA your clients already trust.

**Behind a reverse proxy.** Run with the supplied overlay when something in front
terminates TLS:

```
docker compose -f docker-compose.yml -f docker-compose.no-tls.yml up -d
```

It switches `Ufo__EnableHttps` off and removes the HTTPS endpoint. The endpoint has to be
*removed*, not blanked: setting `Kestrel__Endpoints__Https__Url` to an empty string leaves
the endpoint declared with no URL, and Kestrel refuses to start on that just as it refuses an
https endpoint with no certificate. A null value in the overlay is how Compose expresses
"absent". Getting this wrong is caught at startup with a message saying so rather than an
opaque Kestrel error.

With TLS off the application neither generates nor stores a certificate, the Settings page
reports that TLS is not configured here, and the certificate endpoints refuse writes rather
than storing something that can never be served.

**Known limitation — intermediate certificates.** Only the leaf certificate is presented.
An uploaded PKCS#12 archive is stored complete, intermediates included, so nothing is lost —
but clients that do not fetch intermediates themselves will see an incomplete chain. This
does not affect self-signed certificates, which have none. If you serve a certificate from a
CA that issues via an intermediate, put that intermediate in your clients' trust stores or
terminate TLS upstream until this is addressed.

#### Managing the container

Run these from the repository root, where `docker-compose.yml` lives:

```
cd /path/to/universal-file-observer
```

Status, including the health-check result:

```
docker compose ps
```

Follow the logs:

```
docker compose logs -f
```

Restart without rebuilding:

```
docker compose restart
```

Stop and remove the container, keeping the data:

```
docker compose down
```

Rebuild the image and restart after a code change:

```
docker compose up -d --build
```

Open a shell inside the running container:

```
docker compose exec ufo sh
```

All state lives in the `ufo-data` volume, so the container itself is disposable — `down`
followed by `up -d` preserves every user and snapshot. Note that `docker compose down -v`
also deletes that volume, taking the database, the rolling logs and the generated machine id
with it; use plain `down` unless you mean to wipe the deployment.

Back the volume up to `ufo-data.tar.gz` in the current directory:

```
docker run --rm -v universal-file-observer_ufo-data:/data -v "$PWD:/backup" alpine tar czf /backup/ufo-data.tar.gz -C /data .
```

Everything that differs between the two hosts is an explicit setting under the `Ufo`
configuration section (`Ufo__DataDirectory`, `Ufo__AllowedRoots__0`,
`Ufo__OpenBrowserOnStartup`, `Ufo__EnableHttpsRedirection`, `Ufo__EnableFileLogging`,
`Ufo__MachineId`) rather than a runtime platform check. See
`_docs/AI_DUAL_TARGET_PLAN.md`.

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
