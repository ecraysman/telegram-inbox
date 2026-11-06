# Trabajo con IA

## 1. Herramientas
- **Claude Code** (CLI) con **Claude Opus 5.5** como orquestador y para los agentes de diseño, código, QA y revisión; **Claude Sonnet** para el agente redactor.
- Todo se compiló y probó en contenedores (`sdk:10.0`, `node:24-alpine`), sin SDKs locales. Los agentes nunca tuvieron acceso a `.env`.

## 2. Agentes y roles
Un humano decide y aprueba cada uno de los 12 pasos; la sesión principal orquesta. Quien escribe código nunca es quien lo verifica.

| Agente | Rol | Puede escribir |
|---|---|---|
| A0 Orquestador | Plan, contratos, ADRs, integración, commits | `docs/plan`, `docs/adr`, `docs/ai` |
| A1 Arquitecto | Diseño (paso 2) y crítica del plan (paso 7) | Nada |
| A2 · A3 · A4 | Backend · frontend · plataforma, **en paralelo** | Rutas disjuntas |
| A5 QA | Verificación independiente con inyección de fallas | Tests y `docs/qa` |
| A6 Revisor | Revisión adversarial y evaluación final | Nada |
| A7 Redactor | README, C4 y handout, derivados del código | Docs |

El paralelismo fue posible porque las interfaces (HTTP, SignalR, colas, base de datos, variables de entorno) quedaron congeladas en `docs/plan/contracts.md` antes de escribir código. Roles y prompts completos: [`agents.md`](agents.md). Cada prompt enviado está, textual, en [`prompt-log.md`](prompt-log.md).

## 3. Prompts representativos
1. **Arranque (humano, P-001):** *"…we need to make sure we show a great deal of responsability… #7 Review the plan, to find weak points, untracked decisions and potential issues #8 Execute the plan in parallel…"*. Definió el proceso por pasos con un checkpoint humano en cada uno.
2. **Corrección con evidencia obligatoria (A0 → A2, P-028):** *"QA-D1 (major, D-093): on RabbitMQ 4.3.7 a consumer basic.nack(requeue=true) does NOT increment x-delivery-count… Evidence required: on a throwaway stack force an "unexpected" non-transient DB error (…) and show the message lands in the DLQ after ~7 s, once."*
3. **Re-revisión acotada (A0 → A6, P-029):** *"Check: (a) IsChannelLevelClose: are the chosen reply codes and the Peer-initiator rule right for a server channel close (…) versus a connection loss (…)? Any case that now leaves a dead consumer (the N-1 regression) or cancels an in-flight send on a connection blip (D-088)?"*. Cada ronda revisa solo lo corregido, con preguntas concretas.

## 4. Cómo se revisó, corrigió y validó
- **Guardarraíles antes del código:** [`AGENTS.md`](../../AGENTS.md) fija versiones, dependencias permitidas, la regla de dependencias, el manejo de secretos y la definición de terminado. Cada agente tiene rutas propias y, en cada gate, el orquestador verificó con `git status` que nadie escribiera fuera de las suyas.
- **Crítica del plan (paso 7):** A1 encontró, entre otros, que `RequireHost` habría devuelto 404 a cada update de Telegram detrás de ngrok. Se corrigió en el contrato antes de programar (D-044).
- **Revisión adversarial en 4 rondas:** A6 encontró defectos reales que los tests no cubrían. Entre ellos: un bucle que podía reenviar una respuesta hasta 24 veces, mensajes en la DLQ durante una caída de la base y un consumidor que quedaba muerto tras `consumer_timeout`. Cada hallazgo se corrigió y se volvió a revisar. En la tercera ronda la decisión volvió al humano, como fija el plan, y eligió entre dos opciones (D-087).
- **QA independiente:** con un stack descartable y fallas inyectadas (broker y base caídos, `basic.cancel`, restricciones forzadas), A5 encontró dos defectos que la revisión de código no podía ver. Uno era que RabbitMQ 4.3 no cuenta los reencolados del consumidor (D-093). Informe: [`verification-report.md`](../qa/verification-report.md).
- **El orquestador no confió en los reportes:** volvió a compilar (0 warnings) y a correr los tests (69 de dominio + 33 del cliente), leyó los cambios y reconstruyó el stack real. Las afirmaciones se registraron solo después de ver la salida.
- **Trazabilidad:** cada requisito se mapea a decisión, código y evidencia en [`traceability.md`](../qa/traceability.md). Las 100 decisiones (`docs/plan/decisions-log.md`) indican quién las tomó.
- **Errores propios, también registrados:** por ejemplo, anotar un resultado de tests antes de verlo o una búsqueda de secretos invalidada por un alias de `grep`. Se corrigieron y quedaron en el registro de prompts.
- **Lo que la IA no puede validar:** el envío real a Telegram y la revisión visual los hace el humano con [`manual-e2e-checklist.md`](../qa/manual-e2e-checklist.md).
