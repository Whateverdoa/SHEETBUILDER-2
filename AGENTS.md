# Repository Guidelines

## Project Structure & Module Organization
ConsoleApp1_vdp_sheetbuilder/ConsoleApp1_vdp_sheetbuilder/ hosts the .NET 8 API: Controllers/ expose sync and progress endpoints, Services/ orchestrate fingerprinted jobs, Models/ shape DTOs, and wwwroot/uploads/ keeps transient payloads. sheetbuilder-frontend/ contains the React + TypeScript client; feature code lives under src/ (components, hooks, stores, services, utils) with Vitest helpers in src/test. Use the root start-*-sandbox.sh scripts to spin up the paired environments with reliability flags set.

## Build, Test, and Development Commands
Run `cd ConsoleApp1_vdp_sheetbuilder && dotnet restore && dotnet run` for local API work, or `./start-backend-sandbox.sh` to boot the progress-tunable sandbox. Ship server bits with `dotnet publish -c Release`. For the UI, execute `cd sheetbuilder-frontend && npm install`, then `npm run dev -- --port 5174` (honors VITE_API_URL). Verify bundles with `npm run build` and smoke-test via `npm run preview`.

## Coding Style & Naming Conventions
Use 4-space indent, PascalCase types, camelCase locals, and the Async suffix in C#. Log with structured contexts that include jobId. Keep long-running tasks inside PdfProcessingService or ProgressService. In TypeScript, stick to 2-space indent, PascalCase components, camelCase Zustand stores, and `use*` prefixes for hooks. Tailwind utility combos live inline; reuse helpers from src/utils. Format with `dotnet format` and `npm run lint` before submitting.

## Testing Guidelines
Backend tests run through `dotnet test ConsoleApp1_vdp_sheetbuilder.sln` with xUnit; focus on fingerprint idempotency, 409 handling above 200 MB, and progress payload shapes. Frontend specs belong in `*.test.tsx`, leverage src/test/setup.ts, and should cover SSE reconnects, jobId restoration, and guarded progress math using `npm run test` or `npm run test:ui`.

## Commit & Pull Request Guidelines
Write Conventional Commits such as `feat(frontend): guard progress parsing`; keep subjects ≤72 characters and add brief bullet bodies when touching multiple layers. PRs should summarize behavior changes, list verification steps (`npm run test`, `dotnet test`, manual SSE check), link tickets or the progress TDD plan, and flag config or DTO shifts for both frontend and backend reviewers.

## Progress Reliability Expectations
Prefer `/api/pdf/process-with-progress` and surface guidance or a 409 for legacy uploads above 200 MB. Persist jobId so clients can reattach instead of re-uploading, and instrument UI fallbacks when SSE drops to polling. Tune `UploadReliability__*` environment variables through the sandbox scripts to validate duplicate detection windows before merging.
