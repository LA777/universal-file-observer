# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Stage 1 - Angular front end.
# Built here rather than by the .NET SDK's PublishRunWebpack target so the SDK
# image does not need Node installed.
# ---------------------------------------------------------------------------
# Angular 22 CLI requires Node ^22.22.3, ^24.15.0 or >=26; pinned so an older
# cached 22.x layer cannot silently break the build.
FROM node:22.23-alpine AS spa
WORKDIR /src/ufo.client

COPY ufo.client/package.json ufo.client/package-lock.json* ./
RUN npm ci

COPY ufo.client/ ./

# src/environments/environment.ts is gitignored; fall back to the example.
RUN if [ ! -f src/environments/environment.ts ] && [ -f src/environments/environment.example.ts ]; then \
        cp src/environments/environment.example.ts src/environments/environment.ts; \
    fi

RUN npx ng build --configuration production

# ---------------------------------------------------------------------------
# Stage 2 - .NET publish.
# Ufo.Platform.Windows and Ufo.Desktop are deliberately absent: the container
# never loads the WMI provider or WinForms.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Ufo.Abstractions/Ufo.Abstractions.csproj Ufo.Abstractions/
COPY Ufo.Database/Ufo.Database.csproj Ufo.Database/
COPY Ufo.Server/Ufo.Server.csproj Ufo.Server/
RUN dotnet restore Ufo.Server/Ufo.Server.csproj

COPY Ufo.Abstractions/ Ufo.Abstractions/
COPY Ufo.Database/ Ufo.Database/
COPY Ufo.Server/ Ufo.Server/

RUN dotnet publish Ufo.Server/Ufo.Server.csproj -c Release -o /app/publish -p:BuildSpa=false

COPY --from=spa /src/ufo.client/dist/ufo.client/browser/ /app/publish/wwwroot/

# ---------------------------------------------------------------------------
# Stage 3 - runtime.
# Debian-based, not Alpine: the bundled SQLitePCLRaw e_sqlite3 native library is
# linked against glibc and will not load on musl.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# /data holds the SQLite database, the rolling logs, the generated machine id and
# the key the stored TLS certificate is encrypted with (losing it costs the
# certificate, which is then regenerated self-signed);
# /library is where the folders to be indexed are mounted;
# /workspace is where the file operations (create, rename, copy, move, delete)
# can actually write - /library is expected to be mounted read-only. Mount
# volumes over all three: anything written to the image layer is lost when the
# container is recreated, and a churning machine id fragments snapshot history.
#
# All three are created here rather than left to Docker precisely so they end up
# owned by the application. Docker seeds a named volume from the image directory
# it covers, so a /workspace that does not exist in the image becomes a
# root-owned volume that the app - running as $APP_UID - cannot write a byte to.
RUN mkdir -p /data /library /workspace && chown -R $APP_UID:$APP_UID /data /library /workspace
VOLUME ["/data"]

WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish/ ./

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Kestrel endpoint configuration takes precedence over ASPNETCORE_URLS, so the
# endpoint declared in appsettings.json has to be overridden here. Binding to
# localhost would leave the port unreachable from outside the container.
#
# One endpoint, and it is HTTPS. There is deliberately no plaintext listener
# alongside it: an open HTTP port carries credentials and JWTs in the clear, and
# anything that can reach the container can use it, so serving both would make
# the TLS endpoint optional in practice.
#
# The certificate is never baked in or mounted: the application stores one in its
# database, generating a self-signed certificate on first run and serving whatever
# an administrator later uploads on the Settings page.
#
# A deployment terminating TLS upstream overrides this endpoint back to http and
# turns Ufo__EnableHttps off - Kestrel refuses to start an https:// endpoint it
# has no certificate for. docker-compose.no-tls.yml does exactly that.
ENV Kestrel__Endpoints__App__Url=https://0.0.0.0:8443

ENV Ufo__DataDirectory=/data
ENV ConnectionStrings__DefaultConnection="Data Source=/data/ufo.db;Foreign Keys=True"

# Restricts the file-browsing, search, video and file-operation endpoints to what
# is mounted in. UfoHostOptions.ForContainer() already defaults to /library, so
# the restriction holds even in an image that does not set it; stated here as the
# mount contract. Add further roots as Ufo__AllowedRoots__2, __3 and so on.
#
# /library is the tree to index and is expected to be mounted read-only, so the
# write operations are refused there by the file system rather than by UFO.
# /workspace is the writable root those operations are for.
ENV Ufo__AllowedRoots__0=/library
ENV Ufo__AllowedRoots__1=/workspace

# The machine identity recorded against each snapshot. A container's own
# /etc/machine-id is regenerated whenever the container is recreated, so set this
# to the identity of the physical host to keep snapshot history attributable.
# ENV Ufo__MachineId=

# JWT__Key MUST be supplied at run time. No key is baked into appsettings.json,
# and the host refuses to start without one rather than falling back to a shared
# default. Generate one with: openssl rand -hex 32

EXPOSE 8443
USER $APP_UID

# Probes /api/user/is-created using the runtime that is already in the image, so
# no curl or wget has to be installed.
HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 \
    CMD ["dotnet", "/app/Ufo.Server.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "/app/Ufo.Server.dll"]
