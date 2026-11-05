# Contracts (Step #3): FROZEN during Step #8

> These are the interfaces shared between agents A2 (backend), A3 (frontend) and A4 (platform).
> **Revision 2 (Step #7):** amended after the A1 critique (P-011). §7 "Wire, persistence and runtime conventions" is as binding as the rest; if §1–§6 and §7 seem to disagree, §7 wins.
> **Only the orchestrator may change this file.** An agent that needs a change stops and reports it.
> JSON is camelCase. Enums are serialized as camelCase strings. Every timestamp is ISO-8601 UTC (`2026-11-05T14:03:22.123Z`). IDs are UUID strings.

## 1. Network and ports

| Service | Container port | Host port | Notes |
|---|---|---|---|
| frontend (nginx) | 80 | `${FRONTEND_PORT:-4200}` | Serves the SPA. Proxies `/api/*` and `/hubs/*` (WebSocket upgrade, no buffering) to `api:8080` |
| api | 8080 | — | REST + SignalR + `/health/*` |
| api | 8081 | — | **Only** `POST /telegram/webhook`; the ngrok target |
| ngrok | 4040 | `127.0.0.1:4040` | Agent API (`/api/tunnels`) + inspector. Bound to localhost only: the inspector shows the secret header |
| rabbitmq | 5672 / 15672 | — / `127.0.0.1:15672` | Management UI on the host for the demo, localhost only |
| postgres | 5432 | — | Not exposed |

## 2. HTTP API (api:8080, reached via nginx at `/api`)

### DTOs
```ts
type Direction = 'inbound' | 'outbound';
type MessageStatus = 'received' | 'pending' | 'sent' | 'failed'; // inbound is always 'received'

interface MessageDto {
  id: string; seq: number;      // seq: DB identity, tie-breaker for ordering (occurredAt, seq)
  contactId: string; direction: Direction; text: string;
  occurredAt: string;            // inbound: Telegram date; outbound: when the operator queued it
  readAt: string | null;         // outbound: = occurredAt
  status: MessageStatus;
  failureReason: string | null;  // only when status = 'failed'
}
interface ContactSummaryDto {
  id: string; displayName: string; username: string | null;  // username without '@'
  unreadCount: number;
  lastMessage: MessageDto | null;
}
```
`displayName` is `firstName + ' ' + lastName` trimmed. If that is empty it falls back to `@username`, and then to the chat id.

### Endpoints
| Method and path | Body | Success | Errors |
|---|---|---|---|
| `GET /api/contacts` | — | 200 `ContactSummaryDto[]`, ordered by `lastMessage.occurredAt` desc | — |
| `GET /api/contacts/{contactId}/messages` | — | 200 `MessageDto[]`, ordered by `(occurredAt, seq)` asc, full history | 404 |
| `POST /api/contacts/{contactId}/read` | `{ "upToMessageId": "uuid" }` | 204. Marks unread inbound messages at or before that message (by `(occurredAt, seq)`) as read. Idempotent; `conversationRead` is emitted **only if rows changed** | 400 `validation_failed` (missing or invalid body, or the message belongs to another contact), 404 `contact_not_found`, 404 `message_not_found` |
| `POST /api/contacts/{contactId}/messages` | `{ "text": "..." }` | **202** `MessageDto` (`status: 'pending'`) | 400 (blank or over 4096 chars), 404 |
| `POST /api/messages/{messageId}/retry` | — | **202** `MessageDto` (`status: 'pending'`) | 404, 409 (status is not `failed`) |
| `GET /health/live` | — | 200 | — |
| `GET /health/ready` | — | 200 when the DB and the broker are reachable | 503 |

Errors use **RFC 9457 `application/problem+json`** with an extra `code` field: `validation_failed` (plus `errors: {field: [msg]}`), `contact_not_found`, `message_not_found`, `message_not_failed`.

### Telegram webhook (api:8081 only)
`POST /telegram/webhook`, body = Telegram `Update`. Required header: `X-Telegram-Bot-Api-Secret-Token`.

| Case | Status |
|---|---|
| Secret missing or wrong | 401 |
| Authenticated but unparseable, or not a private-chat text `message` | 200 (logged, ignored) |
| Published and confirmed by the broker | 200 |
| Broker unavailable, nack or timeout (5 s) | 503 |

## 3. Real time: SignalR hub `/hubs/inbox` (JSON protocol)

The hub is server to client only; the client invokes no hub methods. Client method names and payloads:

| Method | Payload | Emitted by | When |
|---|---|---|---|
| `messageReceived` | `{ message: MessageDto, contact: ContactSummaryDto }` | worker (via `inbox.events`) | New inbound message persisted (also re-emitted on duplicate redelivery) |
| `messageQueued` | `{ message: MessageDto, contact: ContactSummaryDto }` | api | Operator queued a reply |
| `messageStatusChanged` | `{ message: MessageDto, contact: ContactSummaryDto }` | worker (Sent/Failed), api (retry → Pending) | Delivery state changed |
| `conversationRead` | `{ contact: ContactSummaryDto }` | api | Read marking applied |

**Client rules:** upsert messages by `message.id` and contacts by `contact.id`, so duplicates are harmless. Re-sort the contact list on every event. On `onreconnected`, refetch `GET /api/contacts` and the open conversation. If a `messageReceived` arrives for the conversation currently open, call `POST /read` with that message id.

## 4. RabbitMQ

### Topology (declared idempotently at startup by the api and the worker)
| Exchange | Type | Bindings |
|---|---|---|
| `inbox.incoming` | direct, durable | `telegram.text` → `inbox.incoming.telegram` |
| `inbox.outgoing` | direct, durable | `telegram.send` → `inbox.outgoing.telegram` |
| `inbox.dlx` | direct, durable | `incoming` → `inbox.incoming.telegram.dlq`; `outgoing` → `inbox.outgoing.telegram.dlq` |
| `inbox.events` | fanout, durable | each api instance binds its own exclusive, auto-delete, server-named queue |

Work queues are `x-queue-type: quorum` with `x-delivery-limit: 5`, `x-dead-letter-exchange: inbox.dlx` and `x-dead-letter-routing-key: incoming|outgoing`. DLQs are quorum and durable.
Consumers use manual ack and prefetch 1. Publishers use publisher confirms with a 5 s timeout and `delivery_mode = persistent`; events are transient.

### Messages
All messages have `content_type: application/json`, and the AMQP `type` property is set to the name below.

```jsonc
// type = "IncomingTextMessage.v1", message_id = "tg-update-{updateId}"
{ "updateId": 123456789, "chatId": 987654321, "telegramMessageId": 42,
  "from": { "firstName": "Ana", "lastName": "Pérez", "username": "anap" },   // lastName/username nullable
  "text": "Hola", "sentAt": "2026-11-05T14:03:22Z" }

// type = "OutgoingTextMessage.v1", message_id = "{messageId}". The DB row is the source of truth.
{ "messageId": "0b8f…uuid" }

// type = "InboxEvent.v1" on inbox.events. The api relay calls Clients.All.SendAsync(eventType, payload)
{ "eventType": "messageReceived" | "messageStatusChanged", "payload": { /* exactly the §3 payload */ } }
```

## 5. Database (PostgreSQL, snake_case, EF Core migrations in Infrastructure)

```
contacts( id uuid PK, telegram_chat_id bigint UNIQUE NOT NULL, first_name text NOT NULL,
          last_name text NULL, username text NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL )

messages( id uuid PK, seq bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
          contact_id uuid NOT NULL FK→contacts ON DELETE CASCADE,
          direction text NOT NULL CHECK (direction IN ('inbound','outbound')),
          text text NOT NULL, occurred_at timestamptz NOT NULL, read_at timestamptz NULL,
          status text NOT NULL CHECK (status IN ('received','pending','sent','failed')),
          failure_reason text NULL,
          telegram_update_id bigint NULL UNIQUE,       -- inbound idempotency key (ADR-001)
          telegram_message_id bigint NULL,             -- inbound: from update; outbound: from sendMessage
          delivered_at timestamptz NULL,               -- outbound: Telegram's date on success
          last_queued_at timestamptz NULL )            -- outbound: set by Outbound() and RequeueForRetry(); the sweeper's staleness key

ix_messages_unread        ON messages(contact_id) WHERE read_at IS NULL
ix_messages_conversation  ON messages(contact_id, occurred_at DESC, seq DESC)
ix_messages_pending       ON messages(last_queued_at) WHERE status = 'pending'
```

## 6. Environment variables

### `.env` (filled in by the user; `.env.example` holds placeholders)
| Variable | Required | Example / default | Notes |
|---|---|---|---|
| `TELEGRAM_BOT_TOKEN` | yes | `123456:ABC-...` | From BotFather. Never logged |
| `TELEGRAM_WEBHOOK_SECRET` | yes | any string matching `[A-Za-z0-9_-]{16,256}` | Sent by Telegram in the secret header |
| `NGROK_AUTHTOKEN` | yes | — | From the ngrok dashboard |
| `NGROK_DOMAIN` | no | `my-name.ngrok-free.app` | Static free domain. If unset, ngrok assigns a random URL |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | yes | `inbox` / `change-me` / `inbox` | Local only |
| `RABBITMQ_USER` / `RABBITMQ_PASSWORD` | yes | `inbox` / `change-me` | Local only |
| `FRONTEND_PORT` | no | `4200` | |

### .NET configuration keys (set by docker-compose from the variables above)
| Key (env form) | Consumer |
|---|---|
| `ConnectionStrings__Inbox` = `Host=postgres;Port=5432;Database=…;Username=…;Password=…` | api, worker |
| `RabbitMq__Host` = `rabbitmq`, `RabbitMq__Port` = `5672`, `RabbitMq__User`, `RabbitMq__Password` | api, worker |
| `Telegram__BotToken` | api (setWebhook), worker (sendMessage) |
| `Telegram__WebhookSecret` | api |
| `Ngrok__ApiUrl` = `http://ngrok:4040` | api |
| `Webhook__WatchdogIntervalSeconds` = `60` | api (optional) |
| `Outbox__SweepIntervalSeconds` = `30`, `Outbox__StaleAfterSeconds` = `60` | worker (optional) |

Startup **fails fast** with a clear error if a required key is **missing or malformed** (for example `TELEGRAM_WEBHOOK_SECRET` not matching its regex); the error names the key and never its value. A key that is present but **invalid at runtime** (a wrong bot token, a bad ngrok token) only logs errors and retries. The inbox keeps working (D-041).

## 7. Wire, persistence and runtime conventions (added in Step #7; binding)

### 7.1 JSON (one source of options)
- A single `InboxJson.Options` factory in Infrastructure: `JsonSerializerDefaults.Web` (camelCase) plus `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`. It is used by the **HTTP JSON options**, **`AddSignalR().AddJsonProtocol`** and **every RabbitMQ serializer**. No other `JsonSerializerOptions` instance is allowed.
- Timestamps: `DateTimeOffset` in UTC. On the wire they serialize as STJ does, for example `2026-11-05T14:03:22.1234567+00:00`. **Clients must parse with `Date.parse`/`new Date()` and never compare timestamp strings.**
- The `inbox.events` relay deserializes `payload` as `JsonElement` and forwards it **unchanged** to `Clients.All.SendAsync(eventType, payload)`.

### 7.2 Persistence
- Enums are stored as **lowercase strings** through an explicit EF `ValueConverter` (`"inbound"`, `"pending"`…). They must match the CHECK constraints. `HasConversion<string>()` without the lowercase mapping is **forbidden**.
- `seq`: `UseIdentityAlwaysColumn()` with `ValueGeneratedOnAdd`. Application code never writes it.
- Migrations: EF Core migrations live in `Inbox.Infrastructure`, with a design-time `IDesignTimeDbContextFactory`, and the `dotnet-ef` local tool in `backend/.config/dotnet-tools.json` (allowed). The api applies them **before `app.Run()`**, so "healthy" implies "migrated".

### 7.3 Port isolation (security boundary, D-020)
- It is enforced by **`HttpContext.Connection.LocalPort`**, never by `RequireHost`. Behind ngrok the Host header is the ngrok domain, so `RequireHost("*:8081")` would 404 every Telegram update.
- On **8081**, everything except `POST /telegram/webhook` returns 404. On **8080**, `/telegram/webhook` returns 404.

### 7.4 Inbound persistence recipe (worker, `ProcessIncomingMessage`)
1. Begin a transaction.
2. Load the contact by `telegram_chat_id`; otherwise `Contact.FromTelegram(...)`. Call `UpdateProfile(...)` through the domain. There is **no concurrent upsert**, because there is one consumer with prefetch 1 (this is an assumption: scaling consumers requires `INSERT … ON CONFLICT` on contacts).
3. Build `Message.Inbound(...)`.
4. Insert it with `ExecuteSqlInterpolatedAsync("INSERT … ON CONFLICT (telegram_update_id) DO NOTHING")`. If 0 rows are affected it is a duplicate: load the existing message by `telegram_update_id`.
5. Commit, then publish `messageReceived` (for new messages **and** duplicates), then ack.

**Never catch the unique violation (SQLSTATE 23505) inside the transaction.** PostgreSQL aborts the transaction (25P02).

### 7.5 RabbitMQ publishing rules
- Work messages (`inbox.incoming`, `inbox.outgoing`) are published with `mandatory: true`, persistent delivery and publisher confirms (5 s). A **basic.return** (unroutable) counts as a failure: the webhook returns 503, and for an outbound reply the row stays Pending and the sweeper retries it.
- Events (`inbox.events`) are transient, with no confirms and not mandatory. If an event publish fails **after** the commit, the worker nacks with requeue. Re-processing is idempotent, and duplicate events are harmless.
- Quorum `x-delivery-limit: 5`: a message is dead-lettered when its delivery count **exceeds** 5, which means up to 6 deliveries.
- **There is no RabbitMQ definitions file.** The topology is declared only by one shared `RabbitMqTopology` class in Infrastructure, which both hosts call at startup.

### 7.6 Telegram error classification (`ITelegramGateway`)
| Telegram response | Classification | Worker action |
|---|---|---|
| 2xx `ok: true` | success | `MarkSent(message_id, date)` |
| 429 | transient | retry after `min(retry_after, 10 s)` |
| 5xx, network error, timeout (10 s) | transient | retry after 1 s, 2 s, 4 s |
| any other 4xx (400, 401, 403, 404…) | permanent | `MarkFailed("rejected: <description>")` |

A transient error that is still failing after 3 retries leads to `MarkFailed("unavailable: <description>")`. The worst case per message is about 30 s, which keeps D-035's order of magnitude.
- `sendMessage` is called with `chat_id` and `text` only, and **never `parse_mode`** (text is sent literally). Length is counted in UTF-16 code units: C# `string.Length` and JS `.length` agree.
- The bot token appears in request URLs, so **HttpClient logging must redact `/bot<token>/`**.

### 7.7 nginx (frontend container, owned by A4)
```nginx
map $http_upgrade $connection_upgrade { default upgrade; '' close; }
location /api/  { proxy_pass http://api:8080; }      # no URI part, so the path is passed unchanged
location /hubs/ { proxy_pass http://api:8080; proxy_http_version 1.1;
                  proxy_set_header Upgrade $http_upgrade; proxy_set_header Connection $connection_upgrade;
                  proxy_buffering off; proxy_read_timeout 3600s; }
location /      { try_files $uri $uri/ /index.html; }
```

### 7.8 ngrok container (owned by A4)
- Config file in `infra/ngrok/ngrok.yml` with `web_addr: 0.0.0.0:4040` stated explicitly, so the api can reach `ngrok:4040`.
- The tunnel targets `api:8081`. The `--url=$NGROK_DOMAIN` flag is added **only if `NGROK_DOMAIN` is non-empty**, through an `sh -c` entrypoint. An empty value must never be passed.

### 7.9 Health
- Api readiness: an EF Core DbContext check plus a custom `RabbitMqHealthCheck` (`IConnection.IsOpen`). No RabbitMQ health package is used.
- Compose: postgres `pg_isready`; rabbitmq `rabbitmq-diagnostics -q check_port_connectivity`; api `curl -fsS http://localhost:8080/health/ready`.

### 7.10 Frontend real-time and state rules
- The client starts the SignalR connection **before** the first REST fetch.
- Reconnect policy: retries never stop (0, 2, 5, 10 s, then every 30 s). `onclose` restarts `start()` with the same backoff. The first `start()` is retried as well.
- **Status precedence:** a message upsert never replaces `sent` or `failed` with `pending`, except through a `messageStatusChanged` event (a retry). The api pushes `messageQueued` **before** publishing to RabbitMQ.
- The contact list is re-sorted on every event by `lastMessage.occurredAt` desc. Messages are sorted by `(occurredAt, seq)`.
