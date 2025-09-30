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

export VITE_API_URL="${VITE_API_URL:-http://localhost:5002}"

cd "$frontend_dir"

echo "VITE_API_URL=$VITE_API_URL"
echo "Installing deps if needed..."
npm install

echo "Starting Vite dev server on port 5174..."
exec npm run dev -- --port 5174
