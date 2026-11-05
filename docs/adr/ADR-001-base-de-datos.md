# ADR-001: Base de datos — PostgreSQL

**Estado:** Aceptado

## Contexto

La bandeja necesita persistir **contactos** (usuarios de Telegram) y sus **mensajes** (entrantes y salientes). Las consultas que más se usan son:

- Listado de contactos con **cantidad de no leídos** y **vista previa del último mensaje**.
- Historial de una conversación ordenado por fecha.
- Marcar como leídos todos los mensajes de un contacto.

Además, Telegram **reintenta** el webhook ante fallos, así que el alta de mensajes tiene que ser **idempotente** (clave única sobre `update_id`). Los mensajes contienen emojis, por lo que se requiere UTF-8 completo. El backend usa la última versión estable de .NET con EF Core. La solución tiene que levantar con `docker compose up`.

## Alternativas consideradas

| Opción | Ventajas | Desventajas |
|---|---|---|
| **PostgreSQL** | El proveedor EF Core (Npgsql) acompaña cada versión mayor de EF Core. Ofrece `ON CONFLICT DO NOTHING` para idempotencia e índices parciales (solo no leídos). `timestamptz` guarda instantes reales en UTC y la codificación es UTF-8 por defecto. La imagen Alpine es liviana. | Algo menos difundido que MySQL en algunos equipos. Su operación avanzada (vacuum, extensiones) no aporta valor a esta escala. |
| **MariaDB / MySQL** | Muy difundido y simple para un esquema de dos o tres tablas. MariaDB es 100 % GPL. | El proveedor EF Core comunitario (Pomelo) históricamente demora meses en soportar cada versión mayor de EF Core, y el oficial de Oracle es menos maduro: hay riesgo de fricción con la última versión de .NET. Hay que garantizar `utf8mb4` para no romper emojis. No tiene índices parciales. `INSERT IGNORE` también oculta otros errores. `DATETIME` no tiene zona horaria. |
| **MongoDB** (NoSQL) | Una conversación es un documento natural y el esquema es flexible. | Los no leídos y el último mensaje por contacto requieren agregaciones o desnormalización mantenida a mano. Ofrece menos garantías relacionales y el soporte EF Core es más limitado. |

## Decisión

Se elige **PostgreSQL** (imagen oficial `postgres` Alpine), accedido con **EF Core + Npgsql**. El modelo es relacional (contacto 1-N mensajes), las consultas clave son agregaciones simples y el factor decisivo es contar con un proveedor EF Core alineado con la última versión de .NET. La idempotencia se resuelve con una restricción única sobre el `update_id` de Telegram. Los no leídos se resuelven con un índice parcial (`WHERE read_at IS NULL`).

A esta escala cualquiera de las tres opciones funcionaría: la elección se basa en el ecosistema .NET y en detalles de corrección (idempotencia, zona horaria, UTF-8), no en rendimiento.

## Consecuencias

- **Ganamos:** idempotencia declarativa, consultas de no leídos baratas, fechas sin ambigüedad y un camino sin fricción con la última versión de EF Core.
- **Resignamos:** portabilidad directa a otro motor. El SQL específico de PostgreSQL (`ON CONFLICT`, índice parcial) queda **encapsulado en la capa Infrastructure**, detrás de interfaces de repositorio. Cambiar de motor solo afecta esa capa.
- **Riesgos:** Npgsql exige `DateTime` en UTC para `timestamptz`, así que el dominio debe trabajar siempre en UTC. Las migraciones se aplican al iniciar la API: es aceptable para la demo, pero en producción deberían ejecutarse como un paso separado del despliegue. Los datos persisten en un volumen de Docker; `docker compose down -v` los borra.
