# Trabajo con IA

## 1. Herramientas
**Claude Code** (CLI) con **Claude Opus 5.5** para el orquestador y los agentes de diseño, código, QA y revisión, y **Claude Sonnet** para el redactor. Todo se compiló y probó en contenedores; los agentes tenían prohibido leer `.env`.

## 2. Agentes y roles
Un humano decide y aprueba cada uno de los 12 pasos; la sesión principal orquesta. Quien escribe código nunca es quien lo verifica.

| Agente | Rol | Puede escribir |
|---|---|---|
| A0 Orquestador | Plan, contratos, ADRs, integración, commits | `docs/plan`, `docs/adr`, `docs/ai` |
| A1 Arquitecto | Diseño (paso 2) y crítica del plan (paso 7) | Nada |
| A2 · A3 · A4 | Backend · frontend · plataforma, **en paralelo** | Rutas disjuntas |
| A5 QA | Verificación independiente con inyección de fallas | Tests y `docs/qa` |
| A6 Revisor | Revisión adversarial y evaluación final | Nada |
| A7 Redactor | README, C4 y guía de evaluación, derivados del código | Docs |

El paralelismo funcionó porque las interfaces quedaron congeladas en `docs/plan/contracts.md` antes de escribir código. Roles y prompts: [`agents.md`](agents.md); cada prompt enviado está, textual, en [`prompt-log.md`](prompt-log.md).

## 3. Prompts representativos
1. **Arranque (humano, P-001):** *"…we need to make sure we show a great deal of responsability… #7 Review the plan, to find weak points, untracked decisions and potential issues #8 Execute the plan in parallel…"*. Fijó el proceso por pasos con un checkpoint humano en cada uno.
2. **Corrección con evidencia obligatoria (A0 → A2, P-028):** *"…on RabbitMQ 4.3.7 a consumer basic.nack(requeue=true) does NOT increment x-delivery-count… Evidence required: … show the message lands in the DLQ after ~7 s, once."*
3. **Re-revisión acotada (A0 → A6, P-029):** *"…Any case that now leaves a dead consumer (the N-1 regression) or cancels an in-flight send on a connection blip (D-088)?"*. Cada ronda revisa solo lo corregido, con preguntas concretas.

## 4. Cómo se revisó, corrigió y validó
- **Reglas antes del código:** [`AGENTS.md`](../../AGENTS.md) fija versiones, dependencias permitidas, la regla de dependencias, secretos y la definición de terminado. En cada gate se verificó con `git status` que ningún agente escribiera fuera de sus rutas.
- **Crítica del plan:** A1 detectó que `RequireHost` habría rechazado cada update de Telegram detrás de ngrok; se corrigió en el contrato antes de programar (D-044).
- **Revisión adversarial (4 rondas):** encontró, entre otros, un bucle que podía reenviar una respuesta hasta 24 veces y un consumidor que quedaba muerto tras `consumer_timeout`. En la tercera ronda la decisión volvió al humano, que eligió entre dos opciones (D-087).
- **QA con fallas inyectadas:** encontró dos defectos que la revisión no podía ver, por ejemplo que RabbitMQ 4.3 no cuenta los reencolados del consumidor (D-093). Ver [`verification-report.md`](../qa/verification-report.md).
- **Sin confiar en los reportes:** el orquestador volvió a compilar (0 warnings), a correr los tests (69 de dominio y 33 del cliente) y a levantar el stack antes de registrar un resultado. Sus propios errores (anotar un resultado antes de verlo, una búsqueda invalidada por un alias de `grep`) también quedaron registrados. Cada requisito tiene su evidencia en [`traceability.md`](../qa/traceability.md).
- **Lo que la IA no valida:** el envío real a Telegram y la revisión visual los hace el humano con [`manual-e2e-checklist.md`](../qa/manual-e2e-checklist.md).
