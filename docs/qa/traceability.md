# Trazabilidad y auditoría de desvíos (Step #10)

> Autor: A0 (orquestador). Fecha: 2026-11-06. Commit auditado: `7202599` (más las correcciones de documentación de este paso).
> Método: cada fila se contrastó con el código (`grep` y lectura de los archivos citados) y con la evidencia de QA (`docs/qa/verification-report.md`, abreviado **VR**). Nada se marca como cumplido solo porque un documento lo afirma.
> Estados: ✅ cumplido · ⚠️ desvío aceptado (ver §3) · 🔁 reemplazado por una decisión posterior · ⏳ pendiente de la prueba humana.

## 1. Requisitos → decisión → código → evidencia

### 1.1 Funcionales (§3.1)

| # | Requisito | Decisión | Código | Evidencia | Estado |
|---|---|---|---|---|---|
| 1 | Recibir en tiempo real los mensajes de texto | ADR-002, ADR-003, D-011, D-015 | `Api/Endpoints/TelegramWebhookEndpoint.cs` → `Application/UseCases/AcceptIncomingMessage.cs` → `Worker/Consumers/IncomingMessageConsumer.cs` → `UseCases/ProcessIncomingMessage.cs` → `Messaging/RabbitMqInboxNotifier.cs` → `Messaging/InboxEventsSubscriber.cs` → `Api/RealTime/SignalRInboxNotifier.cs` | VR §2 3.1.1 y §6: `messageReceived` ~0,3 s después del webhook, también a través de nginx | ✅ simulado · ⏳ con Telegram real |
| 2 | Contactos con nombre/username, no leídos y vista previa | D-022, D-026 | `Infrastructure/Persistence/InboxQueries.cs:50` (conteo y último mensaje en SQL), `Domain/Contacts/Contact.cs` (`DisplayName`, `UpdateProfile`), `frontend/.../contacts/contact-list.*` | VR §2 3.1.2: `GET /api/contacts` con `displayName`, `unreadCount` y `lastMessage` | ✅ API · ⏳ UI |
| 3 | Abrir una conversación y ver su historial | D-016 | `InboxQueries.cs:34` (`occurred_at`, `seq`), `features/conversation/conversation-panel.*` | VR §2 3.1.3 | ✅ API · ⏳ UI |
| 4 | Marcar como leídos al abrir | D-023, D-024, D-065 | `Contact.MarkConversationReadUpTo` (`Contact.cs:77`), `UseCases/MarkConversationAsRead.cs`, `state/inbox.store.ts:185` | VR §2 3.1.4 (parcial, idempotente, un solo evento); 5 tests en `ConversationReadTests.cs` | ✅ API · ⏳ UI |
| 5 | Responder; la respuesta llega a Telegram | D-021, D-029..D-032, ADR-004 | `UseCases/QueueReply.cs`, `UseCases/DeliverReply.cs`, `Telegram/TelegramGateway.cs:24`, `Worker/Outbox/OutboxSweeperService.cs` | VR §6: `202 pending` → worker → Bot API real (401 con token falso → `failed`) | ⏳ un `sendMessage` exitoso solo se ve con el bot real (checklist pasos 5–6) |

### 1.2 Técnicos (§3.2)

| Área | Decisión | Código / configuración | Evidencia | Estado |
|---|---|---|---|---|
| .NET última estable, Clean Architecture | D-005, D-007, D-008, D-054 | `backend/Dockerfile` (`sdk:10.0`, `aspnet:10.0`, `runtime:10.0`), referencias entre proyectos (`*.csproj`), `Directory.Packages.props` | VR §3; 0 referencias a EF Core, RabbitMQ, AspNetCore o HttpClient en Domain y Application. Al 2026-11-06 .NET 11 sigue en RC (GA prevista para .NET Conf, 10/11) | ✅ |
| Angular última estable | D-005, D-062 | `frontend/package.json` (`@angular/core` 22.2.x, zoneless, sin zone.js) | VR §3. Angular pasó a un ciclo anual: Angular 23 está previsto para junio de 2027 | ✅ |
| Base de datos a elección | D-004, ADR-001 | `Persistence/InboxDbContext.cs`, `Configurations/*`, migración `InitialCreate` | VR §3: migraciones al iniciar; `ON CONFLICT` probado en VR §6 | ✅ |
| RabbitMQ para entrantes | D-009, D-012..D-015, D-033, ADR-003 | `Messaging/RabbitMqTopology.cs`, `RabbitMqPublisher.cs:37` (`mandatory`, confirm), `QueueConsumer.cs:74` (`prefetch 1`) | VR §3 (topología live, 1 consumidor por cola), VR §7 (broker caído → 503, veneno → DLQ) | ✅ |
| Tiempo real | D-017, ADR-002, D-063, D-064 | `Api/RealTime/InboxHub.cs`, `core/realtime.service.ts:62` (reconexión sin fin), `:72` (refetch al reconectar) | VR §3 (negociación y WebSocket por nginx) | ✅ backend · ⏳ navegador |
| Bot propio + webhook automático con ngrok | D-018, D-019, D-042 | `Api/Webhooks/WebhookRegistrationService.cs`, `UseCases/RegisterWebhook.cs`, `Ngrok/NgrokPublicUrlProvider.cs` | VR §3: log de registro y `getWebhookInfo` con la URL de ngrok y `allowed_updates=['message']` | ✅ (el re-registro ante un cambio de URL no se verificó en vivo, VR §7) |
| ngrok expone el webhook | D-020, D-044 | `docker-compose.yml:63` (`ngrok http api:8081`), `Api/PortIsolationMiddleware.cs:14` | VR §5: todo lo que no es el webhook → 404 por la URL pública; webhook sin secreto → 401 | ✅ |
| `docker compose up` tras completar `.env` | D-038, D-041, D-057, D-058, D-060 | `docker-compose.yml` (6 servicios, healthchecks, `depends_on`), `.env.example` | VR §3: stack live sano. Reconstruido con R-4 (`7202599`): sano, 1 consumidor por cola | ✅ · ⏳ desde un `.env` limpio (checklist paso 1) |

### 1.3 Fuera de alcance (§3.3), diseño (§4) y condiciones (§6)

| Ítem | Decisión | Evidencia | Estado |
|---|---|---|---|
| Sin autenticación ni usuarios | D-027, D-020 | No hay login; la API solo se publica en `127.0.0.1` y no por ngrok (VR §4–§5) | ✅ |
| Solo texto | D-025 | `TelegramUpdateMapper.cs:12`; sticker, foto, edición y grupo → 200 y sin filas (VR §4) | ✅ |
| Sin responsive ni paginación | AGENTS §6 | `GET …/messages` devuelve el historial completo (VR §4) | ✅ |
| Layout de dos paneles y estilo similar a encodelabs | `docs/plan/ui-style.md`, D-067 | Tokens reales de encodelabs (Red Hat Display, marca `oklch(0.499 0.082 209)`) en `frontend/src/styles.css` | ⏳ revisión visual humana |
| `.env` fuera del repositorio | AGENTS §7.1 | `.gitignore`; búsqueda de secretos sobre `git ls-files` limpia en cada commit | ✅ |
| Tests unitarios sobre el dominio | D-095 | 69 tests de dominio (xUnit) en verde; 33 tests de reglas del cliente (Vitest), justificados en el README | ✅ (ver §3, desvío 7) |
| Supuestos en el README | — | Cada decisión marcada "Supuesto" en el registro está en README §5 (contrastado uno por uno) | ✅ |

### 1.4 Entregables (§5)

| Entregable | Ubicación | Estado |
|---|---|---|
| `docker-compose.yml`, `.env.example` | raíz | ✅ |
| README con las 5 secciones pedidas | `README.md` §1–§5 | ✅ |
| C4 nivel 1 y 2 (draw.io + SVG) | `docs/c4/` | ✅ (contrastados con el compose y el código en el Step #9) |
| ADR-001, ADR-002, ADR-003 con la plantilla y ≤ 1 página | `docs/adr/` | ✅ (más ADR-004 y ADR-005, opcionales) |
| Trabajo con IA (1 página) | `docs/ai/trabajo-con-ia.md` | ✅ (Step #11) |
| Preguntas bonus | `docs/preguntas-bonus.md`, enlazado desde el README | ✅ (Step #11) |
| Repositorio público | — | Step #12, con autorización del humano |

## 2. Plan vs. construido: cada decisión

Todas las decisiones de `docs/plan/decisions-log.md` se contrastaron con el código o con el proceso. Las de proceso (P) se verifican con el registro de prompts y el historial de git.

| Decisiones | Estado | Evidencia o nota |
|---|---|---|
| D-001, D-002 | ✅ P | Docs en español y código en inglés; un checkpoint por paso hasta el #10 (ver §3, desvío 10) |
| D-003 | ⏳ | Credenciales reales cargadas y webhook registrado; falta la prueba en vivo |
| D-004 | ✅ | PostgreSQL 18.6 + Npgsql 10.0.3 |
| D-005, D-006 | ✅ | .NET 10.0.14 y Angular 22.2; todo se compila en contenedores (AGENTS §3) |
| D-007, D-008, D-009 | ✅ | 5 proyectos + tests; handlers simples; RabbitMQ.Client 7.2.2 sin MassTransit |
| D-010, D-011 | ✅ | Servicio `worker` aparte; `inbox.events` → `InboxEventsSubscriber` → SignalR |
| D-012 | ✅ | 401/503/200 en `TelegramWebhookEndpoint.cs` (VR §6) |
| D-013 | ✅ | `Contracts/IncomingTextMessage.cs` |
| D-014 | ✅ con ajuste | Quorum + DLX + `x-delivery-limit 5`; el límite quedó como respaldo (D-093) |
| D-015 | ✅ | `prefetch 1`, ack tras el commit, nueva notificación ante un duplicado (VR §6) |
| D-016 | ✅ | `occurred_at`, `seq` (`InboxQueries.cs:34`). Su estado decía "reemplazada"; se corrigió (§4) |
| D-017, D-018, D-019, D-020 | ✅ | SignalR; watchdog (`WebhookRegistrationService.cs:38`); secreto desde `.env`; ngrok solo a `api:8081` |
| D-021, D-022, D-023, D-024 | ✅ | `QueueReply` + `DeliverReply`; conteo en SQL; `ReadAt = queuedAt` (`Message.cs:77`); `MarkConversationReadUpTo` |
| D-025, D-026, D-027, D-028 | ✅ Supuestos | En el README §5 y en el código (`TelegramUpdateMapper.cs:12`, `Contact.UpdateProfile`) |
| D-029 | ✅ | Barredor cada 30 s para `Pending` de más de 60 s, por `last_queued_at` (`Worker/Program.cs:11-12`, `Repositories.cs:59`) |
| D-030 | ✅ | At-least-once, registrado como riesgo en ADR-004 |
| D-031, D-032, D-033 | ✅ | `MarkFailed` + ack; `POST /api/messages/{id}/retry`; `inbox.dlx` con una DLQ por flujo |
| D-034 | ✅ | ADR-004 |
| D-035 | ✅ riesgo aceptado | Envío serial con disparador de revisión (ADR-004) |
| D-036 | ✅ | `requirements.md` está en `.gitignore` y no figura en ningún commit |
| D-037, D-039, D-040, D-045, D-046, D-047, D-048 | ✅ P | Valores de unión respetados (los builds encajaron sin cambios en O-4); propiedad de archivos verificada en cada gate con `git status --porcelain --untracked-files=all` |
| D-038 | ✅ | `curl` en la imagen de la api; el worker sin healthcheck (`docker-compose.yml:112`) |
| D-041 | ✅ | La api no depende de ngrok (`docker-compose.yml:85`) |
| D-042, D-043, D-044 | ✅ | `secret_token` en `setWebhook`; contracts revisión 5 con §7; `Connection.LocalPort` |
| D-049, D-050, D-051 | ✅ | `seq` en `MessageDto`; `mandatory: true`; tope de 10 s para `retry_after` (`DeliverReply.cs:24`) |
| D-052, D-053, D-054 | ✅ Supuestos | README §5 (D-054 se actualizó, ver §4) |
| D-055 | ✅ | No se agregó `version` a los DTOs |
| D-056 | ⚠️ | Desvío 1 |
| D-057, D-058, D-060 | ✅ | Variables por servicio; `:?` solo para la base y el broker; volumen de Postgres 18 y `hostname` fijo |
| D-059 | ⚠️ | Desvío 2 |
| D-061 | ✅ | README, solución de problemas |
| D-062 | ✅ | Zoneless, sin router ni forms, Vitest |
| D-063, D-064, D-065, D-066, D-067 | ✅ | `realtime.service.ts:57-93`; `inbox.reducers.ts:15`; `inbox.store.ts:185-200`; borradores por contacto; tokens de marca claros |
| D-068 | ⚠️ | Desvío 3 |
| D-069, D-070, D-071 | ✅ | Política de reintentos en `DeliverReply`; `RemoveAllLoggers` (`DependencyInjection.cs:64`); `RecordRequeued` |
| D-072 | ⚠️ | Desvío 4 |
| D-073, D-074 | ✅ | `PoisonMessageException` → DLQ; SHA-256 + `FixedTimeEquals` |
| D-075 | ⚠️ | Desvío 5 |
| D-076 | 🔁 sobre ADR-003 original | Reintento sin límite para errores transitorios (`PersistenceRetry.cs:15`, tope 30 s); ADR-003 actualizado |
| D-077 | ✅ ampliada por D-087 | Tras el resultado de Telegram nunca se reenvía (`PersistenceRetry.cs:35`) |
| D-078, D-079, D-080, D-081, D-082 | ✅ | Re-emisión (`DeliverReply.cs:38`); el 202 no degrada el estado; Supuestos en el README; `GSS Encryption Mode=Disable` |
| D-083 | ✅ mecanismo cambiado por D-094 | `WorkQueueConsumer.HandleBasicCancelAsync`, `QueueConsumer.IsChannelLevelClose` (VR §7: 7 ciclos de `basic.cancel`, nunca 0 ni 2 consumidores) |
| D-084, D-085, D-086 | ✅ | Solo `DbException{IsTransient}` (`TransientErrors.cs:16`); `MarkConversationReadUpTo` se queda en `Contact`; detalles aceptados |
| D-087, D-088, D-089, D-090 | ✅ | Sin reenvío tras el resultado; cancelación solo ante pérdida de suscripción; `TransientPersistenceException` en Application; sin diario en disco (ADR-005) |
| D-091, D-092 | ✅ | Menores de R-3 cerrados; D-092 confirmado por inyección de fallas (VR §7) |
| D-093, D-094 | ✅ | DLQ tras 3 reintentos (`QueueConsumer.cs:202-215`); clasificación por `ShutdownEventArgs` |
| D-095 | ⚠️ | Desvío 7 |
| D-096 | ⚠️ P | Desvío 8 |
| D-097, D-098 | ✅ | Aceptado; README y ADR-003 actualizados |

**Tareas** (`docs/plan/tasks.md`): B-1..B-10, F-1..F-6, P-1..P-5, Q-1..Q-5, R-1, W-1, W-2, O-1..O-4 completas. O-5 (este documento), O-6, O-7 y W-3 se completaron en los Steps #10 y #11; queda R-2 (Step #12).

## 3. Desvíos respecto del plan

| # | Desvío | Quién lo reportó | Motivo | Resolución |
|---|---|---|---|---|
| 1 | Imágenes con *minor* fija (`postgres:18.6`, `rabbitmq:4.3`, `nginx:1.30`) en lugar de la *major* que listaba AGENTS §3 | A4 | AGENTS delegaba en A4 fijar la *minor* tras verificarla | Aceptado (D-056) |
| 2 | El puerto del frontend se publica solo en `127.0.0.1` (contracts §1 no lo especificaba) | A4 | Coherente con D-028 y la falta de autenticación | Aceptado en G1 (D-059) |
| 3 | Paquetes `Microsoft.Extensions.*` fuera de la lista de AGENTS §4 | A2 | El worker corre sobre `runtime:10.0`, sin ASP.NET | Aceptado (D-068); son de Microsoft y sin licencia comercial |
| 4 | Espera del confirm con `.WaitAsync(5 s)` en vez de `CancellationToken` | A2 | Medido: la librería ignora la cancelación con el broker colgado (25 s → 5 s) | Aceptado (D-072); el contrato no cambió |
| 5 | Imágenes base descargadas desde `mirror.gcr.io` durante el desarrollo | A0 | Límite de Docker Hub | Solo afecta al entorno de desarrollo; el compose usa los nombres oficiales (D-075) |
| 6 | B-7 decía "400/403 permanentes"; se implementó "todo 4xx salvo 429" | A1 en el Step #7 | Clasifica toda respuesta, incluido 401/404 por un token inválido | Aceptado (D-051) |
| 7 | Tests del frontend además de los de dominio (AGENTS §5 y requirements §6 piden tests de dominio) | A5 (R-5) | Prueban reglas del cliente: orden, precedencia de estados, upserts idempotentes | Se mantienen y se justifican en el README (D-095) |
| 8 | El orquestador editó un archivo de A7 (README, sección Tests) | A0 | Un párrafo, con A7 ya terminado | Registrado (D-096) |
| 9 | Un cuarto ciclo de corrección (R-4), cuando el plan escala al humano en el tercero | A0 | Defectos encontrados por QA | El humano lo aprobó explícitamente (P-027) |
| 10 | Los Steps #11 y #12 corren sin checkpoint, con el gate G3 abierto (falta la prueba en vivo) | Humano (P-031) | Tiempo disponible | Se retoma la prueba en vivo después; cualquier hallazgo vuelve como corrección (D-100) |
| 11 | La evidencia de R-4 se obtuvo con RabbitMQ 4.4.0 (fijado: 4.3) | A2 | El stack descartable usó otra imagen | El arreglo no depende del contador de entregas; el stack live (4.3) se reconstruyó con R-4: sano, 1 consumidor por cola, 0 errores |
| 12 | No hay *linter* configurado | A5 | No estaba en el plan | El control de estilo es `TreatWarningsAsErrors` + `strict` en TypeScript; no se agrega |

**Tropiezos de proceso** (registrados en `prompt-log.md`, ninguno filtró secretos): A4 ejecutó `docker compose config` sin `-q` (con valores de ejemplo); el orquestador anotó "33/33" antes de ver el resumen y lo corrigió; una primera búsqueda de secretos fue inválida por un alias de `grep` y se repitió.

## 4. Referencias colgantes y correcciones de este paso

Chequeo automático sobre los 18 `.md` versionados: enlaces relativos, rutas citadas entre comillas invertidas e identificadores D-xxx, P-xxx y ADR-xxx.

- **D-xxx, P-xxx, ADR-xxx:** todos existen. No quedan marcas *(pending: …)* en el registro de decisiones.
- **Rutas faltantes:** solo `docs/qa/traceability.md` (este archivo), `docs/ai/trabajo-con-ia.md` y `docs/handout.md`, que se crearon en los Steps #10 y #11. Re-chequeo al cerrar el Step #11: 22 archivos, 0 referencias colgantes.
- **Correcciones:**
  - D-016 tenía el estado "🔁 reemplazada por D-021", pero su fila ya describe el orden vigente. Pasa a ✅ (revisada por D-021).
  - D-054 y README §5 decían que Angular 23 podía salir en días. Angular pasó a un ciclo anual y la versión 23 está prevista para junio de 2027; .NET 11 sigue en RC, con GA prevista para el 10/11.

## 5. Pendiente para cerrar G3

La prueba en vivo con `docs/qa/manual-e2e-checklist.md`: un `sendMessage` exitoso, bloqueo del bot → `Failed` → desbloqueo → Reintentar, y la revisión visual de la UI. Es lo único de §3.1 que no tiene evidencia directa.
