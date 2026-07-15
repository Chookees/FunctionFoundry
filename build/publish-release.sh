#!/usr/bin/env bash
# Restore, build Release, pack packages, and collect deliverables into ./Release
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_NOLOGO="${DOTNET_NOLOGO:-1}"
export DOTNET_CLI_TELEMETRY_OPTOUT="${DOTNET_CLI_TELEMETRY_OPTOUT:-1}"

RELEASE_DIR="$ROOT/Release"
STAGE_BIN="$RELEASE_DIR/bin"
STAGE_PACKAGES="$RELEASE_DIR/packages"

echo "==> Repository root: $ROOT"
echo "==> Cleaning $RELEASE_DIR"
rm -rf "$RELEASE_DIR"
mkdir -p "$STAGE_BIN" "$STAGE_PACKAGES"

echo "==> Restoring"
dotnet restore FunctionFoundry.slnx

echo "==> Building Release"
dotnet build FunctionFoundry.slnx -c Release --no-restore

echo "==> Packing NuGet packages"
rm -rf "$ROOT/artifacts/packages"
mkdir -p "$ROOT/artifacts/packages"
dotnet pack FunctionFoundry.slnx -c Release --no-build -o "$ROOT/artifacts/packages"

echo "==> Collecting library binaries"
shopt -s nullglob
for project_dir in "$ROOT"/src/FunctionFoundry.*; do
  name="$(basename "$project_dir")"
  src_out="$ROOT/artifacts/bin/$name/release"
  if [[ ! -d "$src_out" ]]; then
    echo "    skip $name (no release output at $src_out)"
    continue
  fi

  dest="$STAGE_BIN/$name"
  mkdir -p "$dest"
  # Copy assembly, docs, symbols; skip deps.json/runtimeconfig from class libraries when absent.
  for pattern in "$name.dll" "$name.xml" "$name.pdb" "$name.deps.json"; do
    if [[ -f "$src_out/$pattern" ]]; then
      cp -f "$src_out/$pattern" "$dest/"
    fi
  done
  echo "    $name"
done

echo "==> Collecting NuGet packages"
package_count=0
for pkg in "$ROOT"/artifacts/packages/FunctionFoundry.*.nupkg "$ROOT"/artifacts/packages/FunctionFoundry.*.snupkg; do
  [[ -e "$pkg" ]] || continue
  cp -f "$pkg" "$STAGE_PACKAGES/"
  package_count=$((package_count + 1))
done
echo "    $package_count package file(s)"

echo "==> Writing Release manifest"
{
  echo "FunctionFoundry Release bundle"
  echo "GeneratedUTC=$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  echo "Host=$(uname -s) $(uname -m)"
  echo "DotnetSdk=$(dotnet --version)"
  echo
  echo "Libraries:"
  find "$STAGE_BIN" -type f | sort | sed "s|^$RELEASE_DIR/||"
  echo
  echo "Packages:"
  find "$STAGE_PACKAGES" -type f | sort | sed "s|^$RELEASE_DIR/||"
} > "$RELEASE_DIR/MANIFEST.txt"

echo
echo "Release folder ready: $RELEASE_DIR"
find "$RELEASE_DIR" -maxdepth 2 -type f | sort | sed "s|^$ROOT/||"
