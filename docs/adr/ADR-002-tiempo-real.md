# ADR-002: Comunicación en tiempo real — SignalR

**Estado:** Aceptado

## Contexto

El operador tiene que ver en el momento los mensajes nuevos, los cambios de no leídos y el estado de entrega de sus respuestas (pendiente, enviada o fallida). El tráfico es casi todo **servidor → cliente**: las acciones del operador (responder, marcar leído, reintentar) ya viajan por la API HTTP.

Hay dos procesos de backend. El **worker** genera la mayoría de los eventos (mensaje recibido, cambio de estado de entrega), pero no tiene conexión con los navegadores. La **api** sí la tiene. Hace falta reconexión automática, porque en una demo con Docker los reinicios son comunes. No debe perderse nada durante un corte, y el stack tiene que ser oficial en .NET 10 y Angular 22.

## Alternativas consideradas

| Opción | Ventajas | Desventajas |
|---|---|---|
| **SignalR** | Es oficial en ambos extremos (ASP.NET Core 10 y `@microsoft/signalr`). Reconexión automática, heartbeats y *fallback* de transporte (WebSocket → SSE → long polling). Mensajes con nombre y payload tipado, y difusión a todos los clientes. | Usa un protocolo propio, con dependencia JS de ~50 KB y sin clientes WebSocket "puros". nginx necesita los encabezados `Upgrade`. |
| **Server-Sent Events** (`TypedResults.ServerSentEvents`, .NET 10) | Encaja con el flujo unidireccional. Es HTTP simple y `EventSource` reconecta solo. | Hay que escribir el *broadcaster* a mano: un canal por conexión, limpieza y difusión. Requiere desactivar el *buffering* de nginx. Es más difícil sumar encabezados o autenticación después. |
| **WebSockets puros** | Control total y sin librerías. | Hay que construir framing, heartbeats, reconexión y serialización: mucho código y muchos bugs posibles. |
| **Polling (2–5 s)** | Trivial y sin estado. | No es tiempo real, desperdicia requests y no cumple el espíritu del requisito 3.1.1. |

## Decisión

Se usa **SignalR**, con un hub `/hubs/inbox` **solo servidor → cliente** y cuatro eventos:
- `messageReceived`
- `messageQueued`
- `messageStatusChanged`
- `conversationRead`

Cada evento lleva el mensaje o contacto completo, no un delta.

Para que el worker llegue a los navegadores sin conocer SignalR, publica eventos en el exchange *fanout* `inbox.events` de RabbitMQ. Cada instancia de la api los consume con una cola exclusiva y los reenvía al hub. El puerto `IInboxNotifier` tiene dos adaptadores: SignalR en la api y RabbitMQ en el worker. La api recibe por el mismo exchange los eventos que genera el worker.

**Reglas del cliente:**
- Hace *upsert* por id, así que un evento duplicado no tiene efecto.
- En `onreconnected` vuelve a pedir el listado y la conversación abierta, lo que cubre cualquier hueco durante el corte.

SSE queda documentado como segunda opción.

## Consecuencias

- **Ganamos:** reconexión y *fallback* sin código propio. El hub no conoce RabbitMQ, y el worker no conoce SignalR (Clean Architecture: puertos y adaptadores). `inbox.events` ya funciona como *backplane*: varias réplicas de la api reciben todos los eventos sin sumar Redis.
- **Resignamos:** un protocolo estándar abierto (SignalR solo se consume bien con su cliente) y un salto extra (worker → RabbitMQ → api → navegador), de pocos milisegundos.
- **Riesgos:** los eventos son **transitorios**: si la api está caída, se pierden. Es aceptable, porque la fuente de verdad es la base y el cliente resincroniza al reconectar. El proxy nginx debe configurarse para WebSocket (`Upgrade`/`Connection`, timeouts largos). Sin autenticación (fuera de alcance), cualquiera que alcance el hub ve los eventos. Por eso el hub **no** se expone por ngrok (ADR-003).
