# UI style guide (Step #3)

Source: the production CSS of https://encodelabs.com.ar/es/, downloaded on 2026-11-05. The token values below are copied **verbatim** from the site's CSS custom properties, not eyeballed from screenshots.
Requirement §4: "similar en colores y estilo general… no idéntica… sencilla, prolija y agradable". There are no animations and no responsive layout (desktop only).

## Typography
- **Red Hat Display** from Google Fonts (weights 400, 500, 700), with fallback `system-ui, sans-serif`. This is the site's only font family.
- Base size 15px. Contact name 15px/600. Preview 13px. Timestamps 12px muted. Header title 18px/700.

## Color tokens (use the CSS custom properties exactly as named; define them in `frontend/src/styles.css`)

| Token | Value (from site) | ≈ hex | Use in the inbox |
|---|---|---|---|
| `--color-brand-primary` (brand-50) | `oklch(0.499 0.082 209)` | #1f6f7c | Primary actions (Send button), selected-contact accent bar, links |
| `--color-brand-dark` (brand-70) | `oklch(0.382 0.066 215)` | #0f5560 | Sidebar header background, button hover |
| `--color-brand-deepest` (brand-80) | `oklch(0.282 0.047 212)` | #0b3a42 | Header text on light backgrounds |
| `--color-brand-light` (brand-5) | `oklch(0.951 0.012 201)` | #eef5f6 | Selected contact background, **inbound** bubble |
| `--color-brand-10` | `oklch(0.875 0.026 201)` | #cfe0e2 | Hover on a contact row |
| `--color-accent` (brand-35) | `oklch(0.627 0.132 162)` | #1d9e75 | **Unread badge**, connection indicator "connected" |
| `--color-blue-30` | `oklch(0.794 0.112 208)` | #5cc8e0 | Focus ring |
| `--color-neutral-90` (text-primary) | `oklch(0.209 0 0)` | #181818 | Main text |
| `--color-neutral-60` (text-secondary) | `oklch(0.517 0 0)` | #6b6b6b | Previews, secondary text |
| `--color-neutral-40` (text-muted) | `oklch(0.712 0 0)` | #a2a2a2 | Timestamps, placeholders |
| `--color-neutral-20` (border-default) | `oklch(0.919 0 0)` | #e5e5e5 | Dividers, input borders |
| `--color-neutral-5` (surface-subtle) | `oklch(0.970 0 0)` | #f5f5f5 | Conversation background |
| `--color-white` | `oklch(1 0 0)` | #ffffff | Sidebar and composer surfaces |
| `--color-red-60` | `oklch(0.577 0.214 28)` | #dc2626 | **Failed** delivery, error text |

**Outbound bubble:** `--color-brand-primary` background with white text, which matches the site's dark/brand surfaces carrying inverse text.

## Shape and spacing (site tokens)
- Radius: `--radius-xs: 0.5rem` (inputs, badges), `--radius-sm: 0.75rem` (bubbles), `--radius-md: 1rem` (cards and panels), `--radius-full` (badge pill, avatar circle).
- Spacing scale: 0.25 / 0.5 / 0.75 / 1 / 1.5 / 2 rem.
- Flat surfaces with 1px `--color-neutral-20` borders. At most one soft shadow, `0 1px 2px rgba(2,6,23,.08)`, which is also taken from the site.

## Layout (desktop)
```
┌──────────────── 100vh ────────────────────────────────────────────┐
│ Sidebar 320px (white)        │ Conversation (surface-subtle)        │
│ ┌──────────────────────────┐ │ ┌──────────────────────────────────┐ │
│ │ encodelabs·Bandeja  ● on │ │ │ Avatar  Display name  @username  │ │
│ ├──────────────────────────┤ │ ├──────────────────────────────────┤ │
│ │ (A) Ana Pérez     12:41  │ │ │  inbound bubble (brand-light)    │ │
│ │     Hola, quería… [3]    │ │ │            outbound bubble ✓     │ │
│ │ (J) @juanc        12:10  │ │ │            outbound ⚠ Reintentar │ │
│ │     Gracias!             │ │ ├──────────────────────────────────┤ │
│ └──────────────────────────┘ │ │ [ Escribí una respuesta…  ] [Enviar] │
└───────────────────────────────────────────────────────────────────┘
```
- Avatar: a circle with initials on `--color-brand-10`.
- Delivery icon on outbound messages: Pending = clock (muted); Sent = ✓ (brand); Failed = ⚠ (red) plus the reason and a "Reintentar" button.
- Empty states: "No hay conversaciones todavía. Escribile al bot desde Telegram." and "Seleccioná una conversación".
- Connection indicator in the sidebar header: green dot "En línea", grey "Reconectando…".
- The UI text is in Spanish (rioplatense: "Escribí", "Seleccioná").
