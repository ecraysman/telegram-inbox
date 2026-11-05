# ADR-003: Flujo de recepción del webhook y topología de colas en RabbitMQ

**Estado:** Aceptado

## Contexto

Telegram entrega cada mensaje con un `POST` al webhook y **reintenta** si no recibe un 2xx. El requisito exige RabbitMQ para procesar los entrantes. Necesitamos:
- No perder mensajes aunque se caiga la base o el broker.
- No duplicarlos ante los reintentos de Telegram o de RabbitMQ (*at-least-once*).
- Que un mensaje "venenoso" no bloquee la cola.
- No exponer a internet la API de la bandeja, que no tiene autenticación por alcance.

## Alternativas consideradas

| Tema | Opción A | Opción B (elegida) |
|---|---|---|
| Procesos | Un único proceso: la api consume su propia cola. Es más simple, pero el desacople queda solo en el código. | **api (recepción) + worker (procesamiento) separados.** La recepción depende solo del broker y el procesamiento solo de la base. Cada uno escala y falla por separado. |
| Respuesta del webhook | Siempre 200. Es simple, pero si el broker cae el mensaje se pierde. | **200 solo tras el *publisher confirm* del broker y 503 si falla**, para que Telegram reintente (según su documentación, guarda los updates no entregados hasta 24 h). |
| Reintentos | Colas de reintento con TTL + DLX y conteo de `x-death`: backoff real, pero más piezas. | **Cola *quorum* con `x-delivery-limit` + reintento corto en proceso**: protección nativa contra mensajes venenosos con poco código. |
| Contrato en cola | El `Update` crudo de Telegram: acopla el worker al esquema de Telegram. | **`IncomingTextMessage v1` propio** (capa anticorrupción). |
| Librería | MassTransit: retries gratis, pero v9 es comercial y oculta la topología. | **RabbitMQ.Client 7**: la topología queda explícita en el código. |

## Decisión

**Flujo:**
1. Telegram llama a `POST /telegram/webhook` en el **puerto 8081** de la api, el único que ngrok expone.
2. La api valida `X-Telegram-Bot-Api-Secret-Token` en tiempo constante; si no coincide, devuelve **401**. El aislamiento del puerto se valida por el puerto local de la conexión, no por el encabezado Host, que detrás de ngrok es el dominio público.
3. Si el update no es un mensaje de texto de chat privado, lo registra y devuelve **200 sin procesar**.
4. Lo traduce a `IncomingTextMessage v1` y lo publica como persistente, `mandatory` y con confirmación (un mensaje no enrutable cuenta como fallo). Responde **200 tras el confirm**, o **503 si el broker no confirma en 5 s**.
5. El worker consume con `prefetch 1` y *ack* manual. En **una transacción** hace *upsert* del contacto e `INSERT … ON CONFLICT (telegram_update_id) DO NOTHING` (ADR-001).
6. Recién después del *commit* publica `messageReceived` en `inbox.events` y hace **ack**. Ante un duplicado, también hace ack y vuelve a notificar.

**Errores en el worker:**
- Los permanentes (deserialización, validación) se rechazan sin reencolar y van a la DLQ.
- Los transitorios (base caída) se reintentan 3 veces en proceso con backoff (1/2/4 s) y luego se reencolan. Cuando la cantidad de entregas supera `x-delivery-limit` (5), van a la DLQ.

**Topología** (la declaran de forma idempotente la api y el worker al iniciar):
```
inbox.incoming (direct) ─telegram.text─► inbox.incoming.telegram  [quorum, delivery-limit 5, DLX inbox.dlx / rk incoming]
inbox.outgoing (direct) ─telegram.send─► inbox.outgoing.telegram  [quorum, delivery-limit 5, DLX inbox.dlx / rk outgoing]  (ADR-004)
inbox.dlx      (direct) ─incoming─► inbox.incoming.telegram.dlq   ─outgoing─► inbox.outgoing.telegram.dlq
inbox.events   (fanout) ─► una cola exclusiva auto-delete por instancia de api (eventos transitorios, ADR-002)
```

**Registro del webhook:** la api toma la URL https de ngrok (`:4040/api/tunnels`) y llama a `setWebhook` con `secret_token`, `allowed_updates=["message"]` y `drop_pending_updates=false`. Re-registra cada 60 s si la URL cambió. No bloquea el arranque.

## Consecuencias

- **Ganamos:** durabilidad de punta a punta (Telegram → confirm → commit → ack), idempotencia en dos capas, aislamiento de mensajes venenosos, una DLQ por flujo y una API privada.
- **Resignamos:** simplicidad (un contenedor más y clasificación de errores). El reintento en proceso bloquea el consumidor unos segundos, aceptable a este volumen.
- **Riesgos:** con reintentos, el orden de procesamiento no está garantizado, así que el historial se ordena por fecha + secuencia. Un 503 sostenido acumula pendientes en Telegram, que se recuperan al volver el broker. El plan gratuito de ngrok admite una sola sesión y la URL puede cambiar al reiniciarlo (lo mitiga el re-registro).
