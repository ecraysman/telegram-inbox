# Contracts (Step #3): FROZEN during Step #8

> These are the interfaces shared between agents A2 (backend), A3 (frontend) and A4 (platform).
> **Only the orchestrator may change this file.** An agent that needs a change stops and reports it.
> JSON is camelCase. Enums are serialized as camelCase strings. Every timestamp is ISO-8601 UTC (`2026-11-05T14:03:22.123Z`). IDs are UUID strings.

## 1. Network and ports

| Service | Container port | Host port | Notes |
|---|---|---|---|
| frontend (nginx) | 80 | `${FRONTEND_PORT:-4200}` | Serves the SPA. Proxies `/api/*` and `/hubs/*` (WebSocket upgrade, no buffering) to `api:8080` |
| api | 8080 | — | REST + SignalR + `/health/*` |
| api | 8081 | — | **Only** `POST /telegram/webhook`; the ngrok target |
| ngrok | 4040 | 4040 | Agent API (`/api/tunnels`) + inspector |
| rabbitmq | 5672 / 15672 | — / 15672 | Management UI on the host for the demo |
| postgres | 5432 | — | Not exposed |

## 2. HTTP API (api:8080, reached via nginx at `/api`)

### DTOs
```ts
type Direction = 'inbound' | 'outbound';
type MessageStatus = 'received' | 'pending' | 'sent' | 'failed'; // inbound is always 'received'

interface MessageDto {
  id: string; contactId: string; direction: Direction; text: string;
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
| `POST /api/contacts/{contactId}/read` | `{ "upToMessageId": "uuid" }` | 204. Marks unread inbound messages at or before that message (by `(occurredAt, seq)`) as read. Idempotent | 400 (message does not belong to the contact), 404 |
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
          delivered_at timestamptz NULL )              -- outbound: Telegram's date on success

ix_messages_unread        ON messages(contact_id) WHERE read_at IS NULL
ix_messages_conversation  ON messages(contact_id, occurred_at DESC, seq DESC)
ix_messages_pending       ON messages(occurred_at) WHERE status = 'pending'
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

Startup **fails fast** with a clear error if a required key is missing; the error names the key and never its value.
