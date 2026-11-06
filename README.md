# Bandeja conversacional

Prueba técnica encodelabs: una bandeja web donde un operador recibe, visualiza y responde en tiempo real los mensajes de texto que los usuarios envían a un bot de Telegram.

**Resumen del desafío.** La bandeja debe: (1) recibir en tiempo real los mensajes de texto enviados al bot; (2) listar los contactos con su nombre o username, la cantidad de mensajes no leídos y la vista previa del último mensaje; (3) abrir una conversación y mostrar su historial; (4) marcar los mensajes como leídos al abrirla; y (5) permitir responder al contacto, de modo que la respuesta llegue al usuario en Telegram. Requisitos técnicos: backend .NET con Clean Architecture, frontend Angular, base de datos a elección, RabbitMQ para los mensajes entrantes, comunicación en tiempo real a elección, bot propio creado con BotFather, webhook registrado automáticamente con la URL pública de ngrok, y todo levantado con Docker Compose después de completar el `.env`. Quedan fuera de alcance: autenticación, mensajes que no sean de texto, diseño responsive y paginación del historial.

## 1. Requisitos previos

- Docker con Compose v2 (`docker compose`). No hace falta ningún SDK local: .NET y Node corren dentro de contenedores.
- Una cuenta de Telegram (para crear el bot y para probar).
- Una cuenta de ngrok (el plan gratuito alcanza).

## 2. Cómo completar el `.env`

```bash
cp .env.example .env
```

El `.env` no se sube al repositorio (está en `.gitignore`). Completá estas variables:

| Variable | Cómo obtenerla |
|---|---|
| `TELEGRAM_BOT_TOKEN` | En Telegram, hablá con [@BotFather](https://t.me/BotFather), enviá `/newbot`, elegí nombre y username, y copiá el token que te devuelve (formato `123456789:AA...`). |
| `NGROK_AUTHTOKEN` | En el [dashboard de ngrok](https://dashboard.ngrok.com/get-started/your-authtoken), sección *Your Authtoken*. |
| `NGROK_DOMAIN` | Opcional. El dominio estático gratuito de tu cuenta ([Domains](https://dashboard.ngrok.com/domains)), por ejemplo `mi-nombre.ngrok-free.app`. Si lo dejás vacío, ngrok asigna una URL aleatoria y el webhook se vuelve a registrar solo. |
| `TELEGRAM_WEBHOOK_SECRET` | Lo generás vos: `openssl rand -hex 32` (16 a 256 caracteres, solo `A-Z a-z 0-9 _ -`). No hay que cargarlo en BotFather: la aplicación se lo entrega a Telegram sola al registrar el webhook (D-042), y Telegram lo reenvía en cada llamada. |
| `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` | Base local del contenedor. Para la contraseña: `openssl rand -hex 24`. No uses `;` en estos valores porque forman la cadena de conexión. |
| `RABBITMQ_USER`, `RABBITMQ_PASSWORD` | Usuario del broker y de su consola web. Contraseña: `openssl rand -hex 24`. |
| `FRONTEND_PORT` | Puerto del host para la UI (por defecto `4200`). |

Ambas variables de Telegram deben tener valor. Un token con formato válido pero falso (por ejemplo `123:abc`) permite levantar la bandeja y ver la UI sin un bot real; solo fallarán el registro del webhook y los envíos (D-081, D-041).

## 3. Cómo levantar la solución

```bash
docker compose up -d --build
docker compose ps
```

| Servicio | URL |
|---|---|
| Bandeja (UI) | http://localhost:4200 |
| Consola de RabbitMQ | http://localhost:15672 (usuario y contraseña del `.env`) |
| Inspector de ngrok | http://localhost:4040 |

Todos los puertos del host están atados a `127.0.0.1`. El inspector de ngrok muestra el encabezado secreto del webhook, por eso no se expone a la red. Para ver la URL pública y el registro del webhook: `docker compose logs api` y `docker compose logs ngrok`. Para bajar todo conservando los datos: `docker compose down`.

## 4. Cómo probarla de punta a punta

La guía paso a paso está en [docs/qa/manual-e2e-checklist.md](docs/qa/manual-e2e-checklist.md) (la escribe QA en paralelo). Versión corta:

1. Con la solución levantada, abrí http://localhost:4200. Debe verse la bandeja vacía.
2. Desde tu cuenta de Telegram, abrí el bot y enviale un texto. Debe aparecer el contacto en la UI en tiempo real, con el contador de no leídos en 1 y la vista previa.
3. Abrí la conversación: se muestra el historial y el contador vuelve a 0.
4. Escribí una respuesta y enviala. Debe llegar a Telegram; en la UI el mensaje pasa de pendiente (reloj) a enviado (tilde).
5. Reiniciá la página: el historial persiste. Enviá otro mensaje con la conversación cerrada y comprobá que el contador sube.

Verificación de que la URL pública expone solo el webhook (reemplazá `TU_DOMINIO` por tu dominio de ngrok, visible en http://localhost:4040):

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://TU_DOMINIO/api/contacts                       # esperado: 404
curl -s -o /dev/null -w "%{http_code}\n" https://TU_DOMINIO/hubs/inbox                         # esperado: 404
curl -s -o /dev/null -w "%{http_code}\n" -X POST https://TU_DOMINIO/telegram/webhook -d '{}'   # esperado: 401 (sin el secreto)
```

Puede que ngrok devuelva una página intermedia en los navegadores; con `curl` no. Estos códigos son los esperados según el diseño (D-020, D-044); no los verifiqué en esta redacción del README, QA los valida en el checklist.

## 5. Supuestos

Decisiones de alcance tomadas ante lo que el enunciado no define (el identificador entre paréntesis remite a [docs/plan/decisions-log.md](docs/plan/decisions-log.md)).

- Solo se procesan mensajes de texto de chats privados; se ignoran ediciones, grupos, canales y contenido multimedia (D-025).
- Un contacto se identifica por su `chat_id`; su nombre y username se actualizan en cada mensaje entrante (D-026).
- Hay un único operador, el estado de lectura es global y la api corre en una sola réplica (D-027).
- La UI se sirve en `localhost:4200`, no a través de ngrok; ngrok expone únicamente el webhook (D-028, D-020).
- Si se cambia el token del bot sobre una base existente, hay que recrear los datos con `docker compose down -v`, porque el `update_id` de Telegram es por bot (D-052).
- Un mensaje demorado por reintentos de Telegram conserva su fecha original y se ordena en el pasado (D-053).
- «Última versión estable» se interpreta como .NET 10 y Angular 22 al 2026-11-05; .NET 11 y Angular 23 podrían salir en los próximos días (D-054).
- Un empate dentro del mismo segundo entre una respuesta (precisión de milisegundos) y un mensaje entrante (segundos enteros) puede mostrarse en cualquier orden; el desfase de reloj del host tiene el mismo efecto (D-080).
- Ambas variables de Telegram deben tener valor; un token con formato válido pero falso permite usar la bandeja sin bot (D-081).
- El tope de `retry_after` de Telegram es de 10 s y todo 4xx distinto de 429 se considera permanente (D-051).
- El archivo `requirements.md` del desafío no se publica en este repositorio; el resumen está al comienzo de este README (D-036).
- Si Docker Hub limita las descargas anónimas, hay que ejecutar `docker login` (D-061).

## Arquitectura

La solución corre en seis contenedores con Docker Compose. Telegram llama por HTTPS a ngrok, que reenvía solo `POST /telegram/webhook` al puerto 8081 de la **api**. La api valida el secreto, publica el mensaje en **RabbitMQ** y responde 200 recién después de la confirmación del broker. El **worker** consume la cola, guarda en **PostgreSQL** y publica un evento; la api lo reenvía a los navegadores por **SignalR**. Las respuestas del operador siguen el camino inverso y asíncrono: la api guarda el mensaje como pendiente y lo publica, y el worker lo envía a Telegram y registra el resultado. El **frontend** (nginx + Angular) sirve la UI y hace de proxy de `/api` y `/hubs` hacia la api.

- Diagramas C4: [nivel 1, contexto](docs/c4/c4-l1-contexto.svg) y [nivel 2, contenedores](docs/c4/c4-l2-contenedores.svg) (editables en [docs/c4/](docs/c4/README.md)).
- [ADR-001: Base de datos](docs/adr/ADR-001-base-de-datos.md)
- [ADR-002: Tiempo real](docs/adr/ADR-002-tiempo-real.md)
- [ADR-003: Webhook y colas](docs/adr/ADR-003-webhook-y-colas.md)
- [ADR-004: Envío asíncrono de respuestas](docs/adr/ADR-004-envio-asincrono-respuestas.md)
- [ADR-005: Tolerancia a fallas de la base de datos](docs/adr/ADR-005-tolerancia-fallas-base-de-datos.md)

## Decisiones destacadas

- **Worker separado de la api:** la recepción depende solo del broker y el procesamiento solo de la base (D-010, ADR-003).
- **Respuestas en cola con la fila como outbox:** sobreviven a caídas de Telegram y su estado (pendiente, enviado, fallido) es visible; un barredor republica los pendientes (D-021, D-029, ADR-004).
- **Reintento manual de respuestas fallidas** desde la UI, para que `Failed` no sea un callejón sin salida (D-032).
- **ngrok expone solo el puerto 8081 de la api** (el webhook); la API REST y el hub no son públicos porque no hay autenticación (D-020, D-044).
- **Sin pérdida de mensajes entrantes:** los errores de infraestructura se reintentan sin límite y la DLQ solo recibe mensajes venenosos (D-076, ADR-003, ADR-005).
- **El webhook se registra solo y se auto-repara:** un servicio de la api consulta a ngrok y a Telegram cada 60 s, y la api no depende de ngrok para arrancar (D-018, D-041, D-042).
- **Sin duplicados tras un envío confirmado:** luego de la respuesta final de Telegram solo se reintenta el guardado, nunca el envío (D-077, D-087).

## Solución de problemas

- **Docker Hub rate limit** (`toomanyrequests` al descargar imágenes): ejecutá `docker login` y repetí `docker compose up -d --build` (D-061, D-075).
- **ngrok `ERR_NGROK_108`:** el plan gratuito admite una sola sesión de agente a la vez; cerrá otros agentes de ngrok de tu cuenta.
- **Un «fail» de EF sobre `__EFMigrationsHistory` en el primer arranque:** es inofensivo; es la consulta previa a que exista la tabla de migraciones.
- **Cambiaste de bot:** los datos son por bot; recreá los volúmenes con `docker compose down -v` (borra la base) (D-052).
- **Mensajes en la DLQ** (`inbox.incoming.telegram.dlq`, `inbox.outgoing.telegram.dlq`): son mensajes venenosos o con errores no transitorios, que no se ven en la UI. Para inspeccionarlos, abrí la consola de RabbitMQ (http://localhost:15672) → *Queues* → la DLQ → *Get messages*. Para reprocesar uno, corregí la causa y volvé a publicarlo con *Publish message* en el exchange `inbox.incoming` (routing key `telegram.text`) o `inbox.outgoing` (`telegram.send`) (D-093).
- **El worker no tiene healthcheck** (D-038): para ver si funciona, mirá `docker compose logs worker` y los consumidores de las colas en la consola de RabbitMQ.

## Estructura del repositorio

```
backend/      Solución .NET: Domain, Application, Infrastructure, Api, Worker y tests de dominio (Dockerfile multi-target)
frontend/     Workspace Angular, Dockerfile y nginx.conf
infra/        Configuración del agente ngrok
docs/adr/     Decisiones de arquitectura (ADR-001 a ADR-005)
docs/c4/      Diagramas C4 (nivel 1 y 2)
docs/plan/    Plan, contratos, arquitectura y registro de decisiones
docs/ai/      Trabajo con IA: agentes y prompts
docs/qa/      Verificación y checklist manual
docker-compose.yml  .env.example  AGENTS.md
```

## Tests

- **Backend (69 tests):** unitarios sobre el dominio, como pide el enunciado. Cubren cada invariante y transición de estado (`MarkAsRead`, `Pending → Sent | Failed`, `Failed → Pending`, límites del texto, UTC, orden de la conversación).
- **Frontend (33 tests, Vitest):** también son unitarios, sobre las reglas de estado del cliente: orden de contactos y mensajes, precedencia de estados de entrega y *upserts* idempotentes ante eventos duplicados. No son tests de integración ni E2E. Se mantienen porque protegen la corrección del tiempo real, que es donde una bandeja falla de forma más visible (D-095).

Se ejecutan en contenedores, con tu usuario, desde la raíz del repositorio:

```bash
docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget \
  -v "$PWD/backend":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp -e npm_config_cache=/tmp/.npm \
  -v "$PWD/frontend":/app -w /app node:24-alpine sh -c "npm ci && npx ng test --watch=false && npx ng build"
docker compose config -q && docker compose build
```

El primero corre los tests del dominio, el segundo los tests y el build del frontend, y el tercero valida y construye las imágenes.

## Trabajo con IA

El documento de una página sobre las herramientas, los agentes y la validación del código estará en `docs/ai/trabajo-con-ia.md` (se escribe más adelante). Mientras tanto, el detalle está en [docs/ai/](docs/ai/): [agentes](docs/ai/agents.md) y [registro de prompts](docs/ai/prompt-log.md).
