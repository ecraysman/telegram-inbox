# Tasks (Step #3)

The task backlog by agent. Dependencies and phases are sequenced in `plan.md` (Step #4).
Every task's acceptance criteria include the Definition of Done in `AGENTS.md` §8.

## A0 · Orchestrator
| ID | Task | Acceptance |
|---|---|---|
| O-1 | Write ADR-002 (real time), ADR-003 (webhook + queues), ADR-004 (async replies) in Spanish | One page each, follows the template, consistent with `architecture.md` |
| O-2 | Initialize git, root `.gitignore` (`.env`), first commit of the docs | `.env` ignored; `git status` clean |
| O-3 | Spawn A2/A3/A4 with logged prompts; integrate their outputs | Prompts in `prompt-log.md` before launch |
| O-4 | Integration pass: `docker compose up`, fix seams between agents | All containers healthy |
| O-5 | Traceability matrix: requirements → code → tests/QA evidence (Step #10) | Every §3.1, §3.2 and §5 item mapped |
| O-6 | `docs/ai/trabajo-con-ia.md` (Spanish, at most one page) | Tools, agents and roles, 2–3 prompts, how output was validated |
| O-7 | Bonus answers (cache, media) | One paragraph each, in the README or a separate doc |

## A2 · Backend
| ID | Task | Acceptance |
|---|---|---|
| B-1 | Solution skeleton: 5 projects + tests, `Directory.Build.props`, central packages, project references obeying the dependency rule | `dotnet build` passes with zero warnings |
| B-2 | Domain: `Contact`, `Message` (+ delivery state machine), `MessageText`, enums, `DomainException` | No framework references |
| B-3 | Domain unit tests: every invariant and transition (MarkAsRead idempotent, outbound never unread, Pending→Sent/Failed, Failed→Pending, invalid transitions, text limits, DisplayName fallbacks, UTC enforcement) | `dotnet test` green |
| B-4 | Application: ports + handlers (`ProcessIncomingMessage`, `GetContacts`, `GetConversation`, `MarkConversationAsRead`, `QueueReply`, `DeliverReply`, `RetryReply`, `RequeueStalePendingReplies`, `RegisterWebhook`) | Depends on Domain only |
| B-5 | Persistence: DbContext, Fluent config matching contracts §5, migration, repositories, `IInboxQueries` projections, `ON CONFLICT` insert | The migration creates exactly the contract schema and indexes |
| B-6 | Messaging: connection management, topology declaration, confirm-publisher, consumer base (manual ack, prefetch 1, permanent vs transient error classification) | Topology matches contracts §4 |
| B-7 | Telegram adapter: typed HttpClient (`sendMessage`, `setWebhook`, `getWebhookInfo`), error mapping (400/403 permanent; 429 with `retry_after`, 5xx and network transient) | The token never appears in logs (URL redaction) |
| B-8 | Ngrok adapter: `/api/tunnels` discovery of the https tunnel | Retries until the tunnel exists |
| B-9 | Api host: REST endpoints, ProblemDetails, SignalR hub + notifier, `inbox.events` relay, webhook endpoint on :8081 only, `WebhookRegistrationService` + watchdog, migrations at startup, health checks, fail-fast config | Matches contracts §1–§3 |
| B-10 | Worker host: incoming consumer, outgoing consumer, outbox sweeper, RabbitMQ notifier | Matches architecture §3 and §6 and contracts §7.4–§7.6 |

**A2 milestones** (reported one by one; M4 can be cut): M1 inbound end to end · M2 REST read + mark-read · M3 outbound happy path · M4 Failed/retry, sweeper, watchdog.

## A3 · Frontend
| ID | Task | Acceptance |
|---|---|---|
| F-1 | Angular 22 workspace (standalone, strict), global tokens and Red Hat Display font | `ng build` passes |
| F-2 | `ApiService` + models matching contracts §2 | Types identical to the DTOs |
| F-3 | `RealtimeService`: SignalR connection, automatic reconnect, connection-status signal, event streams | Refetch on reconnect |
| F-4 | `InboxStore` (signals): contacts sorted by last activity, upsert by id, selected conversation, auto-mark-read on open and on incoming messages for the open conversation | Duplicate events are harmless |
| F-5 | Contacts sidebar: avatar initials, name or @username, preview, time, unread badge, selected state, empty state | Matches ui-style.md |
| F-6 | Conversation panel: header, bubbles by direction, delivery icons, failed reason + "Reintentar", composer (Enter sends, Shift+Enter adds a new line, keeps the text and shows the error when the request fails, 4096 limit) | Matches ui-style.md |

## A4 · Platform
| ID | Task | Acceptance |
|---|---|---|
| P-1 | `backend/Dockerfile` (multi-stage; targets `api` and `worker`; curl in the api stage) + `backend/.dockerignore` | Dockerfile reviewed against plan.md §3; real build at O-4 |
| P-2 | `frontend/Dockerfile` + `frontend/nginx.conf` exactly per contracts §7.7 + `frontend/.dockerignore` | Matches §7.7; verified at O-4 (UI loads, hub on WebSocket) |
| P-3 | `docker-compose.yml`: 6 services, healthchecks (§7.9), `depends_on: service_healthy` (api does **not** depend on ngrok), named volumes, localhost-only host ports, ngrok per §7.8 + `infra/ngrok/ngrok.yml` | `docker compose config -q` passes |
| P-4 | Validate the existing `.env.example` against compose (every `${VAR}` documented) | No real values; no missing variables |
| P-5 | Verify and pin the exact postgres and rabbitmq image tags | Tags exist on Docker Hub |

## A5 · QA (Step #9)
| ID | Task | Acceptance |
|---|---|---|
| Q-1 | Run the build and unit tests; review domain test coverage and add any missing invariant tests | Report with real output |
| Q-2 | Simulated Telegram scenarios: valid text, duplicate `update_id`, non-text, wrong secret, broker down → 503. **Security boundary:** through the ngrok URL, `GET /api/contacts` and `/hubs/inbox` → 404 while `POST /telegram/webhook` works (needs the real `.env`) | Evidence per scenario |
| Q-3 | Outbound scenarios: success, invalid token or blocked user → Failed + retry, outbox sweeper recovers Pending | Evidence |
| Q-4 | `docs/qa/verification-report.md`: §3.1 and §3.2 marked PASS/FAIL/NOT-VERIFIED | Every row has evidence |
| Q-5 | Manual end-to-end checklist for the human with a real Telegram account | Step by step |

## A6 · Reviewer (end of Step #8, Step #12)
| ID | Task | Acceptance |
|---|---|---|
| R-1 | Adversarial code review (architecture, SOLID, correctness, security, coherence) | Ranked findings with file:line |
| R-2 | Evaluator simulation against the §7 rubric, with a score per criterion and gaps | Actionable list |

## A7 · Writer (Steps #9 and #11)
| ID | Task | Acceptance |
|---|---|---|
| W-1 | README.md (the 5 required sections + links) | Every command exists in the repo |
| W-2 | C4 L1 + L2 in draw.io XML + SVG, derived from the compose file and the code | Containers and protocols match `docker-compose.yml` |
| W-3 | Handout document (Step #11) | Content defined at Step #11 |
