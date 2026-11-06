# ADR-005: Tolerancia a fallas de la base de datos

**Estado:** Aceptado

## Contexto

PostgreSQL es la fuente de verdad (ADR-001). Se evaluó si hace falta un respaldo en disco ("dump" de mensajes) para cuando la base falla. Primero se analizó qué pasa hoy en cada flujo:

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
| Journal local en el worker, **JSON Lines** | Desbloquea las salientes y evita duplicados aun si el worker se cae. Se escribe un registro a la vez (*append*), es legible y un renglón cortado se detecta fácil. No suma dependencias | Unas 150 líneas más tests y un volumen nuevo. Casos nuevos: journal corrupto, reconciliación, disco lleno. Supone un solo worker |
| Journal en **CSV** | Simple | El texto de los mensajes tiene comas, comillas y saltos de línea, así que el *escaping* es frágil, y no tiene tipos |
| Journal en **Parquet** | Compacto, ideal para análisis | Formato columnar por lotes: no permite agregar registros de a uno de forma segura y requiere una librería. No sirve como log ante caídas |
| Cola "resultados de entrega" en RabbitMQ | Reusa un almacenamiento durable existente | El barredor no puede consultar la cola antes de reenviar, así que vuelve el riesgo de duplicado |

## Decisión

**No se implementa un respaldo en disco en esta prueba de concepto.** La durabilidad de los entrantes ya la da RabbitMQ. La brecha restante es angosta: solo se da si la base acepta lecturas pero rechaza escrituras justo después de un envío exitoso y, además, el worker se cae en ese lapso. Si hiciera falta, la opción es un **journal JSON Lines** en un volumen del worker: se escribe y fuerza a disco antes de confirmar, el envío y el barredor lo consultan antes de enviar, y un reconciliador lo aplica al volver la base.

## Consecuencias

- **Ganamos:** el worker sigue sin estado propio, sin complejidad nueva en la etapa final, y la decisión queda trazada con sus alternativas.
- **Resignamos:** con una falla de escritura persistente, las salientes esperan y la bandeja no se puede usar mientras la base esté caída.
- **Cuándo revisar:** si en la práctica aparecen fallas de escritura persistentes tras envíos exitosos, adoptar el journal JSON Lines, o el estado `Sending` de ADR-004. Para poder ver la bandeja durante una caída, sumar una caché de lectura (ver pregunta bonus sobre caché).
