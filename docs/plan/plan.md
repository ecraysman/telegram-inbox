# Execution plan (Step #4)

> How Steps #5 to #12 are executed: order, parallelism, hand-offs, quality gates and failure handling.
> Inputs: `architecture.md` (approved P-007), `contracts.md`, `tasks.md`, `AGENTS.md`, `ui-style.md`.
> Every step ends with a **human checkpoint** (D-002). Nothing advances without approval.

## 1. Phase map

```
 DONE                     ┌───────────── PLANNING ─────────────┐   ┌────────────── BUILD ──────────────┐   ┌──── PROVE ────┐   ┌─ CLOSE ─┐
 #1 agents                │ #5 ADR-002/003/004 + 1st commit     │   │ #8 Wave 1 (parallel)               │   │ #9 QA ∥ docs  │   │ #11     │
 #2 architecture   ──►    │ #6 prompt log (continuous)          │──►│     A2 backend ∥ A3 frontend ∥ A4  │──►│ #10 deviation │──►│ #12     │
 #3 guides/contracts      │ #7 A1 CRITIQUE → fix plan           │   │    Gate G1 → integration (A0)      │   │     audit     │   │         │
                          └─────────────────────────────────────┘   │    Wave 2: A6 review → fixes → G2  │   └───────────────┘   └─────────┘
                                                                    └────────────────────────────────────┘
```

## 2. Steps in detail

### Step #5: Document (A0)
- **O-1:** write ADR-002 (Tiempo real: SignalR), ADR-003 (Recepción del webhook y topología de colas), ADR-004 (Envío asíncrono de respuestas). They are in Spanish, use the template and are at most one page each. They are derived from `architecture.md` and `decisions-log.md`, so they add no new decisions.
- **O-2:** first commit `docs: planning, ADRs and agent guardrails` (only after the human authorizes commits).
- Afterwards, clear every *(pending: O-1, Step #5)* marker in the decisions log.

### Step #6: Prompt log (continuous, A0)
- Every prompt is appended to `docs/ai/prompt-log.md` **before** the agent is launched. Every result is summarised after it returns.
- At Step #11 the 2–3 representative prompts are chosen from it for `trabajo-con-ia.md`.

### Step #7: Plan review (A1 CRITIQUE)
- Input: all of `docs/plan/**`, `docs/adr/**` and `AGENTS.md`.
- Output: findings ranked blocker/major/minor. A0 fixes the plan and logs every change as a D-xxx, and the human approves.
- **Gate G0:** no open blockers; every major finding is fixed or accepted by the human.

### Step #8: Build
**Wave 1: three agents in parallel**, launched in one message as background agents. They share the working tree but write to disjoint paths (AGENTS.md §7.2), and no agent commits.

| Agent | Tasks | Est. tool calls | Depends on |
|---|---|---|---|
| A2 Backend | B-1…B-10 in milestones M1→M4 (M4 cuttable, see tasks.md) | large (critical path) | contracts only |
| A3 Frontend | F-1…F-6 | medium | contracts only |
| A4 Platform | P-1…P-5; acceptance is `docker compose config -q` (the real build happens at O-4) | small | contracts + the fixed paths in §3 below |

**Gate G1 (A0, per agent report):**
1. The report has all 5 sections.
2. A0 **re-runs** the agent's verification commands itself and does not trust the report.
3. `git status --porcelain --untracked-files=all` shows no writes outside the agent's owned paths. Untracked files count.
4. A0 checks the contracts at the seams: DTO names, hub method names, queue names.

**Integration (A0, O-4):** `docker compose up --build` with a test `.env`. If the human has filled in the real `.env`, A0 uses it without ever printing it; otherwise it uses dummy tokens, and webhook registration then retries harmlessly. A0 fixes any seam bug in the owning agent's area by sending the issue back to that agent (SendMessage), so ownership is preserved. One-line glue fixes are done by A0 and logged.

**Wave 2:**
1. A6 runs an adversarial review (R-1).
2. Blocker and major findings go back to the owning agent.
3. Then a re-review limited to the changed files.

**Gate G2:**
- No open blocker or major findings.
- Build and tests green.
- `docker compose up` on a clean checkout: postgres, rabbitmq, api and frontend **healthy**; worker **running** (it has no healthcheck by design, D-038); ngrok running with the real `.env`.
- Commit: `feat: inbox backend, frontend and platform`. Agents never commit; only A0 commits, after each gate.

### Step #9: Prove (parallel)
- **A5 QA:** Q-1 to Q-5, simulated Telegram traffic, and `docs/qa/verification-report.md`.
- **A7 Writer** (in parallel; it only reads code): W-1 README and W-2 C4 diagrams derived from `docker-compose.yml` and the code.
- **Human live test** (D-003): with the real bot, following A5's checklist. Expected time: about 10 minutes.
- **Gate G3:** every §3.1 and §3.2 item is PASS with evidence, or is explicitly accepted by the human.

### Step #10: Deviation audit (A0)
- Traceability matrix (`docs/qa/traceability.md`): requirement → design decision → code location → evidence.
- **Plan vs. built:** every D-xxx marked as honoured or deviated, with the reason. Every deviation that an agent reported is listed.
- **Dangling references:** every ADR, file or task ID referenced in the docs exists.

### Step #11: Handout (A7 + A0)
- W-3 handout. Its content is agreed with the human at the start of Step #11.
- O-6 `trabajo-con-ia.md` (at most 1 page) and O-7 bonus answers.

### Step #12: Evaluation review (A6 evaluator mode, R-2)
- Score each §7 criterion (business vision 15, architecture 20, decisions 20, AI 15, code 20, functionality/UI 10) with the gaps.
- A0 closes the cheap gaps; the human decides on the rest. Final commit, then the public repo is pushed by the human, or by A0 if authorized.

## 3. Seam decisions fixed now (to avoid cross-agent blocking)

| Seam | Fixed value | Owner of each side |
|---|---|---|
| Backend project paths | `backend/src/Inbox.Api/Inbox.Api.csproj`, `backend/src/Inbox.Worker/Inbox.Worker.csproj`, solution `backend/Inbox.sln` | A2 creates them; A4's Dockerfile builds them |
| Backend Dockerfile | One multi-stage file with targets `api` and `worker` (`docker build --target api`) | A4 |
| Api healthcheck | `curl` installed in the api runtime stage; compose runs `curl -fsS http://localhost:8080/health/ready` | A4 (Dockerfile + compose); A2 provides the endpoint |
| Worker healthcheck | **None.** No service depends on the worker; its liveness is shown by the logs and by the RabbitMQ consumer count | A4, documented in the README |
| Worker startup | `depends_on: api: service_healthy` (migrations are applied by the api, ADR-001) | A4 |
| ngrok independence (D-041) | The api **must not** depend on ngrok. With a missing or invalid `NGROK_AUTHTOKEN` the inbox still runs; only the public tunnel is missing | A4 (compose), A2 (non-blocking registration) |
| Angular project name / output | Project `inbox`, output `frontend/dist/inbox/browser` | A3 creates it; A4's Dockerfile copies it |
| Angular API base URL | Relative (`/api`, `/hubs/inbox`); no environment files needed (single origin via nginx) | A3 |
| Listening ports in the api | Kestrel `http://+:8080;http://+:8081` via `ASPNETCORE_URLS`; the webhook endpoint is restricted to 8081 | A2 code, A4 env |

## 4. Failure handling

| Situation | Response |
|---|---|
| An agent report fails a gate (build red, missing section, path violation) | Sent back to the same agent with the evidence. After two failed rounds, A0 escalates to the human |
| An agent needs a contract change | The agent stops and reports. A0 decides, updates `contracts.md` and logs a D-xxx; the human is told at the next checkpoint. Affected agents are notified |
| Agents disagree at a seam | `contracts.md` wins. If the contract is silent, A0 decides and logs it |
| A fact in a plan turns out wrong (API shape, image tag) | The implementer uses the real behaviour, reports it as a deviation, and A0 updates the docs |
| A real-environment problem (ngrok session limit, Telegram blocked) | Documented as a risk; the human decides |
| Agent output looks plausible but is unverified | Treated as not done. "Evidence over claims" (AGENTS.md §7.5) |

## 5. What the human is needed for
1. A checkpoint approval at the end of every step.
2. Authorization to commit (Step #5) and to push or create the public repo (Step #12).
3. Filling in `.env` with the real `TELEGRAM_BOT_TOKEN`, `NGROK_AUTHTOKEN` and optional `NGROK_DOMAIN`, ideally before Step #8 integration.
4. A live end-to-end test from a Telegram account (Step #9).
5. Content of the handout (Step #11).
