# Progress API Reliability TDD Plan

Purpose
- Eliminate automatic re-uploads for large files (>200MB), ensure single processing per user action, and enable robust reconnection without re-sending the file.
- Align normal frontend with progress-enabled flow and enforce server-side idempotency.

Problem Summary
- Normal frontend still posts to /api/pdf/process (sync), times out, then retries the entire 540MB upload.
- Progress parsing errors caused re-upload attempts instead of reconnecting to an existing job.
- Backend accepts duplicate uploads and processes the same file twice.

Goals (Acceptance Criteria)
- AC1: One upload per user action. No automatic re-upload loops.
- AC2: If UI disconnects, it restores progress by jobId (SSE or polling) without re-uploading.
- AC3: For the same file+params within N minutes, the backend returns existing jobId/result (no duplicate processing).
- AC4: Normal frontend never calls /api/pdf/process for large files; it uses /process-with-progress only.
- AC5: Progress events/Status payload match a documented schema; client-side code handles missing fields safely.

Out of Scope
- Changing the PDF algorithm or output format.
- Auth or user accounts.

API Contract (Progress)
- Progress event (SSE data JSON) and Status include:
  {
    "jobId": string,
    "stage": "Initializing"|"PreparingDimensions"|"ProcessingPages"|"OptimizingOutput"|"Finalizing"|"Completed"|"Failed",
    "currentPage": number,
    "totalPages": number,
    "percentageComplete": number,     // 0..100
    "pagesPerSecond": number,
    "estimatedTimeRemaining": string, // ISO8601 or HH:mm:ss
    "elapsedTime": string,            // ISO8601 or HH:mm:ss
    "currentOperation": string,
    "performance": {
      "memoryUsageMB": number,
      "cacheHitCount": number,
      "cacheMissCount": number,
      "cacheHitRatio": number,
      "xObjectsCached": number,
      "sheetsGenerated": number
    },
    "timestamp": string               // ISO8601
  }

- Start response (POST /api/pdf/process-with-progress):
  { "success": true, "jobId": string }
  - If duplicate of active job: { "success": true, "jobId": string, "duplicateOf": true }

- Status response (GET /api/pdf/status/{jobId}):
  {
    "success": true,
    "jobId": string,
    "stage": string,
    "startTime": string,
    "endTime": string|null,
    "progress": Progress | null,
    "result": {
      "success": boolean,
      "message": string,
      "outputFileName": string|null,
      "downloadUrl": string|null,
      "processingTime": string,
      "inputPages": number,
      "outputPages": number
    } | null,
    "error": string | null
  }

Policy for Legacy Endpoint (/api/pdf/process)
- For request bodies > 200MB (configurable):
  - Return 409 Conflict with JSON: { "success": false, "message": "Large files must use /api/pdf/process-with-progress" }
  - Do not start processing and do not accept the upload stream.

Idempotency (Backend)
- Fingerprint: { originalFileName, contentLength, rotationAngle, order } (+ optional SHA-256 if added later).
- Active job registry (in-memory) keyed by fingerprint:
  - If duplicate arrives while active: return { success: true, jobId, duplicateOf: true } and DO NOT start a second task.
- Recently-completed cache (TTL configurable, e.g., 30 minutes) keyed by fingerprint:
  - If duplicate arrives after completion and output file exists: return prior result (success + downloadUrl) without reprocessing.

Client Behavior (Normal Frontend)
- Always upload via /api/pdf/process-with-progress.
- Persist jobId in localStorage per file fingerprint (fileName + size + params). On reload or error, check for existing jobId and reattach (SSE, else polling) — never re-upload automatically.
- Error handling:
  - Do not retry upload on any UI/runtime error.
  - Only reconnect to existing job or present actionable error.
- Progress handling:
  - Use percentageComplete, currentPage, totalPages, pagesPerSecond. Guard .toFixed() calls.
- UI Lock:
  - Prevent multiple simultaneous submissions; unlock after completion/error.

TDD Test Matrix
Frontend
- F1: Progress parsing tolerant to missing/undefined values; no exceptions thrown.
- F2: On SSE disconnect, switches to polling; no new upload.
- F3: On page reload, restores job from localStorage and reattaches; no new upload.
- F4: Never calls /api/pdf/process for files > 200MB; asserts POST path used is /process-with-progress.
- F5: Single submission lock prevents duplicate clicks from sending a second upload.

Backend
- B1: Duplicate active upload returns the same jobId; only one background task created.
- B2: Duplicate after completion within TTL returns prior result; no new processing.
- B3: Legacy endpoint rejects large uploads with 409; no processing triggered.
- B4: Progress/Status payload schema test.

Tasks (by area)
Frontend (normal app)
1) Switch upload flow to /api/pdf/process-with-progress only.
2) Persist jobId keyed by fingerprint; on init, if fingerprint has jobId, attach to progress/status.
3) SSE → polling fallback without re-upload.
4) Fix progress parsing to match schema; guard undefined values.
5) Add submission lock (prevent duplicate sends) and release on completion/error.
6) Remove any retry logic that re-sends FormData on error; retries only reattach to existing jobId.
7) Add console + UI diagnostics for jobId and current state.

Backend
1) Add fingerprint computation (filename, size, rotation, order).
2) Add in-memory active job registry; on duplicate, return existing jobId (no new task).
3) Add recently-completed cache (TTL) with result shortcut.
4) Enforce 409 on /api/pdf/process for large uploads; include guidance message.
5) Expand logging: origin, user-agent, route, jobId, fingerprint.

DevOps/Verification
1) Add minimal endpoint to dump active/finished job registry (dev-only) or enable structured logs.
2) Run large file (>500MB) scenario and verify:
   - One upload only
   - No calls to /api/pdf/process
   - Reattach on reload uses existing jobId
   - Duplicate POST returns existing jobId/result

Rollout Plan
- Phase 1: Frontend switch + parsing fixes + lock (no server change required to stop re-uploads).
- Phase 2: Backend idempotency and legacy endpoint guard (prevents waste from any remaining old clients).
- Phase 3: Logging/observability checks and clean-up.

Branch
- Name: progress-api-reliability-tdd
- Scope: Documentation + TDD tests + incremental implementation per phases above.
