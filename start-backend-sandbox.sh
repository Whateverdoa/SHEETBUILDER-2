#!/bin/bash
set -euo pipefail

# Resolve repository root relative to this script so we can launch from any PWD
script_dir="$(cd -- "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_path="$script_dir/ConsoleApp1_vdp_sheetbuilder/ConsoleApp1_vdp_sheetbuilder/ConsoleApp1_vdp_sheetbuilder.csproj"

if [[ ! -f "$project_path" ]]; then
  echo "Unable to locate backend project at: $project_path" >&2
  exit 1
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:5002}"

# Reliability knobs (optional)
export UploadReliability__EnforceProgressForLarge="${UploadReliability__EnforceProgressForLarge:-true}"
export UploadReliability__LargeFileThresholdMb="${UploadReliability__LargeFileThresholdMb:-200}"
export UploadReliability__IdempotencyActive="${UploadReliability__IdempotencyActive:-true}"
export UploadReliability__RecentResultTtlMinutes="${UploadReliability__RecentResultTtlMinutes:-30}"

echo "Starting backend at $ASPNETCORE_URLS (project: $project_path) ..."
exec dotnet run --project "$project_path"
