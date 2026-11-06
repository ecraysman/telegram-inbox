# ADR-003: Flujo de recepción del webhook y topología de colas en RabbitMQ

**Estado:** Aceptado

## Contexto

Telegram entrega cada mensaje con un `POST` al webhook y **reintenta** si no recibe un 2xx. El requisito exige RabbitMQ para procesar los entrantes. Necesitamos:
- No perder mensajes aunque se caiga la base o el broker.
- No duplicarlos ante reintentos (*at-least-once*).
- Que un mensaje "venenoso" no bloquee la cola.
- No exponer a internet la API de la bandeja, que no tiene autenticación.

## Alternativas consideradas

| Tema | Opción descartada | Opción elegida |
|---|---|---|
| Procesos | Un único proceso: más simple, pero el desacople queda solo en el código. | **api (recepción) + worker (procesamiento)**: la recepción depende solo del broker y el procesamiento solo de la base. |
| Respuesta del webhook | Siempre 200: si el broker cae, el mensaje se pierde. | **200 tras el *publisher confirm*, 503 si falla**: Telegram reintenta (guarda los updates hasta 24 h). |
| Reintentos | Colas de reintento con TTL y `x-death`: más piezas. | **Reintento en proceso + cola *quorum* con `x-delivery-limit`**. |
| Contrato en cola | El `Update` crudo: acopla el worker a Telegram. | **`IncomingTextMessage v1` propio** (capa anticorrupción). |
| Librería | MassTransit: v9 es comercial y oculta la topología. | **RabbitMQ.Client 7**: topología explícita en el código. |

## Decisión

**Flujo:**
1. Telegram llama a `POST /telegram/webhook` en el puerto **8081**, el único que ngrok expone. El aislamiento se valida por el puerto local de la conexión, no por el encabezado Host.
2. Se valida el `secret_token` en tiempo constante (**401** si no coincide). Lo que no sea texto de chat privado se registra y se responde **200** sin procesar.
3. Se publica `IncomingTextMessage v1` persistente, `mandatory` y con confirmación: **200** tras el confirm, **503** si no llega en 5 s.
4. El worker consume de a uno (`prefetch 1`, *ack* manual). En una transacción guarda el contacto y hace `INSERT … ON CONFLICT (telegram_update_id) DO NOTHING`. Después del *commit* notifica `messageReceived` y hace ack, también ante un duplicado.

**Errores en el worker:**
- Venenosos (deserialización, validación): van a la DLQ.
- De infraestructura transitorios (base o broker caídos, timeouts): reintento en proceso **sin límite**, con backoff de hasta 30 s. Si el broker cancela al consumidor por `consumer_timeout`, el worker se vuelve a suscribir.
- Otros: 3 reintentos y van a la DLQ. `x-delivery-limit` (5) queda solo como red de seguridad: en RabbitMQ 4.3 un reencolado del consumidor no incrementa el contador de entregas.

**Topología** (declarada de forma idempotente al iniciar):
```
inbox.incoming (direct) ─telegram.text─► inbox.incoming.telegram  [quorum, delivery-limit 5, DLX inbox.dlx / incoming]
inbox.outgoing (direct) ─telegram.send─► inbox.outgoing.telegram  [quorum, delivery-limit 5, DLX inbox.dlx / outgoing]
inbox.dlx      (direct) ─► inbox.incoming.telegram.dlq · inbox.outgoing.telegram.dlq
inbox.events   (fanout) ─► una cola exclusiva por instancia de api (ADR-002)
```

**Registro del webhook:** la api obtiene la URL de ngrok (`:4040/api/tunnels`) y llama a `setWebhook` con el secreto y `allowed_updates=["message"]`. Re-registra si la URL cambia y no bloquea el arranque.

## Consecuencias

- **Ganamos:** durabilidad de punta a punta (Telegram → confirm → commit → ack), idempotencia en dos capas, aislamiento de mensajes venenosos, una DLQ por flujo y una API privada.
- **Resignamos:** simplicidad (un contenedor más y una clasificación de errores). Durante una caída de la base el consumidor queda bloqueado reintentando: los mensajes esperan en la cola sin perderse (ADR-005).
- **Riesgos:** con reintentos, el orden de procesamiento no está garantizado, así que el historial se ordena por fecha + secuencia. El plan gratuito de ngrok admite una sola sesión y la URL puede cambiar al reiniciarlo (lo mitiga el re-registro).
