# ADR-004: Envío asíncrono de respuestas

**Estado:** Aceptado

## Contexto

El operador responde desde la bandeja y la respuesta tiene que llegar al usuario en Telegram (3.1.5). Telegram puede tardar, limitar (429), rechazar (403: el usuario bloqueó el bot) o estar caído. Queremos que **ninguna respuesta se pierda en silencio** y que el operador vea en todo momento en qué estado está cada una. Además, `sendMessage` de Telegram **no tiene clave de idempotencia**.

## Alternativas consideradas

| Opción | Ventajas | Desventajas |
|---|---|---|
| **Síncrona:** la api llama a `sendMessage` y persiste solo si tuvo éxito. | Simple, con feedback inmediato y sin estados intermedios. | Si Telegram está caído, la respuesta falla y el operador reintenta a mano. Si la escritura en base falla después del envío, el mensaje llega pero no se ve. |
| **Asíncrona con cola** (elegida) | Sobrevive a caídas de Telegram, con reintentos automáticos y un estado de entrega visible y auditable. | Requiere una máquina de estados, estados en la UI y política de reintentos. |
| Tabla *outbox* separada + *dispatcher* | Es el patrón de libro para "guardar y publicar" de forma atómica. | Duplica estado: el mensaje ya es un registro persistente. |

## Decisión

Respuesta **asíncrona por RabbitMQ**, con la **fila del mensaje como outbox**:

1. **api:** valida el texto (no vacío y como máximo 4096 caracteres) y guarda el mensaje saliente en estado **`Pending`**. Publica `OutgoingTextMessage v1 {messageId}` con confirmación y responde **202**.
2. **worker** (`inbox.outgoing.telegram`, `prefetch 1`): si el mensaje ya no está `Pending`, hace ack y termina (idempotente). Si sigue `Pending`, llama a `sendMessage`:
   - Éxito → **`Sent`**, guardando el `message_id` de Telegram.
   - 400/403 → **`Failed`** con el motivo.
   - 429/5xx/red → 3 reintentos respetando `retry_after`; si siguen fallando, **`Failed`**.
3. Cada cambio de estado se persiste y se emite (`messageStatusChanged`). Un `Failed` se **acepta (ack) y se muestra**: no queda escondido en la DLQ, que se reserva para mensajes venenosos.
4. **Barredor** del worker (cada 30 s): re-publica los `Pending` con más de 60 s. Esto cubre el caso de "guardado pero nunca publicado", por ejemplo si la api se cae entre el commit y la publicación. Si el broker no confirma, la api igual responde 202: el mensaje está seguro en la base y el barredor lo publicará.
5. **Reintento manual:** `POST /api/messages/{id}/retry` pasa `Failed → Pending`. Es un botón "Reintentar" en la UI.

Las transiciones (`Pending → Sent | Failed`, `Failed → Pending`) son **reglas del dominio**: una transición inválida lanza una excepción y está cubierta por tests unitarios.

## Consecuencias

- **Ganamos:** ninguna respuesta se pierde en silencio y las caídas de Telegram se absorben. El operador ve el estado de cada respuesta (reloj, ✓, ⚠ + "Reintentar"). La respuesta conserva su posición en la conversación (orden por momento de escritura + secuencia).
- **Resignamos:** simplicidad: estados en el dominio y en la UI, un barredor y un endpoint extra.
- **Riesgo aceptado: entrega *at-least-once*.** Si el worker se cae después de que Telegram aceptó el mensaje y antes de guardar `Sent` (milisegundos), la redelivery lo duplica. La alternativa *at-most-once* (estado `Sending`) convierte ese caso en una **pérdida silenciosa**, y en atención al cliente un duplicado raro es preferible.
- **Riesgo aceptado: envío serial.** Un consumidor con `prefetch 1` mantiene el orden por conversación. Pero con Telegram caído, cada respuesta espera hasta unos 7 s de reintentos de la anterior. Es aceptable **hasta llegar a cientos de conversaciones activas *y* con Telegram degradado a la vez**. Entonces se particiona la cola por contacto (*consistent-hash exchange*) o se usan colas de reintento con demora.
