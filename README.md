# SheetBuilder 2 – Production Runbook

SheetBuilder 2 pairs a .NET 8 Web API for Variable Data Printing workflows with a React + TypeScript frontend. This guide covers day-to-day development tasks and the steps required to build, verify, and deploy both services for production use.

## Project layout
- `ConsoleApp1_vdp_sheetbuilder/` – ASP.NET Core API, Docker assets, integration docs
- `sheetbuilder-frontend/` – Vite + React UI with upload/progress tooling
- `start-backend-sandbox.sh`, `start-frontend-sandbox.sh` – convenience scripts for local work
- `analysis/` – generated analysis artefacts such as `cost-summary.json`
- `.analysisignore` – shared ignore list for agent-driven repository analysis

## Prerequisites
| Tool | Minimum version | Notes |
| ---- | --------------- | ----- |
| .NET SDK | 8.0.x | builds and tests the API |
| Node.js | 20.x | match via `nvm` if available |
| npm | 10.x | ships with Node 20 |
| Docker | 24+ | optional, for container packaging |

## Local development
1. **Backend**
   ```bash
   cd ConsoleApp1_vdp_sheetbuilder
   dotnet restore
   dotnet run
   ```
   The API listens on `https://localhost:7001` and `http://localhost:5000` by default. Use `./start-backend-sandbox.sh` to launch with production-like upload limits and CORS flags.

2. **Frontend**
   ```bash
   cd sheetbuilder-frontend
   npm install
   npm run dev -- --port 5174
   ```
   Point the UI at the API with `VITE_API_URL=http://localhost:5002 npm run dev` or set the environment variable before invoking `start-frontend-sandbox.sh`.

## Verification steps
Run these commands before promoting a build:
```bash
# API unit tests
cd ConsoleApp1_vdp_sheetbuilder
dotnet test

# Publishable build artefacts
dotnet publish ConsoleApp1_vdp_sheetbuilder/ConsoleApp1_vdp_sheetbuilder.csproj -c Release

# Frontend quality gates
cd ../sheetbuilder-frontend
npm run lint
npm run test:run
npm run build
```
All commands must exit cleanly; the frontend build now enforces strict TypeScript checks.

## Production build & deployment
- **API**: publish with `dotnet publish -c Release`, or build the container image using the provided `Dockerfile`/`docker-compose.yml`. Mount `wwwroot/uploads` to persistent storage in production.
- **Frontend**: generate static assets with `npm run build`. Serve the `dist/` folder via a CDN or behind the API (e.g., Nginx, Azure Static Web Apps).
- **Environment variables**:
  - `ASPNETCORE_ENVIRONMENT` (`Production`, `Staging`, etc.)
  - `ASPNETCORE_URLS` (default `http://+:80` inside containers)
  - `UploadReliability__LargeFileThresholdMb` (default `200` MB)
  - `UploadReliability__EnforceProgressForLarge` (`true` to force progress endpoint)
  - `VITE_API_URL` for the UI runtime API target

## Observability & maintenance
- Backend background cleanup removes processed PDFs after `FileStorage.MaxStorageAgeDays` (default 7). Tune this in `appsettings.json` or via environment variables.
- Use `/api/pdf/health` for container health checks (already wired in `docker-compose.yml`).
- SSE progress streams are exposed at `/api/pdf/progress/{jobId}`; the frontend retries automatically if the connection drops.

## Repository automation aids
- `.analysisignore` excludes build artefacts, PDFs, and generated assets to keep automated analyses lean.
- `analysis/cost-summary.json` captures the current token-based cost estimate for full-repo LLM analysis (see notes inside the file for assumptions).
- GitHub Actions workflow (`.github/workflows/ci.yml`) runs backend tests/publish and frontend lint/tests/build on every push and pull request.

## Support scripts
- `start-backend-sandbox.sh` – runs the API with sane defaults for local large-file testing.
- `start-frontend-sandbox.sh` – ensures Node 20 via `nvm`, installs dependencies if missing, and starts Vite on port 5174.

For new environments replicate the steps above, then point the frontend to the deployed API endpoint via `VITE_API_URL` or reverse proxy configuration.
