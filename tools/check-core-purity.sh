#!/usr/bin/env bash
# Enforces ADR: the simulation assembly must have zero Godot references.
# Fails if any file under src/core mentions Godot.
set -euo pipefail
cd "$(dirname "$0")/.."
if grep -rn --include='*.cs' -e 'using Godot' -e 'Godot\.' src/core/; then
  echo "PURITY VIOLATION: Godot reference found under src/core" >&2
  exit 1
fi
if grep -n 'Godot' src/core/Hollowdeep.Core.csproj; then
  echo "PURITY VIOLATION: Godot reference in Hollowdeep.Core.csproj" >&2
  exit 1
fi
echo "core purity OK: no Godot references under src/core"
