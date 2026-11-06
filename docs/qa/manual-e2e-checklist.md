# Checklist manual de punta a punta (con Telegram real)

> Para la persona que prueba con su propia cuenta de Telegram y el `.env` real. Duración: unos 10–15 minutos.
> Marcá cada casilla. Si algo falla, anotá el paso, la hora y lo que viste, y guardá
> `docker compose logs api worker --since 15m` (esos logs no muestran tokens ni el texto de los mensajes).
> Nunca pegues el contenido de `.env` ni el inspector de ngrok (muestra el header secreto) en un reporte.

## 0. Preparación
- [ ] `.env` completo: `TELEGRAM_BOT_TOKEN`, `TELEGRAM_WEBHOOK_SECRET` (16–256 caracteres `A-Z a-z 0-9 _ -`), `NGROK_AUTHTOKEN` y, si tenés, `NGROK_DOMAIN`.
- [ ] Tenés a mano el celular o Telegram Desktop con una cuenta **distinta** del bot, y un navegador en la PC.

## 1. Levantar la solución
- [ ] `docker compose up -d --build` (el `--build` asegura que se usa el código del último commit).
- [ ] `docker compose ps`: `postgres`, `rabbitmq`, `api` y `frontend` en **healthy**; `worker` y `ngrok` en **Up** (el worker no tiene healthcheck a propósito).
- [ ] `docker compose logs api | grep "webhook registered"` muestra `Telegram webhook registered at https://<tu-dominio>/telegram/webhook`. Anotá esa URL.
- [ ] Abrí http://localhost:4200: se ve la bandeja (panel de contactos a la izquierda, conversación al centro) **sin** aviso de "sin conexión".

## 2. Frontera de seguridad (30 segundos, desde la PC)
Reemplazá `URL` por la URL anotada, sin `/telegram/webhook`:
- [ ] `curl -s -o /dev/null -w '%{http_code}\n' URL/api/contacts` → `404`
- [ ] `curl -s -o /dev/null -w '%{http_code}\n' -X POST URL/hubs/inbox/negotiate` → `404`
- [ ] `curl -s -o /dev/null -w '%{http_code}\n' -X POST -d '{}' URL/telegram/webhook` → `401` (sin el secreto)

## 3. Recibir en tiempo real (3.1.1, 3.1.2)
- [ ] Con la bandeja abierta y **sin recargar**, mandale al bot desde Telegram: `Hola, ¿tienen stock? 😀`
- [ ] En 1–2 s aparece el contacto con tu **nombre** (o `@username` si no tenés nombre), la **vista previa** del mensaje, la hora y un contador de **no leídos = 1**.
- [ ] Mandá 2 mensajes más (`segundo`, `tercero`): el contador sube a 3 y la vista previa muestra `tercero`. El contacto queda primero en la lista.
- [ ] (Opcional) Si tenés una segunda cuenta, escribí desde ella: aparece un segundo contacto, y el que escribió último queda arriba.
- [ ] Mandá un sticker o una foto: **no** aparece nada en la bandeja (fuera de alcance, se ignora).

## 4. Abrir la conversación y marcar como leído (3.1.3, 3.1.4)
- [ ] Hacé clic en el contacto: se ve el historial completo en orden, con los mensajes entrantes de un lado.
- [ ] El contador de no leídos desaparece (0) al abrir.
- [ ] Recargá la página (F5): el contador sigue en 0 (quedó guardado, no es solo visual).
- [ ] Con la conversación **abierta**, mandá otro mensaje desde Telegram: aparece al instante en la conversación y el contador **no** vuelve a subir.
- [ ] Abrí otro contacto (o ninguno) y mandá un mensaje al bot: el contador del primero sube a 1.

## 5. Responder (3.1.5): el caso feliz
- [ ] En la conversación, escribí `Sí, tenemos *stock* <b>hoy</b>` y apretá **Enter**.
- [ ] La burbuja aparece enseguida con el ícono de **reloj** (pendiente) y en 1–2 s pasa a **✓** (enviado).
- [ ] En Telegram llega el mensaje **literal**, con los asteriscos y `<b>` tal cual (sin negrita).
- [ ] **Shift+Enter** inserta un salto de línea en vez de enviar. Mandá un mensaje de dos líneas: llega con el salto de línea.
- [ ] Recargá la página: la respuesta sigue en su lugar, con ✓.

## 6. Respuesta fallida y Reintentar (bloqueando el bot)
- [ ] En Telegram, abrí el chat con el bot → menú (⋮ o el nombre del bot) → **Bloquear bot** / **Detener bot**.
- [ ] En la bandeja, respondé `prueba bloqueado`. La burbuja pasa de reloj a **⚠ fallido**, con un motivo parecido a `rejected: Forbidden: bot was blocked by the user` y el botón **Reintentar**. No hay reintentos automáticos (es un 403 permanente) y no llega nada a Telegram.
- [ ] En Telegram, **desbloqueá** el bot (**Desbloquear** / **Reiniciar**). Si Telegram manda `/start` automáticamente, va a aparecer como mensaje entrante en la bandeja: es lo esperado.
- [ ] En la bandeja, apretá **Reintentar** en el mensaje fallido: vuelve a reloj y luego a **✓**, y el mensaje llega a Telegram. La burbuja conserva su posición original en la conversación.
- [ ] Apretar Reintentar dos veces seguidas rápido no genera dos envíos (el segundo responde 409 y la UI se refresca).

## 7. Robustez (opcional, unos 3 minutos)
- [ ] **Reconexión del navegador:** `docker compose restart api`. La UI muestra el estado "reconectando" y vuelve sola en unos segundos. Mientras la api vuelve, mandá un mensaje al bot: después de reconectar aparece (Telegram reintenta el webhook si recibió un error).
- [ ] **Worker caído:** `docker compose stop worker`, mandá un mensaje al bot (no aparece todavía), `docker compose start worker`: el mensaje aparece en segundos, **una sola vez**.
- [ ] **Composer con error:** `docker compose stop api`, escribí una respuesta y apretá Enter: se muestra un error y el **texto se conserva** en el campo. `docker compose start api` y reenviá.
- [ ] RabbitMQ UI (http://localhost:15672, usuario y clave de `.env`): en *Queues*, `inbox.incoming.telegram` e `inbox.outgoing.telegram` tienen **1 consumidor** cada una, y las DLQ están vacías.

## 8. Estilo (sección 4 del enunciado)
- [ ] Layout de escritorio: panel lateral de contactos y panel central con la conversación y el campo para responder.
- [ ] Colores y tipografía similares a encodelabs.com.ar (sobrio, prolijo), sin animaciones.

## 9. Cierre
- [ ] `docker compose down` (sin `-v`, para conservar los datos).
- [ ] Pasale a QA las casillas que fallaron, con hora y logs.
