# ADR-002: Comunicación en tiempo real — SignalR

**Estado:** Aceptado

## Contexto

El operador tiene que ver en el momento los mensajes nuevos, los cambios de no leídos y el estado de entrega de sus respuestas (pendiente, enviada o fallida). El tráfico es casi todo **servidor → cliente**: las acciones del operador (responder, marcar leído, reintentar) ya viajan por la API HTTP.

El **worker** genera la mayoría de los eventos, pero solo la **api** tiene conexión con los navegadores. Hace falta reconexión automática (en Docker los reinicios son comunes), no perder nada durante un corte y usar tecnología oficial en .NET 10 y Angular 22.

## Alternativas consideradas

| Opción | Ventajas | Desventajas |
|---|---|---|
| **SignalR** | Es oficial en ambos extremos (ASP.NET Core 10 y `@microsoft/signalr`). Reconexión automática, heartbeats y *fallback* de transporte (WebSocket → SSE → long polling). Mensajes con nombre y payload tipado, y difusión a todos los clientes. | Usa un protocolo propio, con dependencia JS de ~50 KB y sin clientes WebSocket "puros". nginx necesita los encabezados `Upgrade`. |
| **Server-Sent Events** (`TypedResults.ServerSentEvents`, .NET 10) | Encaja con el flujo unidireccional. Es HTTP simple y `EventSource` reconecta solo. | Hay que escribir el *broadcaster* a mano: un canal por conexión, limpieza y difusión. Requiere desactivar el *buffering* de nginx. Es más difícil sumar encabezados o autenticación después. |
| **WebSockets puros** | Control total y sin librerías. | Hay que construir framing, heartbeats, reconexión y serialización: mucho código y muchos bugs posibles. |
| **Polling (2–5 s)** | Trivial y sin estado. | No es tiempo real, desperdicia requests y no cumple el espíritu del requisito 3.1.1. |

## Decisión

Se usa **SignalR**, con un hub `/hubs/inbox` **solo servidor → cliente** y cuatro eventos (`messageReceived`, `messageQueued`, `messageStatusChanged`, `conversationRead`). Cada uno lleva el mensaje o contacto completo, no un delta.

Para llegar a los navegadores sin conocer SignalR, el worker publica en el exchange *fanout* `inbox.events`. Cada instancia de la api lo consume con una cola exclusiva y lo reenvía al hub. El puerto `IInboxNotifier` tiene dos adaptadores: SignalR en la api y RabbitMQ en el worker.

**Reglas del cliente:** *upsert* por id, así que un duplicado no tiene efecto; en `onreconnected` vuelve a pedir el listado y la conversación abierta, lo que cubre cualquier hueco. SSE queda como segunda opción.

## Consecuencias

- **Ganamos:** reconexión y *fallback* sin código propio; el hub no conoce RabbitMQ y el worker no conoce SignalR (puertos y adaptadores). Los eventos que origina la api (encolado, lectura) se emiten localmente: con varias réplicas habría que enrutarlos también por `inbox.events` o sumar un *backplane*. Hoy se asume una sola réplica (Supuestos).
- **Resignamos:** un protocolo estándar abierto (SignalR solo se consume bien con su cliente) y un salto extra (worker → RabbitMQ → api → navegador), de pocos milisegundos.
- **Riesgos:** los eventos son **transitorios**: si la api está caída o pierde un momento su conexión con RabbitMQ, se pierden. Es aceptable porque la fuente de verdad es la base y el cliente resincroniza al reconectar o recargar. nginx debe configurarse para WebSocket (`Upgrade`/`Connection`, timeouts largos). Sin autenticación (fuera de alcance), cualquiera que alcance el hub ve los eventos; por eso **no** se expone por ngrok (ADR-003).
