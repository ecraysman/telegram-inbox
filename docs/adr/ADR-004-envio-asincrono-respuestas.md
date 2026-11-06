# ADR-004: Envío asíncrono de respuestas

**Estado:** Aceptado

## Contexto

El operador responde desde la bandeja y la respuesta tiene que llegar al usuario en Telegram (3.1.5). Telegram puede tardar, limitar (429), rechazar (403: el usuario bloqueó el bot) o estar caído, y `sendMessage` **no tiene clave de idempotencia**. Queremos que **ninguna respuesta se pierda en silencio** y que el operador vea siempre el estado de cada una.

## Alternativas consideradas

| Opción | Ventajas | Desventajas |
|---|---|---|
| **Síncrona** (enviar y luego guardar) | Simple, con feedback inmediato. | Con Telegram caído la respuesta falla. Si falla el guardado, el mensaje llega pero no se ve. |
| **Asíncrona con cola** (elegida) | Sobrevive a caídas de Telegram, con reintentos y estado visible. | Máquina de estados, estados en la UI y política de reintentos. |
| Tabla *outbox* separada | Patrón de libro para guardar y publicar de forma atómica. | Duplica estado: el mensaje ya es un registro persistente. |

## Decisión

Respuesta **asíncrona por RabbitMQ**, con la **fila del mensaje como outbox**:

1. **api:** valida el texto, guarda el mensaje en estado **`Pending`**, publica `OutgoingTextMessage v1` y responde **202**.
2. **worker:** si el mensaje ya no está `Pending`, solo re-emite su estado. Si sigue `Pending`, llama a `sendMessage`:
   - Éxito → **`Sent`**.
   - 429 o 5xx/red → 3 reintentos (`retry_after` con tope de 10 s), y luego **`Failed`**.
   - Otro 4xx → **`Failed`** sin reintento.
3. Con el resultado final se reintenta **solo el guardado**, nunca el envío. Un `Failed` se muestra al operador; la DLQ queda para mensajes venenosos.
4. **Barredor** (cada 30 s): re-publica los `Pending` viejos. Cubre el caso "guardado pero nunca publicado".
5. **Reintento manual:** `POST /api/messages/{id}/retry` pasa `Failed → Pending` (botón "Reintentar").

Las transiciones son **reglas del dominio**, cubiertas por tests unitarios.

## Consecuencias

- **Ganamos:** ninguna respuesta se pierde en silencio, las caídas de Telegram se absorben y el operador ve el estado (reloj, ✓, ⚠ + "Reintentar").
- **Resignamos:** simplicidad: estados en el dominio y en la UI, un barredor y un endpoint extra.
- **Riesgo aceptado: *at-least-once*.** Puede haber un duplicado si el worker se cae antes de guardar `Sent`, o si Telegram aceptó el mensaje pero su respuesta no llegó (timeout). La alternativa *at-most-once* convierte esos casos en pérdidas, y en atención al cliente un duplicado raro es preferible.
- **Riesgo aceptado: bloqueo ante una falla persistente al guardar.** Si la base no deja guardar `Sent`, el worker reintenta indefinidamente sin reenviar, salvo que el broker devuelva el mensaje por `consumer_timeout` (una vez cada 30 min). Las salientes esperan hasta que se corrige la base; los entrantes siguen funcionando. Preferimos una demora visible (logs críticos, cola creciente, reloj en la UI) a un duplicado. **Evolución:** un estado `Sending` previo al envío, que el barredor ignora y que, si queda colgado, pasa a `Failed` ("estado desconocido") para que decida el operador.
- **Riesgo aceptado: envío serial.** Un solo consumidor mantiene el orden, pero con Telegram caído cada respuesta espera los reintentos de la anterior (unos 7 s; hasta 30 s ante 429). Es aceptable hasta llegar a **cientos de conversaciones activas con Telegram degradado**. Entonces se particiona la cola por contacto o se usan colas de reintento con demora.
