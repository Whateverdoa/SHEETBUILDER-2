#!/bin/bash
set -euo pipefail

# Resolve repo root so the script can be executed from any directory
script_dir="$(cd -- "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
frontend_dir="$script_dir/sheetbuilder-frontend"

if [[ ! -d "$frontend_dir" ]]; then
  echo "Unable to locate frontend at: $frontend_dir" >&2
  exit 1
fi

# Ensure a compatible Node runtime is active
if [[ -s "$HOME/.nvm/nvm.sh" ]]; then
  # shellcheck source=/dev/null
  source "$HOME/.nvm/nvm.sh"
  nvm install 20.19.5 >/dev/null
  nvm use 20.19.5 >/dev/null
fi

if ! command -v node >/dev/null; then
  echo "Node.js is required but was not found on PATH" >&2
  exit 1
fi

node_version="$(node -v)"
echo "Using Node $node_version"

if [[ -z "${VITE_API_URL:-}" ]]; then
  # Try to detect a routable host IP so remote browsers can reach the API
  host_ip=$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null || hostname -I 2>/dev/null | awk '{print $1}' || echo "localhost")
  export VITE_API_URL="http://${host_ip}:5002"
  echo "Resolved VITE_API_URL to $VITE_API_URL"
else
  export VITE_API_URL
fi

cd "$frontend_dir"

echo "VITE_API_URL=$VITE_API_URL"
echo "Installing deps if needed..."
npm install

echo "Starting Vite dev server on port 5174 (host 0.0.0.0)..."
exec npm run dev -- --host 0.0.0.0 --port 5174
