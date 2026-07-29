# SperoFlow Playwright E2E

Browser tests for **login**, **CSRF**, and **AI proposal approval**.

## Projects

| Project | When it runs | What it needs |
|---------|----------------|---------------|
| `mocked` | Default / CI | Local Next.js + mock API (auto-started) |
| `live` | Only if env set | Running edge (`E2E_BASE_URL`) + real user |

## Mocked (CI / laptop without full stack)

```powershell
cd e2e
npm install
npx playwright install chromium
npm run test:mocked
```

Playwright starts:

1. `e2e/mock-api/server.mjs` on port `18080`
2. `frontend` `next dev` with `API_PROXY_TARGET` / `API_INTERNAL_BASE_URL` pointing at the mock

Coverage:

- Login form renders
- Login POST includes `X-CSRF-TOKEN` after `/auth/csrf`
- Wrong password stays on `/login`
- Direct login without CSRF is rejected
- `/roles` shows a pending proposal; **Approve** sends CSRF + `concurrencyToken`

## Live stack

Point at Caddy (or any edge that serves web + `/api`):

```powershell
$env:E2E_BASE_URL = "https://app.example.com"   # or http://127.0.0.1 with compose
$env:E2E_EMAIL = "you@example.com"
$env:E2E_PASSWORD = "your-password"
cd e2e
npm run test:live
```

## Notes

- Production Compose still routes `/api` via Caddy; `API_PROXY_TARGET` is only for local Next + mock/E2E.
- Mock uses cookie `speroflow-xsrf` (not `__Host-`) because HTTP localhost cannot set `__Host-` Secure cookies.
