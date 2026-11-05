# Decisions log

Every decision, who made it, and where it comes from. This is the source for the README's "Supuestos" section and for the ADRs.
Status: ✅ accepted (by the human directly, or via the step checkpoint that contained it) · 🟡 proposed (pending checkpoint) · 🔁 changed later (see note)
References marked *(pending: task, step)* point to documents not written yet. Step #10 checks that no reference is left dangling.

| ID | Decision | Reason | Rejected alternative | Origin | Status |
|---|---|---|---|---|---|
| D-001 | Docs in Spanish, code and identifiers in English | The evaluator writes in Spanish; code follows industry norm | All English / all Spanish | Human (P-001) | ✅ |
| D-002 | Human checkpoint after every step | The human wants control | Autonomous run | Human (P-001) | ✅ |
| D-003 | Human provides the real bot token and ngrok authtoken; a live end-to-end test is run | Proves "Funcionamiento" | Mocks only | Human (P-001) | ✅ |
| D-004 | SQL database, PostgreSQL + EF Core/Npgsql | ADR-001 | MariaDB/MySQL, MongoDB | Human (P-003) | ✅ |
| D-005 | Pin .NET 10 (10.0.x LTS) and Angular 22.2.x | Latest stable on 2026-11-05 (.NET 11 is RC only); LTS is extra stability | .NET 11 RC | Orchestrator (P-004) | ✅ |
| D-006 | Build everything inside Docker (SDK and Node images) | Host has .NET 8 and Node 22.3, below the Angular 22 minimum; the evaluator only needs Docker | Requiring a local SDK | Orchestrator | ✅ |
| D-007 | Five projects: Domain / Application / Infrastructure / Api / Worker, plus Domain.Tests | Real separation that maps to C4 L2 | Three Infrastructure projects | A1 | ✅ |
| D-008 | Plain use-case handlers, no MediatR | Explicit, no commercial license | MediatR | A1 | ✅ |
| D-009 | RabbitMQ.Client 7 directly, no MassTransit | Topology visible in code and ADR-003 | MassTransit (v9 commercial, hides topology) | A1 | ✅ |
| D-010 | **Separate worker container** for message processing | Intake depends only on the broker, processing only on the DB; real decoupling | Consumer hosted inside the api | A1 → Human (P-006) | ✅ |
| D-011 | Worker → browsers via the `inbox.events` fanout, relayed by the api to SignalR | Reuses the broker; works as a backplane | Redis backplane; internal HTTP callback | A1 | ✅ |
| D-012 | Webhook returns 200 after publisher confirm, 503 on broker failure, 401 on bad secret, 200 for ignored updates | Telegram's retry gives end-to-end durability without poison retries | Always 200 | A1 | ✅ |
| D-013 | Internal `IncomingTextMessage v1` contract (anti-corruption layer) | Worker independent of Telegram's schema | Raw update JSON in the queue | A1 | ✅ |
| D-014 | Quorum queue with delivery limit 5 + DLX/DLQ + in-process retry | Poison protection with minimal code | TTL retry queues | A1 | ✅ |
| D-015 | Ack after commit, prefetch 1, re-notify on duplicate | Closes the commit/ack gap | Skip notify on duplicate | A1 | ✅ |
| D-016 | Order history by `occurred_at`, then DB `seq` | Pending replies have no Telegram id yet; a reply stays where it was written | `telegram_message_id` (A1's original) | A1 → Orchestrator (P-006) | 🔁 changed by D-021 |
| D-017 | SignalR for real time | First-party on both ends, reconnect, fallback transports | SSE (runner-up), WebSockets, polling | A1 | ✅ → ADR-002 |
| D-018 | Webhook auto-registration in an api BackgroundService with a 60 s watchdog | Testable, self-heals when the URL changes | One-shot curl init container | A1 | ✅ |
| D-019 | `TELEGRAM_WEBHOOK_SECRET` from `.env` | Deterministic; QA can simulate updates | Random per boot | A1 | ✅ |
| D-020 | ngrok exposes only api:8081 (webhook) | The API has no auth by scope, so it must not be public | Tunnel the whole api or nginx | A1 | ✅ |
| D-021 | **Queued outbound reply**: api persists Pending + publishes; worker sends; state machine Pending → Sent/Failed | Survives Telegram outages; delivery state is visible and auditable | Synchronous send (A1's recommendation) | Human (P-006) | ✅ → ADR-004 |
| D-022 | Unread count and last preview computed at query time | Consistent with ADR-001, no counter drift | Denormalized counters | A1 | ✅ |
| D-023 | Outbound messages created with `ReadAt = occurred_at` | Single unread predicate `read_at IS NULL` | Direction filter in every query | A1 | ✅ |
| D-024 | Mark-read with `upToMessageId` via domain `MarkAsRead` | Doesn't mark unseen messages read; logic stays in the domain | Mark all by server time | A1 | ✅ |
| D-025 | Only private-chat text messages; edited, group, channel and media updates are ignored | Scope §3.3 | Partial support | A1 | ✅ Supuesto |
| D-026 | Contact identity = `chat_id`; name and username refreshed on each inbound | Profiles change | Keep the first-seen name | A1 | ✅ Supuesto |
| D-027 | Single operator, global read state, single api replica | No auth or users by scope | Per-operator read state | A1 | ✅ Supuesto |
| D-028 | UI served on localhost:4200, not through ngrok | Avoids the ngrok interstitial and keeps the API private | UI over ngrok | A1 | ✅ |
| D-029 | The outbound message row is the outbox; a worker sweeper re-publishes Pending messages older than 60 s; API returns 202 even if the publish confirm fails | Closes the commit-then-publish gap with no extra table; re-publishing is idempotent | Separate outbox table + dispatcher; failing the request | Orchestrator (P-006) | ✅ |
| D-030 | At-least-once delivery to Telegram (rare duplicate on a crash between send and commit) | Telegram has no idempotency key; a duplicate is better than a silent loss in support | `Sending` state with at-most-once semantics | Orchestrator → Human (P-007) | ✅ → ADR-004 risk |
| D-031 | Retries exhausted → mark `Failed` and ack (visible in the UI); DLQ only for poison payloads | The operator must see failures; nothing is silently parked in the DLQ | Leave failed sends in the DLQ | Orchestrator (P-006) | ✅ |
| D-032 | `POST /api/messages/{id}/retry` for Failed replies + "Reintentar" in the UI | A Failed state without a recovery action is a dead end | No retry (the operator re-types) | Orchestrator → Human (P-007) | ✅ small scope addition |
| D-033 | `inbox.dlx` is a direct exchange with per-queue dead-letter routing keys and one DLQ per flow | Inbound and outbound poison messages stay separate | Single fanout DLQ | Orchestrator (P-006) | ✅ |
| D-034 | Extra **ADR-004: Envío asíncrono de respuestas** | A significant, human-made decision with trade-offs; "Podés sumar otros ADRs" | Fold it into ADR-003 | Orchestrator (P-006) | ✅ |
| D-035 | **Accepted risk:** outbound sends are serial (prefetch 1, one consumer), so during a Telegram outage every queued reply waits for the retries of the message ahead of it: ~7 s on 5xx/network errors, **up to ~30 s on 429** with `retry_after` capped at 10 s (cap added in Step #7, accepted by human P-012: "POC, 10 s is reasonable") | Keeps per-conversation order with minimal code; harmless at the expected volume | Per-contact queues / consistent-hash exchange; parallel consumers with per-contact locking | Human (P-007) | ✅ **Revisit trigger:** volume reaches hundreds of active conversations **AND** Telegram is degraded or down. Mitigation then: partition the outbound queue per contact (consistent-hash exchange) or delayed-retry queues so the consumer never blocks. → ADR-004 |
| D-036 | `requirements.md` is **not** committed (git-ignored); the README refers to the challenge instead of including it | The challenge text belongs to encodelabs; publishing it in a public repo isn't ours to decide | Commit it for self-containment | Human (P-008) | ✅ |
| D-037 | Seam values fixed in plan.md §3 (project paths, Dockerfile targets, Angular output path, relative API URLs, Kestrel ports) | Lets A2/A3/A4 run in parallel without waiting on each other | Discover the seams during integration | Orchestrator (P-008) | ✅ |
| D-038 | Api healthcheck by `curl` installed in the runtime image; the worker has no healthcheck (nothing depends on it) | aspnet images ship without curl; a worker HTTP endpoint only for a healthcheck adds surface | A healthcheck CLI mode in the app; an HTTP endpoint in the worker | Orchestrator (P-008) | ✅ |
| D-039 | Agents share one working tree with disjoint paths (no git worktrees); only the orchestrator commits, after each gate | Simple and auditable; ownership is enforced by the G1 diff check | Worktree per agent and merging | Orchestrator (P-008) | ✅ |
| D-040 | A0 seeds `.env.example` from contracts §6 before Wave 1; A4 owns it from then on (validates it against compose) | The human needs it now to start creating credentials | Wait for A4 | Orchestrator (P-009) | ✅ |
| D-041 | **The api does not `depends_on` ngrok.** Webhook registration retries in the background, so the stack is fully usable without ngrok or valid tokens (local dev, QA, evaluator without a tunnel) | Build and most validation don't depend on external services; a failing tunnel must not take the inbox down | api depends_on ngrok healthy | Orchestrator (P-009) | ✅ |
| D-042 | The webhook secret is generated locally (`openssl rand -hex 32`) and handed to Telegram **by the api itself** in `setWebhook(secret_token=…)`; Telegram then sends it in `X-Telegram-Bot-Api-Secret-Token` on every call. No BotFather step is needed | Fully automatic and matches "registrado automáticamente"; rotating it is just changing `.env` and restarting | Manual `curl setWebhook` documented in the README | Orchestrator (P-010) | ✅ |
| D-043 | Contracts revision 2: new §7 "Wire, persistence and runtime conventions" (shared JSON options, lowercase enum converters, `seq` identity, inbound recipe, publishing rules, Telegram error table, nginx, ngrok, health, client real-time rules) | A1 critique (P-011) found that a dozen implementation details would make the agents diverge | Leave them to each agent | Orchestrator (A1 BL-4, MA-1..5, MA-8, minors) | ✅ |
| D-044 | Port isolation by `Connection.LocalPort` in both directions; never `RequireHost` | The Host header behind ngrok is the public domain, so `RequireHost` would 404 every Telegram update | `RequireHost("*:8081")` | A1 BL-1 (verified by orchestrator) | ✅ |
| D-045 | A4 owns `frontend/nginx.conf` and both `.dockerignore` files; the orchestrator owns `.gitignore` | Removes the A3/A4 ownership collision | A3 owns nginx.conf | A1 BL-2 | ✅ |
| D-046 | Agent prompts point to task IDs, read tasks/architecture/ui-style, and no longer say "return 200 quickly" or "after ngrok has a tunnel" | Prompts contradicted contracts and D-041 | — | A1 BL-3 | ✅ |
| D-047 | Gates: G1 uses `git status --porcelain --untracked-files=all`; A4 Wave-1 acceptance is `docker compose config -q`; containers run with `--user`; build outputs git-ignored | `git diff` ignores untracked files; A4 can't build before A2/A3 finish; avoids root-owned outputs | — | A1 MA-6 | ✅ |
| D-048 | A2 delivers in milestones M1 inbound E2E → M2 read side → M3 outbound → M4 failure paths (cuttable) | The backend is the critical path; always keep something demonstrable | One big delivery | A1 MA-7 | ✅ |
| D-049 | `MessageDto.seq` exposed; `last_queued_at` column as the sweeper's staleness key | Client tie-breaker; avoids re-publishing on every sweep | Order by arrival; key on `occurred_at` | A1 minors 1, 9 | ✅ |
| D-050 | Work messages published with `mandatory: true`; a basic.return counts as failure | Confirms ack unroutable messages, so the webhook would return 200 and lose the message | No mandatory flag | A1 minor 10 (verified) | ✅ |
| D-051 | `retry_after` capped at 10 s; any 4xx other than 429 is permanent (incl. 401/404 bad token) | Bounds the serial-consumer stall and classifies every response | Unbounded `retry_after` | A1 MA-4 | ✅ Human (P-012) |
| D-052 | Supuesto: changing the bot token on an existing DB volume requires `docker compose down -v`, because `update_id` is per bot | Uniqueness of `telegram_update_id` is per bot | Composite key with a bot id | A1 minor 8 | ✅ Supuesto |
| D-053 | Supuesto: an inbound message delayed by Telegram retries keeps its original `date` and sorts into the past | Telegram's date is the truth of when the user wrote | Order by arrival time | A1 minor 7 | ✅ Supuesto |
| D-054 | Supuesto: "última versión estable" = .NET 10 and Angular 22 as of 2026-11-05; .NET 11 / Angular 23 may reach GA within days | The requirement is time-relative | — | A1 minor 16 | ✅ Supuesto |
| D-055 | Not adopted: a `version` field on DTOs | The status-precedence client rule plus pushing `messageQueued` before publishing covers the race at this size | Versioned DTOs | A1 MA-9 | ✅ |
