# Diagramas C4

Diagramas de alto nivel (sin componentes) de la bandeja conversacional. Están derivados de `docker-compose.yml` y del código, no del plan.

| Nivel | draw.io (editable) | SVG (exportado) |
|---|---|---|
| 1. Contexto del sistema | [c4-l1-contexto.drawio](c4-l1-contexto.drawio) | [c4-l1-contexto.svg](c4-l1-contexto.svg) |
| 2. Contenedores | [c4-l2-contenedores.drawio](c4-l2-contenedores.drawio) | [c4-l2-contenedores.svg](c4-l2-contenedores.svg) |

## Cómo abrirlos

- **SVG:** abrirlo directamente en cualquier navegador.
- **draw.io:** entrar a https://app.diagrams.net, elegir *Archivo > Abrir desde > Dispositivo* y seleccionar el `.drawio`. También sirve arrastrar el archivo a la ventana, o usar la extensión de draw.io para VS Code.

## Nota sobre el SVG

Los SVG se generaron junto con los `.drawio` a partir de las mismas coordenadas y etiquetas (no son una exportación con la CLI de draw.io). Si se edita un `.drawio`, hay que volver a exportar el SVG desde draw.io (*Archivo > Exportar como > SVG*). En el `.drawio`, draw.io puede ubicar algunas etiquetas de flechas en el punto medio de la línea, un poco distinto del SVG.

## Contenedores del nivel 2 (según `docker-compose.yml`)

`frontend` (nginx + Angular), `api` (ASP.NET Core), `worker` (.NET), `postgres`, `rabbitmq` y `ngrok` (agente). Sistemas externos: ngrok cloud y Telegram Bot API. Personas: operador y usuario de Telegram.
