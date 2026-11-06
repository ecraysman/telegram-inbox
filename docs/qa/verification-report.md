# Informe de verificación (QA, Step #9: Q-1…Q-4)

> Autor: A5 QA (independiente; no escribió el código). Fecha: 2026-11-05/06. Commit verificado: `48698b6`.
> Criterio: todo está roto hasta que se demuestre lo contrario. Cada fila tiene evidencia real (comando + salida resumida).
> Estados: **PASS** · **FAIL** · **NOT-VERIFIED** (con el motivo y quién lo verifica).

## 0. Entornos usados

| Entorno | Para qué | Notas |
|---|---|---|
| **Stack LIVE** (`docker compose`, `.env` real del humano) | Solo lecturas: `docker compose ps`, logs, `GET` en `localhost:4200`, `rabbitmqctl`, `getWebhookInfo` y la frontera de seguridad por la URL pública de ngrok | QA nunca leyó `.env`. No se detuvo, reinició ni recreó ningún contenedor live y no se le enviaron datos |
| **Stack descartable QA** | Escenarios simulados de Telegram e inyección de fallas | Red propia `qa-net`, `qa-postgres` (postgres:18.6-alpine), `qa-rabbitmq` (rabbitmq:4.3-management-alpine, 4.3.7), `qa-api` en `127.0.0.1:58080/58081`, `qa-worker`, `qa-frontend` (imagen `inbox-frontend`) en `127.0.0.1:58200`. Secreto de prueba y token *dummy* con formato válido (`123456:TEST…`); `Ngrok__ApiUrl` apunta a un host inexistente. Sweeper acelerado: `Outbox__SweepIntervalSeconds=10`, `Outbox__StaleAfterSeconds=20`. **Imágenes api/worker reconstruidas desde el árbol de `48698b6`** (ver riesgo R-6). Todo se eliminó al terminar |
| **Oyente SignalR** | Ver el *push* en tiempo real | Script Node con `@microsoft/signalr` 10.0.11 (el `node_modules` del frontend), transporte WebSockets, contra `qa-api:8080/hubs/inbox` y también a través de nginx (`qa-frontend/hubs/inbox`) |

## 1. Build y tests (Q-1)

| Ítem | Estado | Evidencia |
|---|---|---|
| Backend build (Release) | PASS | `dotnet build -c Release` en `sdk:10.0` con `--user`: `Build succeeded. 0 Warning(s) 0 Error(s)` |
| Tests de dominio (antes de QA) | PASS | `Passed! - Failed: 0, Passed: 60, Skipped: 0, Total: 60` |
| Tests de dominio (con los 9 que agregó QA) | PASS | `Passed! - Failed: 0, Passed: 69, Skipped: 0, Total: 69`; build `0 Warning(s)` |
| Frontend build | PASS | `npm ci && npx ng build` en `node:24-alpine`: `Application bundle generation complete`, `main` 243.64 kB |
| Tests del frontend (Vitest) | PASS (ver R-5) | `npx ng test --watch=false`: `Test Files 5 passed (5)`, `Tests 33 passed (33)` |
| Lint | NOT-VERIFIED | El repo no tiene un *linter* configurado (no hay `ng lint` ni analizadores extra); el único control es `TreatWarningsAsErrors` (0 warnings) |

Tests agregados (solo invariantes que no estaban cubiertas; convención `Method_Scenario_Expected`):
`Outbound_EmptyContact_Throws`, `RequeueForRetry_NonUtcTime_Throws`, `RecordRequeued_NonUtcTime_Throws`, `MarkFailed_AlreadyFailed_KeepsTheOriginalReason`, `MarkSent_AlreadySent_KeepsTheFirstTelegramData`, `Outbound_ThroughFailAndRetry_IsNeverUnreadAndKeepsItsPosition` (MessageTests); `UpdateProfile_OnlyWhitespaceOrAtSignDiffers_ReturnsFalse`, `UpdateProfile_BlankLastNameAndUsername_AreStoredAsNull` (ContactTests); `MarkConversationReadUpTo_OutboundReplies_AreNotCountedNorChanged` (ConversationReadTests).

## 2. Requisitos funcionales (§3.1)

| # | Requisito | Estado | Evidencia |
|---|---|---|---|
| 3.1.1 | Recibir en tiempo real los mensajes de texto del bot | **PASS** (simulado) · NOT-VERIFIED (humano, con Telegram real) | Webhook simulado con un `Update` de texto → `200`; fila `inbound/received/unread` en `messages`; el oyente recibió `messageReceived` ~0,3 s después, con `contact.unreadCount=1` y `lastMessage`. También probado a través de nginx (WebSocket por `/hubs/`) → `messageReceived` recibido. Con Telegram real lo cubre el checklist (paso 3) |
| 3.1.2 | Listado de contactos con nombre/username, no leídos y vista previa | **PASS** (API) · NOT-VERIFIED (UI, humano) | `GET /api/contacts` → `displayName` `"Ana Pérez"`, `"@solo_user"` (sin nombre → `@username`), `unreadCount`, `lastMessage` completo; orden por `lastMessage.occurredAt` desc. La vista se verifica en el checklist (pasos 3–4) |
| 3.1.3 | Abrir una conversación y ver su historial | **PASS** (API) · NOT-VERIFIED (UI, humano) | `GET /api/contacts/{id}/messages` → historial completo ordenado por `(occurredAt, seq)`, entrantes y salientes; `404 contact_not_found` para un id inexistente |
| 3.1.4 | Marcar como leídos al abrir | **PASS** (API) · NOT-VERIFIED (UI, humano) | Con 3 no leídos, `POST /read {upToMessageId: "segundo"}` → `204`; quedan leídos los 2 primeros y el tercero sigue `readAt=null`, `unreadCount` 3 → 1; un único `conversationRead`. Repetido → `204` y **sin** evento nuevo (idempotente) |
| 3.1.5 | Responder; la respuesta llega a Telegram | **PASS** (todo el camino hasta Telegram) · **NOT-VERIFIED (humano)**: `sendMessage` exitoso | `POST /messages` → `202` `pending` + `messageQueued`; el worker llamó al Bot API real; con el token *dummy* Telegram respondió `401` → `failed`, `"rejected: Unauthorized"` + `messageStatusChanged`. El caso exitoso (`sent`, ✓) solo se puede ver con el bot real: checklist pasos 5–6 |

## 3. Requisitos técnicos (§3.2)

| Área | Estado | Evidencia |
|---|---|---|
| Backend .NET, última estable, Clean Architecture | **PASS** (ver R-7) | `sdk:10.0` / `aspnet:10.0` / `runtime:10.0`. Referencias: Domain → nada; Application → Domain (+ abstracciones de DI/Logging); Infrastructure → Application, Domain; Api/Worker → Application, Infrastructure; tests → Domain. `grep` de `EntityFrameworkCore|RabbitMQ|AspNetCore|HttpClient` en Domain/Application: 0 archivos. `DateTime.Now/UtcNow` en `src`: 0 |
| Frontend Angular, última estable | **PASS** (ver R-7) | `@angular/core` 22.2.4 (lockfile), `@microsoft/signalr` 10.0.11 |
| Base de datos | PASS | PostgreSQL 18.6 (ADR-001); migraciones aplicadas al iniciar la api (la api está *healthy* solo después de migrar) |
| RabbitMQ para entrantes | PASS | Live: `inbox.incoming` (direct) → `inbox.incoming.telegram` (quorum, `x-delivery-limit 5`, DLX `inbox.dlx`, rk `incoming`), DLQs quorum, `inbox.outgoing`, `inbox.events` (fanout) + cola `amq.gen-…` del relay. `list_consumers`: 1 consumidor por cola de trabajo, `ack_required=true`, `prefetch_count=1` |
| Tiempo real | PASS | SignalR `/hubs/inbox`; negociación por nginx en live (`POST localhost:4200/hubs/inbox/negotiate` → `WebSockets, ServerSentEvents, LongPolling`); eventos recibidos por WebSocket (directo y a través de nginx) |
| Telegram: bot propio y webhook registrado automáticamente con la URL de ngrok | **PASS** (live) | Log de la api live: `Telegram webhook registered at https://pithecoid-…ngrok-free.dev/telegram/webhook`. `getWebhookInfo` (ejecutado dentro del contenedor, el token nunca salió del contenedor): `url` = la de ngrok + `/telegram/webhook`, `allowed_updates=['message']`, `pending_update_count=0`, `last_error_message=None` |
| ngrok expone el webhook | PASS | Ver §5 (frontera de seguridad) |
| Docker Compose: `docker compose up` tras completar `.env` | PASS (live) | `docker compose ps`: postgres, rabbitmq, api, frontend **healthy**; worker y ngrok **Up** (el worker no tiene healthcheck por diseño, D-038). La prueba con un `.env` limpio la hace el humano (checklist paso 1) |
| Fail fast de configuración | PASS | api con `Telegram__WebhookSecret='short!'` → `Configuration key 'Telegram__WebhookSecret' is missing or malformed.`; worker con `Telegram__BotToken=notatoken` → idem `Telegram__BotToken`. El valor nunca aparece |

## 4. Fuera de alcance (§3.3)

| Ítem | Estado | Evidencia |
|---|---|---|
| Sin autenticación ni usuarios | PASS | No hay endpoints ni UI de login; la API no está expuesta públicamente (§5) |
| Solo texto | PASS | `sticker`, `photo`+`caption`, `edited_message`, chat de grupo → `200` y **ninguna** fila nueva (`select … order by seq` muestra solo los textos privados) |
| Sin responsive | PASS (código) | Decisión documentada (AGENTS §6); no se agregó nada |
| Sin paginación del historial | PASS | `GET …/messages` devuelve el historial completo, sin parámetros de página |

## 5. Frontera de seguridad (D-020, contracts §7.3), sobre la URL pública LIVE

| Prueba | Esperado | Resultado | Estado |
|---|---|---|---|
| `GET /api/contacts` | 404 | `404 len=0` | PASS |
| `GET /hubs/inbox/negotiate` | 404 | `404 len=0` | PASS |
| `POST /hubs/inbox/negotiate?negotiateVersion=1` | 404 | `404 len=0` | PASS |
| `GET /health/ready`, `GET /health/live`, `GET /`, `GET /telegram/webhook` | 404 | `404 len=0` | PASS |
| `POST /api/contacts/{id}/messages` | 404 | `404 len=0` (cortado por el middleware de puertos, sin llegar a ningún handler) | PASS |
| `POST /telegram/webhook` sin secreto | 401 | `401` | PASS |
| `POST /telegram/webhook` con secreto incorrecto | 401 | `401` | PASS |
| Puerto 8080: `POST /telegram/webhook` | 404 | QA stack `:58080` → `404` vacío | PASS |
| nginx no publica el webhook | — | `POST localhost:4200/telegram/webhook` → `405` (cae en el `location /` estático) | PASS |
| Logs sin token, secreto ni texto | 0 apariciones | `docker logs qa-api/qa-worker | grep -c` token/secreto → 0; texto de mensajes en Information+ → 0 | PASS |

Nota: el primer intento del barrido devolvió `503 ERR_NGROK_3004` por un error del script de QA (zsh no separa palabras en `set -- $p` y mandó un método inválido); repetido con un método válido dio los 404 de la tabla. No es un defecto del producto.

## 6. Escenarios de Telegram simulados (Q-2) y salientes (Q-3), stack QA

| Escenario | Estado | Evidencia |
|---|---|---|
| Texto válido (con acento y emoji) | PASS | `200`; contacto `555001 Ana Pérez anap`; mensaje `inbound/received/unread`, texto intacto `Hola, ¿tienen stock? 😀`; `messageReceived` |
| `update_id` duplicado (idempotencia) | PASS | 2.º POST → `200`; sigue **1** fila; log `Update 900001 was already stored … re-notifying`; segundo `messageReceived` con el **mismo** `message.id` (inofensivo por upsert) |
| Secreto incorrecto / ausente | PASS | `401` / `401`; nada en la cola ni en la base |
| No texto (sticker, foto, edición, grupo) | PASS | `200`, ignorados, sin filas (ver §4) |
| JSON inválido, `{}`, `Content-Type: text/plain` | PASS | `200` (ignorado, contracts §2) |
| Sin `from`, solo `chat.username` | PASS | contacto `@solo_user` (fallback de `displayName`) |
| Texto solo con espacios | PASS | webhook `200`; el worker lo clasifica como veneno → `inbox.incoming.telegram.dlq` +1; 0 filas |
| **Broker caído → 503** | PASS | `docker stop qa-rabbitmq`; webhook → `503` en 16 ms; `/health/ready` → `503 Unhealthy`; 0 filas. Al volver el broker, el reintento de Telegram (mismo update) → `200` y 1 fila |
| Mensaje no ruteable (*basic.return*) → 503 | PASS | Se borró el binding `telegram.text`: webhook → `503`, log `PublishReturnException: 312 NO_ROUTE`; binding restaurado → `200`, 1 fila |
| Respuesta → `Failed` (token inválido) + Reintentar | PASS | `202 pending` + `messageQueued` → `failed "rejected: Unauthorized"` + `messageStatusChanged`. `POST /retry` → `202 pending` (`messageStatusChanged pending`) → `failed` otra vez; `occurredAt` sin cambio, `last_queued_at` actualizado |
| Retry de un mensaje no `failed` | PASS | `409 message_not_failed` |
| Error transitorio de red (Telegram inaccesible) | PASS | worker con `--add-host api.telegram.org:127.0.0.1`: reintentos 1 s, 2 s, 4 s y luego `failed "unavailable: network error (ConnectionError)"` (~8 s) |
| 429 con `retry_after` / 5xx reales de Telegram | NOT-VERIFIED | No se pueden provocar sin un Telegram falso (la URL base está fija en `https://api.telegram.org/`). Cubierto solo por lectura de código (`TelegramGateway`, `DeliverReply`) |
| Texto literal (sin `parse_mode`) | PASS (parcial) | `Sí, tenemos *stock* <b>hoy</b>` se guardó y se envió literalmente (el request lleva solo `chat_id`+`text`, código). Que Telegram lo **muestre** literal lo ve el humano (checklist paso 5) |
| Validaciones REST | PASS | texto en blanco / 4097 caracteres → `400 validation_failed`; contacto o mensaje inexistente → `404 contact_not_found` / `message_not_found`; `upToMessageId` de otro contacto → `400`; body ausente o basura → `400`; id no GUID → `404 not_found`; método inválido → `405 method_not_allowed`. Todo `application/problem+json` con `code` |

## 7. Tolerancia a fallas

| Ítem | Estado | Evidencia |
|---|---|---|
| **Idempotencia** entrante | PASS | §6 (duplicado) + los casos de reconexión de abajo terminan en **1** fila por `update_id` |
| **Caída del broker (503)** | PASS | §6. Tras `docker start qa-rabbitmq`: api y worker reconectan solos, `/health/ready` → 200, 1 consumidor por cola, relay de eventos recreado (`amq.gen-…`, 1 consumidor) y los *push* siguen llegando |
| **Caída de la base** | PASS | `docker stop qa-postgres`: webhook → `200` (el broker sigue bien); el mensaje queda en `inbox.incoming.telegram` (1 sin ack); el worker reintenta sin límite (`attempt 1…5; retrying in 1/2/4/8/16s until it recovers`), DLQ sin cambios. REST → `500 internal_error`, `/health/ready` → 503. `docker start qa-postgres` → guardado en 6 s, `messageReceived`, cola en 0 |
| **Veneno → DLQ** | PASS | JSON inválido en `inbox.incoming` → DLQ entrante; `{"messageId": <inexistente>}` y `null` en `inbox.outgoing` → DLQ saliente; texto en blanco → DLQ entrante. Logs `Poison message … dead-lettering it` |
| **Error inesperado no transitorio → límite de entregas → DLQ** (contracts §7.5, D-084) | **FAIL** (defecto QA-D1) | Ver §9 |
| **Saliente `Failed` + reintento** | PASS | §6 |
| **Barredor (outbox)** | PASS | Respuesta con el broker caído → `202 pending` en 31 ms (el confirm falló, la fila quedó `pending`). Al volver el broker: `Outbox sweeper re-published 1 stale Pending replies` y la respuesta pasa a `failed` (token *dummy*) a los 16 s, con `messageStatusChanged` |
| **D-092 (1): pérdida de conexión con un handler en curso** | **PASS** (con observación QA-D2) | Entrante: postgres caído, mensaje en reintento, `docker restart qa-rabbitmq`: el handler **no** se canceló (intentos 4 y 5 después del reinicio); al volver la base se guardó, el ack se saltó (`Delivery 5 … not settled … broker requeued it`), la copia reentregada tomó el camino de duplicado (`already stored … re-notifying`), 1 fila. Saliente: post-envío con el guardado fallando (CHECK forzado), `docker restart qa-rabbitmq` con `consumer_timeout` por defecto: el guardado siguió reintentando (`attempt 4…6 … without resending`), terminó `failed`, ack saltado, la copia reentregada encontró `Failed` y no reenvió. **`Calling sendMessage` para ese mensaje: 1 vez.** Después: exactamente 1 consumidor por cola (`list_consumers`) |
| **D-092 (2): `basic.cancel` del servidor** | **PASS** | `RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS="-rabbit consumer_timeout 15000"` (verificado `{ok,15000}`), postgres caído, 1 mensaje entrante. Cada ~17 s: `Lost the subscription … basic.cancel from the broker` → `Stopped processing … the broker has requeued it` → `Consuming inbox.incoming.telegram` (7 ciclos en 2 min). Nunca 0 ni 2 consumidores; la DLQ no cambió (los *timeouts* no cuentan para el límite de entregas). Con postgres de vuelta: guardado en 10 s, 1 fila, cola en 0, 1 consumidor |
| Reenvío por `consumer_timeout` durante un guardado post-envío fallido | Comportamiento documentado (ADR-004) | Con `consumer_timeout` 15 s y el guardado fallando, el broker devolvió el mensaje y hubo **2** `sendMessage`. Es el riesgo aceptado en ADR-004 ("una vez cada 30 min mientras dure la falla"); confirmado tal cual |
| Watchdog de webhook (re-registro si cambia la URL) | NOT-VERIFIED | Requiere cambiar la URL de ngrok en el stack live, lo que estaba prohibido. Solo hay evidencia del registro inicial |

## 8. Criterio de lectura y estado de las respuestas en la UI

NOT-VERIFIED por QA (no se usó un navegador). Por código: el store marca como leído al abrir y al recibir un `messageReceived` de la conversación abierta (`inbox.store.ts:185, 211`), refetch en `onreconnected` (`realtime.service.ts:72`), reconexión sin fin. Lo verifica el humano con el checklist (`docs/qa/manual-e2e-checklist.md`).

## 9. Defectos

### QA-D1 (mayor, contra contracts §7.5 / D-084): la clase "otro error inesperado" nunca llega a la DLQ en RabbitMQ 4.3
- **Pasos:** stack QA; `alter table messages add constraint qa_block check (text <> 'qa-block')`; webhook con texto `qa-block`.
- **Esperado (contracts §7.5):** 3 reintentos en proceso (1, 2, 4 s), luego `nack` con `requeue`; el `x-delivery-limit: 5` lo manda a `inbox.incoming.telegram.dlq` tras ≤ 6 entregas.
- **Real:** `Still failing … requeueing` **29 veces** en ~3,5 min; la DLQ nunca cambió (siguió en 2). En otra corrida, tras 7 `nack(requeue)` del worker, el mensaje tenía `x-delivery-count: 1`: en RabbitMQ 4.3.7 el `basic.nack` con `requeue=true` de un consumidor **no incrementa** el contador de entregas (con la misma versión, `basic.get` + `reject requeue` vía la API HTTP sí lo incrementó: 1, 2 y luego lo descartó). El mensaje queda en un bucle infinito cada ~7 s (ruido en logs, CPU) hasta que se corrige la causa; entonces se guarda (1 fila).
- **Atenuante observado:** no bloquea la cola: un mensaje normal enviado durante el bucle se guardó en 6 s (el reencolado va al final).
- **Dueño:** A2 (`QueueConsumer.cs`). Opciones: contar intentos en la aplicación (p. ej. *republish* con un header de intentos y `ack` del original) o, tras N reentregas detectadas, `nack(requeue:false)` → DLQ; o aceptar y documentar el comportamiento (requiere actualizar contracts §7.5, D-084 y ADR-003).

### QA-D2 (menor, riesgo de borde de D-088): un evento de cierre por pérdida de conexión puede llegar tarde y clasificarse como "suscripción perdida"
- **Pasos:** postgres caído con un mensaje entrante en reintento; `docker restart qa-rabbitmq`; levantar postgres.
- **Esperado:** cada consumidor ve el cierre por pérdida de conexión con `IsOpen=false` (solo avanza la generación).
- **Real:** el cierre del canal **saliente** (`320 CONNECTION_FORCED … 'shutdown'`, es decir, la pérdida de conexión de las 01:59:18) se procesó recién a las 01:59:44, cuando terminó el handler entrante en curso. Para entonces la conexión ya estaba recuperada (01:59:24), así que se tomó como "suscripción perdida en una conexión viva" y se recreó canal y consumidor. El estado final es correcto (1 consumidor por cola), pero si en ese instante hubiera una entrega saliente en curso, su token se cancelaría: es justo lo que D-088 quiere evitar (reenvío tras un guardado abortado). Probabilidad baja: requiere un reinicio del broker mientras otro handler está bloqueado en reintentos.
- **Dueño:** A2. Opción: decidir por `ShutdownEventArgs` (p. ej. `ReplyCode 320` / cierre a nivel conexión) en vez de `IConnection.IsOpen` en el momento del evento.

### Observaciones (no son defectos)
- **O-1:** durante una falla persistente del guardado post-envío, el barredor republica el `Pending` en cada barrido (vimos 2 copias extra). Se drenaron sin reenviar (`already Failed; not sending again`). Es coherente con ADR-004, pero agranda la cola mientras dura la falla.
- **O-2:** con la base caída, la REST responde `500 internal_error` (no 503). Es compatible con el contrato (código genérico) y con ADR-005 ("el operador ve un error").
- **O-3:** el webhook con `Content-Type` distinto de JSON responde `200` (ignorado). Telegram siempre manda JSON; coherente con "no parseable → 200".

## 10. Riesgos y pendientes
- **R-5:** el frontend tiene 33 tests unitarios (Vitest). requirements §6 dice "si los incluís, que sean unitarios sobre el dominio" y AGENTS §5 pide tests solo del dominio. D-062 aceptó Vitest; conviene que A0 o el humano confirmen que esto no se lee como desvío.
- **R-6:** las imágenes live `inbox-api`/`inbox-worker` se crearon a las 22:19:57, y `ConsumerErrorClassifier.cs` se modificó a las 22:23:23 (commit 22:23:31). QA verificó con imágenes reconstruidas desde `48698b6`. Antes de la prueba humana: `docker compose up -d --build` para asegurar que el live corre el commit.
- **R-7:** "última versión estable": .NET 10 y Angular 22.2 lo son a la fecha, pero .NET 11 y Angular 23 suelen salir en noviembre. Si salen antes de la entrega, documentarlo como supuesto en el README.
- NOT-VERIFIED que dependen del humano: `sendMessage` exitoso (✓), recepción real desde Telegram, bloqueo del bot → `Failed` → desbloqueo → Reintentar, UI (estilo, layout, no leídos, marcado al abrir), reconexión del navegador.

## Anexo del orquestador (2026-11-06): estado posterior de los defectos

Este informe refleja lo que QA encontró sobre `48698b6`. Los dos defectos se corrigieron en la ronda R-4 (`7202599`), con aprobación del humano:

| Defecto | Corrección | Verificación |
|---|---|---|
| QA-D1 | D-093: tras 3 reintentos en proceso (1, 2 y 4 s), `nack` sin requeue → DLQ; `x-delivery-limit` queda solo como respaldo | A2, stack descartable: error no transitorio forzado con un CHECK → el mensaje llega a `inbox.incoming.telegram.dlq` a los ~7 s, una sola vez (con RabbitMQ 4.4.0; el arreglo no depende del contador de entregas). Re-revisión A6 R-4: corregido |
| QA-D2 | D-094: "suscripción perdida" se decide por los `ShutdownEventArgs` del propio canal, y el `basic.cancel` del servidor tiene su propio camino (`WorkQueueConsumer`) | A2: reinicio del broker con un handler ocupado → sin "Lost the subscription" espurio, 1 consumidor por cola. Re-revisión A6 R-4: corregido |

Stack live reconstruido con `7202599`: servicios sanos, 1 consumidor por cola, DLQs vacías y 0 errores en los logs. Detalle en `docs/qa/traceability.md`.
