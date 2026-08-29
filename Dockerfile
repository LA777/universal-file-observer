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

# /data holds the SQLite database, the rolling logs and the generated machine id;
# /library is where the folders to be indexed are mounted. Mount volumes over
# both - anything written to the image layer is lost when the container is
# recreated, and a churning machine id fragments snapshot history across runs.
RUN mkdir -p /data /library && chown -R $APP_UID:$APP_UID /data /library
VOLUME ["/data"]

WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish/ ./

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Kestrel endpoint configuration takes precedence over ASPNETCORE_URLS, so the
# endpoint declared in appsettings.json has to be overridden here. Binding to
# localhost would leave the port unreachable from outside the container.
ENV Kestrel__Endpoints__App__Url=http://0.0.0.0:8080

ENV Ufo__DataDirectory=/data
ENV ConnectionStrings__DefaultConnection="Data Source=/data/ufo.db;Foreign Keys=True"

# Restricts the file-browsing, search and video endpoints to what is mounted in.
# UfoHostOptions.ForContainer() already defaults to this, so the restriction holds
# even in an image that does not set it; stated here as the mount contract. Add
# further roots as Ufo__AllowedRoots__1, __2 and so on.
ENV Ufo__AllowedRoots__0=/library

# The machine identity recorded against each snapshot. A container's own
# /etc/machine-id is regenerated whenever the container is recreated, so set this
# to the identity of the physical host to keep snapshot history attributable.
# ENV Ufo__MachineId=

# JWT__Key and ApplicationSettings__HashSalt MUST be supplied at run time - the
# values baked into appsettings.json are shared by every copy of this image.

EXPOSE 8080
USER $APP_UID

# Probes /api/user/is-created using the runtime that is already in the image, so
# no curl or wget has to be installed.
HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 \
    CMD ["dotnet", "/app/Ufo.Server.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "/app/Ufo.Server.dll"]
