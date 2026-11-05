# syntax=docker/dockerfile:1
# One multi-stage file, two runtime targets (plan.md §3):
#   docker build --target api    ./backend
#   docker build --target worker ./backend
# BuildKit only builds the stages a target needs.

# ── restore (cached until a project file changes) ─────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Inbox.Domain/Inbox.Domain.csproj                 src/Inbox.Domain/
COPY src/Inbox.Application/Inbox.Application.csproj       src/Inbox.Application/
COPY src/Inbox.Infrastructure/Inbox.Infrastructure.csproj src/Inbox.Infrastructure/
COPY src/Inbox.Api/Inbox.Api.csproj                       src/Inbox.Api/
COPY src/Inbox.Worker/Inbox.Worker.csproj                 src/Inbox.Worker/
RUN dotnet restore src/Inbox.Api/Inbox.Api.csproj \
 && dotnet restore src/Inbox.Worker/Inbox.Worker.csproj
COPY src/ src/

# ── publish ───────────────────────────────────────────────────────
FROM restore AS publish-api
RUN dotnet publish src/Inbox.Api/Inbox.Api.csproj -c Release --no-restore -o /out

FROM restore AS publish-worker
RUN dotnet publish src/Inbox.Worker/Inbox.Worker.csproj -c Release --no-restore -o /out

# ── api runtime ───────────────────────────────────────────────────
# curl is installed only for the compose healthcheck (D-038).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish-api /out .
USER $APP_UID
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "Inbox.Api.dll"]

# ── worker runtime ────────────────────────────────────────────────
# No HTTP endpoint and no healthcheck by design (D-038).
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS worker
WORKDIR /app
COPY --from=publish-worker /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "Inbox.Worker.dll"]
