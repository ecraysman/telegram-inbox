# Guía para la evaluación

Es el primer documento a leer, antes del código. No repite el [README](../README.md): apunta a él y a la evidencia.

## 1. Qué es

Una bandeja web donde un único operador recibe, lee y responde en tiempo real los mensajes de texto que los usuarios envían a un bot de Telegram. Backend .NET 10 (Clean Architecture), frontend Angular 22, PostgreSQL, RabbitMQ y SignalR, todo en Docker Compose (6 servicios: postgres, rabbitmq, ngrok, api, worker, frontend).

Flujo: Telegram → ngrok → api (valida el secreto) → RabbitMQ → worker → PostgreSQL → SignalR → UI. La respuesta del operador va en cola: la api la guarda como pendiente y la publica, y el worker la envía a Telegram y registra el resultado.

## 2. Verlo funcionando en 5 minutos

Detalle en [README §2 (.env)](../README.md#2-cómo-completar-el-env), [§3 (levantar)](../README.md#3-cómo-levantar-la-solución) y [§4 (probar)](../README.md#4-cómo-probarla-de-punta-a-punta).

```bash
cp .env.example .env            # completar TELEGRAM_BOT_TOKEN, NGROK_AUTHTOKEN, TELEGRAM_WEBHOOK_SECRET, claves locales
docker compose up -d --build
docker compose ps               # postgres, rabbitmq, api y frontend en healthy
# abrir http://localhost:4200 y escribirle al bot desde Telegram
```

Guía paso a paso (incluye la verificación de la URL pública): [docs/qa/manual-e2e-checklist.md](qa/manual-e2e-checklist.md). Sin bot real, un token con formato válido pero falso (`123:abc`) permite ver la UI (README §2).

## 3. Recorrido por criterio de evaluación

**Visión de negocio**
- Alcance, supuestos y fuera de alcance: [README, Supuestos](../README.md#5-supuestos) (12 supuestos, cada uno con su D-xxx).
- Priorización y trazabilidad requisito → evidencia: [docs/qa/traceability.md](qa/traceability.md).
- Registro de todas las decisiones, con alternativas: [docs/plan/decisions-log.md](plan/decisions-log.md).

**Arquitectura**
- Diagramas C4 (contexto y contenedores): [docs/c4/](c4/README.md); contrastarlos con [docker-compose.yml](../docker-compose.yml).
- Regla de dependencias por referencias de proyecto: `backend/src/Inbox.Domain`, `Inbox.Application`, `Inbox.Infrastructure`, `Inbox.Api`, `Inbox.Worker`.
- Casos de uso (un handler por caso) en `backend/src/Inbox.Application/UseCases/`; puertos en `backend/src/Inbox.Application/Abstractions/`.
- Consumidores y barredor del outbox, finos: `backend/src/Inbox.Worker/Consumers/`, `backend/src/Inbox.Worker/Outbox/OutboxSweeperService.cs`.

**Toma de decisiones**
- 5 ADR: [docs/adr/](adr/) (base de datos, tiempo real, webhook y colas, envío asíncrono, tolerancia a fallas de la base).
- Contratos congelados (HTTP, SignalR, RabbitMQ, DB, env): [docs/plan/contracts.md](plan/contracts.md).
- Los cambios de rumbo y sus motivos están en el registro de decisiones (ej. D-076, D-084, D-087).

**Trabajo con IA**
- Documento de síntesis: [docs/ai/trabajo-con-ia.md](ai/trabajo-con-ia.md).
- Agentes, roles y propiedad de archivos: [docs/ai/agents.md](ai/agents.md); reglas comunes: [AGENTS.md](../AGENTS.md).
- Prompts reales por rol y ronda de corrección: [docs/ai/prompts/](ai/prompts/); bitácora: [docs/ai/prompt-log.md](ai/prompt-log.md).
- Criterio al validar: QA independiente ([verification-report](qa/verification-report.md), con los dos defectos que encontró y que luego se corrigieron) y rondas de re-revisión `A6-*` en los prompts.

**Calidad del código**
- Reglas del dominio en las entidades, setters privados y `DomainException`: `backend/src/Inbox.Domain/Messages/Message.cs`, `backend/src/Inbox.Domain/Contacts/Contact.cs`, `backend/src/Inbox.Domain/Messages/MessageText.cs`.
- Tests unitarios del dominio (69): `backend/tests/Inbox.Domain.Tests/`.
- Clasificación de errores y reintentos: `backend/src/Inbox.Infrastructure/Messaging/ConsumerErrorClassifier.cs`, `backend/src/Inbox.Application/UseCases/PersistenceRetry.cs`.
- Frontend: store con signals `frontend/src/app/state/inbox.store.ts`, reglas puras en `inbox.reducers.ts`, acceso a red solo en `frontend/src/app/core/` (33 tests Vitest, ver D-095).

**Funcionamiento e interfaz**
- Componentes de UI: `frontend/src/app/features/contacts/`, `frontend/src/app/features/conversation/`; tokens de estilo en [docs/plan/ui-style.md](plan/ui-style.md).
- Evidencia de funcionamiento (simulada y live, con lo no verificado marcado): [docs/qa/verification-report.md](qa/verification-report.md).
- Prueba propia: checklist del punto 2.

## 4. Decisiones que van más allá de lo pedido

| Decisión | Por qué | Referencia |
|---|---|---|
| Respuestas en cola, con la fila del mensaje como outbox y barredor | Sobreviven a caídas de Telegram y del broker; el estado (pendiente, enviado, fallido) es visible | D-021, D-029, [ADR-004](adr/ADR-004-envio-asincrono-respuestas.md) |
| Worker separado de la api | La recepción depende solo del broker; el procesamiento, solo de la base | D-010, [ADR-003](adr/ADR-003-webhook-y-colas.md) |
| Aislamiento por puerto del webhook (ngrok solo llega al 8081) | No hay autenticación; la API REST y el hub no deben ser públicos | D-020, D-044 |
| Reintento sin límite ante errores transitorios de infraestructura | Un corte de la base no debe perder mensajes ya confirmados a Telegram; la DLQ queda solo para mensajes venenosos | D-076, D-084, [ADR-005](adr/ADR-005-tolerancia-fallas-base-de-datos.md) |
| Nunca reenviar tras un resultado final de Telegram | Un duplicado al cliente es peor que reintentar solo el guardado | D-077, D-087 |
| Reintento manual de respuestas fallidas desde la UI | `Failed` no debe ser un callejón sin salida | D-032 |
| Registro y auto-reparación del webhook (watchdog de 60 s); la api no depende de ngrok | Diseñado para re-registrarse si cambia la URL (verificado solo el registro inicial, ver §5); la bandeja funciona sin túnel | D-018, D-041, D-042 |

## 5. Límites conocidos y riesgos aceptados

- **Envío saliente serial** (prefetch 1, un consumidor): durante una caída de Telegram, cada respuesta espera los reintentos de la anterior (~7 s en 5xx o red; hasta ~30 s con 429). D-035.
- **Entrega al menos una vez a Telegram:** una caída entre el envío y el guardado puede duplicar una respuesta; Telegram no ofrece idempotencia. D-030, ADR-004.
- **Un solo operador**, lectura global, api en una réplica. D-027.
- **Prueba con Telegram real pendiente al momento de escribir esto (D-100):** el envío exitoso (tilde), la recepción real y la UI en el navegador no los verificó QA; los cubre el checklist manual.
- **Re-registro del webhook al cambiar la URL de ngrok:** no verificado en vivo (solo el registro inicial).
- **429 y 5xx de Telegram:** verificados únicamente leyendo el código (`TelegramGateway`, `DeliverReply`); no se pudieron provocar.
- **Defecto QA-D1 (RabbitMQ 4.3 no cuenta las reentregas por `nack` con requeue):** corregido en D-093 (tras 3 reintentos en proceso, `nack` sin requeue hacia la DLQ). La corrección se verificó en un stack descartable (el mensaje llega a la DLQ a los ~7 s, una vez) y pasó una nueva revisión de código; ver el anexo del [informe de QA](qa/verification-report.md).
- **Tests del frontend:** el enunciado pide tests unitarios del dominio; los 33 del cliente son unitarios sobre reglas de estado y se justifican en D-095.
- **Sin linter configurado:** el único control estático es `TreatWarningsAsErrors`.
- **El consumidor de RabbitMQ es la pieza más compleja** (`backend/src/Inbox.Infrastructure/Messaging/QueueConsumer.cs`): reintentos por clase de error, re-suscripción ante `consumer_timeout` y una generación por canal para no confirmar entregas de un canal ya cerrado. Existe para no perder ni duplicar mensajes ante caídas y reconexiones (D-083, D-088, D-093, D-094). En producción convendría evaluar una librería madura que lo resuelva, o separar la política de reintentos en su propia clase.

## 6. Mapa de la documentación

| Ruta | Qué es |
|---|---|
| [README.md](../README.md) | Instalación, `.env`, supuestos, arquitectura, tests, solución de problemas |
| [docs/adr/](adr/) | 5 decisiones de arquitectura con alternativas y consecuencias |
| [docs/c4/](c4/README.md) | Diagramas C4 nivel 1 y 2 (editables y SVG) |
| [docs/plan/contracts.md](plan/contracts.md) | Interfaces congeladas: HTTP, SignalR, RabbitMQ, DB, entorno |
| [docs/plan/decisions-log.md](plan/decisions-log.md) | Registro de decisiones D-xxx |
| [docs/plan/plan.md](plan/plan.md), [tasks.md](plan/tasks.md), [architecture.md](plan/architecture.md), [ui-style.md](plan/ui-style.md) | Plan de trabajo, tareas, arquitectura detallada, estilo visual |
| [docs/qa/traceability.md](qa/traceability.md) | Requisito → implementación → evidencia |
| [docs/qa/verification-report.md](qa/verification-report.md) | Informe de QA independiente, con defectos y no verificados |
| [docs/qa/manual-e2e-checklist.md](qa/manual-e2e-checklist.md) | Prueba manual de punta a punta |
| [docs/ai/trabajo-con-ia.md](ai/trabajo-con-ia.md) | Cómo se usó la IA (síntesis) |
| [docs/preguntas-bonus.md](preguntas-bonus.md) | Respuestas a las preguntas bonus (caché, multimedia) |
| [docs/ai/agents.md](ai/agents.md), [prompt-log.md](ai/prompt-log.md), [prompts/](ai/prompts/) | Agentes, bitácora y prompts |
| [AGENTS.md](../AGENTS.md) | Guía y reglas para humanos y agentes |
