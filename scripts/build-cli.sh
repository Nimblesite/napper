#!/usr/bin/env bash
set -euo pipefail

# Build the Napper CLI and copy it into the VSCode extension bin directory.
# Called from src/Napper.VsCode via: bash ../../scripts/build-cli.sh

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXT_BIN="${REPO_ROOT}/src/Napper.VsCode/bin"

ARCH="$(uname -m)"
OS="$(uname -s)"

case "${OS}" in
  Darwin)
    case "${ARCH}" in
      arm64)  RID="osx-arm64" ;;
      x86_64) RID="osx-x64" ;;
      *)      echo "Unsupported arch: ${ARCH}" >&2; exit 1 ;;
    esac
    ;;
  Linux)  RID="linux-x64" ;;
  *)      echo "Unsupported OS: ${OS}" >&2; exit 1 ;;
esac

OUT_DIR="${REPO_ROOT}/out/${RID}"

# NativeAOT per [CLI-AOT-MIGRATION]: a single statically-linked native binary with
# zero .NET runtime dependency — the same artifact that ships in releases and the VSIX,
# so tests exercise the REAL deployed CLI. (Linux needs `clang` + `zlib1g-dev`.)
echo "==> Building CLI (NativeAOT) for ${RID}..."
dotnet publish "${REPO_ROOT}/src/Napper.Cli/Napper.Cli.fsproj" \
  -r "${RID}" \
  -p:PublishAot=true \
  -o "${OUT_DIR}" \
  --nologo

echo "==> CLI built → ${OUT_DIR}/"
mkdir -p "${EXT_BIN}"
cp "${OUT_DIR}/napper" "${EXT_BIN}/napper"
echo "==> Copied CLI → ${EXT_BIN}/"
