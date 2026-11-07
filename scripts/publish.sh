#!/usr/bin/env bash
# Self-contained release builds: ./scripts/publish.sh [linux-x64|win-x64|all]
set -euo pipefail
cd "$(dirname "$0")/.."
targets=("${1:-all}")
[[ "${targets[0]}" == all ]] && targets=(linux-x64 win-x64)
for rid in "${targets[@]}"; do
  dotnet publish src/XiaomiBE6500Manager -c Release -r "$rid" --self-contained true \
    -p:DebugType=None -o "artifacts/$rid"
  echo "-> artifacts/$rid"
done
