# ADR-003: Flujo de recepción del webhook y topología de colas en RabbitMQ

**Estado:** Aceptado

## Contexto

Telegram entrega cada mensaje con un `POST` al webhook y **reintenta** si no recibe un 2xx. El requisito exige RabbitMQ para procesar los entrantes. Necesitamos:
- No perder mensajes aunque se caiga la base o el broker.
- No duplicarlos ante reintentos (*at-least-once*).
- Que un mensaje "venenoso" no bloquee la cola.
- No exponer a internet la API de la bandeja, que no tiene autenticación.

## Alternativas consideradas

| Tema | Descartada: por qué | **Elegida**: ventaja / costo |
|---|---|---|
| Procesos | Un único proceso: más simple, pero el desacople queda solo en el código | **api + worker**: la recepción depende solo del broker y el procesamiento solo de la base / un contenedor más |
| Respuesta del webhook | Siempre 200: si el broker cae, el mensaje se pierde | **200 tras el *publisher confirm*, 503 si falla**: Telegram reintenta hasta 24 h / ~ms de latencia por confirm |
| Reintentos | Colas de reintento con TTL y `x-death`: más piezas | **En proceso + cola *quorum* con DLQ**: poco código / el consumidor queda ocupado mientras reintenta |
| Contrato en cola | El `Update` crudo: acopla el worker a Telegram | **`IncomingTextMessage v1`** (anticorrupción) / un mapeo más |
| Librería | MassTransit: v9 es comercial y oculta la topología | **RabbitMQ.Client 7**: topología explícita / ciclo de vida del consumidor escrito a mano |

## Decisión

**Flujo:**
1. Telegram llama a `POST /telegram/webhook` en el puerto **8081**, el único que ngrok expone. El aislamiento se valida por el puerto local de la conexión, no por el encabezado Host.
2. Se valida el `secret_token` en tiempo constante (**401** si no coincide). Lo que no sea texto de chat privado se registra y se responde **200** sin procesar.
3. Se publica `IncomingTextMessage v1` persistente, `mandatory` y con confirmación: **200** tras el confirm, **503** si no llega en 5 s.
4. El worker consume de a uno (`prefetch 1`, *ack* manual), guarda contacto y mensaje en una transacción con `ON CONFLICT (telegram_update_id) DO NOTHING` y, tras el *commit*, notifica `messageReceived` y hace ack (también ante un duplicado).

**Errores en el worker:**
- Venenosos (deserialización, validación): van a la DLQ.
- Transitorios de infraestructura (base o broker caídos, timeouts): reintento **sin límite**, backoff de hasta 30 s; ante un `consumer_timeout` el worker se vuelve a suscribir.
- Otros: 3 reintentos y DLQ. `x-delivery-limit` (5) es solo un respaldo: en RabbitMQ 4.3 un reencolado del consumidor no incrementa el contador.

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
