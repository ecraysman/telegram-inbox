# ADR-005: Tolerancia a fallas de la base de datos

**Estado:** Aceptado

## Contexto

PostgreSQL es la fuente de verdad (ADR-001). Se evaluó si hace falta un respaldo en disco para cuando la base falla, empezando por qué pasa hoy en cada flujo:

| Flujo | Con la base caída | ¿Se pierde algo? |
|---|---|---|
| Mensajes entrantes | Esperan en la cola *quorum* de RabbitMQ, que es durable en disco, y se guardan al volver la base (ADR-003) | No: RabbitMQ ya es el buffer en disco |
| Respuesta nueva del operador | La api no puede guardarla como `Pending`: el operador ve un error y conserva el borrador | No, pero no puede enviar |
| Lectura de la bandeja | La UI no puede cargar contactos ni historial | No, pero la bandeja no se puede usar |
| Respuesta ya aceptada por Telegram cuyo `Sent` no se puede guardar | El worker reintenta guardar sin reenviar, y las salientes esperan (ADR-004) | No, pero bloquea las salientes. Si el worker se cae en ese lapso, puede haber un duplicado |

El único dato que existe solo en memoria es el último caso: "Telegram aceptó la respuesta X como mensaje Y".

## Alternativas consideradas

| Opción | Ventajas | Desventajas |
|---|---|---|
| **No agregar respaldo; documentar** (elegida) | No suma estado ni complejidad. RabbitMQ ya cubre los entrantes y ADR-004 evita duplicados | Las salientes se bloquean ante una falla de escritura que no se recupera |
| Journal local en el worker, **JSON Lines** | Desbloquea las salientes y evita duplicados aun si el worker se cae. *Append* de a un registro, legible, un renglón cortado se detecta fácil, sin dependencias | ~150 líneas más tests y un volumen. Casos nuevos: journal corrupto, reconciliación, disco lleno. Supone un solo worker |
| Journal en **CSV** o **Parquet** | CSV es simple; Parquet, compacto para análisis | CSV: el texto tiene comas, comillas y saltos de línea (*escaping* frágil, sin tipos). Parquet: columnar por lotes, no admite *append* seguro de a uno y requiere una librería |
| Cola "resultados de entrega" en RabbitMQ | Reusa un almacenamiento durable existente | El barredor no puede consultar la cola antes de reenviar, así que vuelve el riesgo de duplicado |

## Decisión

**No se implementa un respaldo en disco en esta prueba de concepto.** RabbitMQ ya da la durabilidad de los entrantes; la brecha restante solo aparece si la base rechaza escrituras justo después de un envío exitoso **y** el worker se cae en ese lapso. Si hiciera falta: un **journal JSON Lines** en un volumen del worker, forzado a disco antes de confirmar, consultado antes de cada envío y aplicado por un reconciliador al volver la base.

## Consecuencias

- **Ganamos:** el worker sigue sin estado propio, sin complejidad nueva en la etapa final, y la decisión queda trazada con sus alternativas.
- **Resignamos:** con una falla de escritura persistente, las salientes esperan y la bandeja no se puede usar mientras la base esté caída.
- **Cuándo revisar:** si en la práctica aparecen fallas de escritura persistentes tras envíos exitosos, adoptar el journal JSON Lines, o el estado `Sending` de ADR-004. Para poder ver la bandeja durante una caída, sumar una caché de lectura (ver pregunta bonus sobre caché).
