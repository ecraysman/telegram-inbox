# AGENTS.md: repository guide and guardrails

Read this before touching anything. It applies to every human and AI agent working in this repository.

## 1. Sources of truth (highest first)
1. `requirements.md`: the challenge. It is never edited.
2. `docs/adr/*`: accepted decisions.
3. `docs/plan/contracts.md`: frozen interfaces (HTTP, SignalR, RabbitMQ, DB, env).
4. `docs/plan/plan.md` and `docs/plan/tasks.md`: what to build and in which order.
5. `docs/plan/architecture.md`, `docs/plan/ui-style.md`, `docs/plan/decisions-log.md`.

If two sources conflict, the higher one wins **and you report the conflict**. Never silently "fix" the lower one.

## 2. Repository layout
```
backend/
  Inbox.sln  Directory.Build.props  Directory.Packages.props  Dockerfile
  src/Inbox.Domain/  src/Inbox.Application/  src/Inbox.Infrastructure/  src/Inbox.Api/  src/Inbox.Worker/
  tests/Inbox.Domain.Tests/
frontend/            Angular workspace + Dockerfile + nginx.conf
infra/               ngrok config, rabbitmq definitions (if any)
docs/adr/  docs/c4/  docs/plan/  docs/ai/  docs/qa/
docker-compose.yml  .env.example  README.md  AGENTS.md
```

## 3. Pinned versions
| Item | Version |
|---|---|
| .NET SDK / runtime images | `mcr.microsoft.com/dotnet/sdk:10.0`, `aspnet:10.0` / `runtime:10.0` (LTS) |
| EF Core + Npgsql provider | 10.0.x |
| RabbitMQ.Client | 7.2.x (async API only) |
| Angular / CLI | 22.2.x |
| Node (build image) | `node:24-alpine` |
| @microsoft/signalr | 10.0.x |
| PostgreSQL / RabbitMQ images | `postgres:18-alpine` / `rabbitmq:4-management-alpine`. A4 pins the exact minor after verifying it exists |

Local host SDKs are **not** assumed. Build and test with containers:
```bash
docker run --rm -v "$PWD/backend":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
docker run --rm -v "$PWD/frontend":/app -w /app node:24-alpine sh -c "npm ci && npx ng build"
docker compose config -q && docker compose build
```

## 4. Allowed dependencies
Any addition outside this list needs a "Decision" entry in your report with its justification.
- **Backend:** `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design` (migrations only), `RabbitMQ.Client`, `Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`, `xunit.v3` + `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio`.
  - **Not allowed:** MediatR, MassTransit, FluentAssertions v8+, AutoMapper (all commercially licensed or unnecessary), Telegram.Bot (we use a small typed HttpClient adapter we control).
- **Frontend:** `@microsoft/signalr`. No UI component framework and no state library; signals are enough.

## 5. Backend rules
- **Dependency rule (enforced by project references):** Domain → nothing; Application → Domain; Infrastructure → Application and Domain; Api/Worker → Application and Infrastructure. Domain and Application must not reference EF Core, RabbitMQ, ASP.NET or `HttpClient`.
- **The domain holds the rules.** State changes go through entity methods (`MarkAsRead`, `MarkSent`, `MarkFailed`, `RequeueForRetry`). Setters are private. Invalid transitions throw `DomainException`. Value objects validate on construction.
- **Use cases:** one sealed handler class per use case in Application, which depends on ports only. Endpoints and consumers are thin: map input, call the handler, map output.
- **Time:** inject `TimeProvider`; never call `DateTime.Now` or `DateTime.UtcNow`. Use `DateTimeOffset` in UTC everywhere (ADR-001).
- **Async:** every I/O method is async with a `CancellationToken` and the `Async` suffix. No `.Result` or `.Wait()`.
- **Style:** file-scoped namespaces, `Nullable` enabled, `TreatWarningsAsErrors`, `sealed` by default, records for DTOs and messages, primary constructors where they make the code shorter. Central package management lives in `Directory.Packages.props`.
- **Logging:** structured (`ILogger` message templates). **Never log tokens, the webhook secret, connection strings or message text at Information level or above.** Message text may appear at Debug.
- **Errors:** RFC 9457 ProblemDetails with a `code` field (see contracts.md).
- **Tests:** xUnit, **Domain layer only** (requirements §6). Name tests `Method_Scenario_Expected`. Cover every invariant and state transition.

## 6. Frontend rules
- Standalone components, signals, `@if`/`@for`, `ChangeDetectionStrategy.OnPush`, strict TypeScript, `inject()`.
- Structure: `core/` (api.service, realtime.service, models), `state/` (inbox.store with signals), `features/contacts`, `features/conversation`, `shared/`. Components never call `HttpClient` or SignalR directly.
- Styles: tokens from `docs/plan/ui-style.md` as CSS custom properties in `styles.css`, plus component-scoped CSS. No inline colors.
- The UI text is in Spanish. Desktop only, no animations.

## 7. Guardrails (hard)
1. **Secrets:** never create, read, print or commit `.env`, and never put real tokens anywhere. `.gitignore` excludes `.env`. Only `.env.example` holds placeholders.
2. **Ownership:** edit only the paths your role owns (`docs/ai/agents.md`). Report anything else you need changed.
3. **Contracts are frozen** during implementation. A change goes through the orchestrator, who updates `contracts.md` and notifies all agents.
4. **Scope:** no auth, media, responsive layout or pagination (§3.3). No speculative features. Anything beyond scope needs an entry in the decisions log.
5. **Evidence over claims:** report what you ran and its real output. "Should work" is not a verification.
6. **No dead code:** no commented-out code, and no `TODO` without a tracked issue in your report.
7. **Destructive operations:** never run `docker compose down -v`, `git reset --hard`, `rm -rf` outside your own build output, or force-push.

## 8. Definition of Done (per task)
- [ ] Builds with zero warnings; tests pass; the frontend production build succeeds.
- [ ] Matches `contracts.md` exactly (names, status codes, payloads).
- [ ] Respects the dependency rule and the guardrails above.
- [ ] The report lists files, decisions, deviations, verification commands with output, and open issues.

## 9. Git
- Only the orchestrator commits. Use Conventional Commits (`feat(backend): …`, `docs(adr): …`), one logical change per commit.
- Commit messages are in English and end with the attribution trailer configured for the session.
