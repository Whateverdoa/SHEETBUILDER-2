# SheetBuilder Frontend

React + TypeScript UI for the SheetBuilder PDF processing service. The app handles large PDF uploads, queues files for processing, and streams progress updates from the API.

## Quick start
```bash
npm install
npm run dev -- --port 5174
```
Set `VITE_API_URL` to point at the backend (defaults to `http://localhost:5002`). Example:
```bash
VITE_API_URL=https://sheetbuilder-api.example.com npm run dev
```

## Scripts
| Command | Description |
| ------- | ----------- |
| `npm run dev` | Start Vite dev server (port 5174 by default) |
| `npm run build` | Type-check and build production bundle |
| `npm run lint` | ESLint with type-aware rules |
| `npm run test` | Vitest in watch mode |
| `npm run test:run` | Vitest in CI mode |
| `npm run preview` | Preview the production build |

## Environment variables
| Variable | Purpose | Default |
| -------- | ------- | ------- |
| `VITE_API_URL` | Backend base URL used for all API calls | `http://localhost:5002` |

## Production build
```bash
npm run build
```
The compiled assets land in `dist/`. Serve them via the backend, a CDN, or any static file host. Pair with the backend published from `ConsoleApp1_vdp_sheetbuilder`.

## Testing notes
- Upload tests rely on mocked `XMLHttpRequest`; run `npm run test:run` in CI.
- Strict TypeScript settings prevent unused variables and unsupported toast variants—fix build warnings before committing.

Refer to the root `README.md` for the end-to-end runbook covering both frontend and backend workflows.
