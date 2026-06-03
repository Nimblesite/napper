#!/usr/bin/env bash
set -euo pipefail

# Build the Napper CLI and copy it into the VSCode extension bin directory.
# Called from src/Napper.VsCode via: bash ../../scripts/build-cli.sh

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXT_BIN="${REPO_ROOT}/src/Napper.VsCode/bin"

ARCH="$(uname -m)"
OS="$(uname -s)"

# RID is the .NET runtime identifier (osx-*/linux-*); NODE_PLATFORM is the
# `${process.platform}-${process.arch}` string the extension's resolver uses to find
# the bundled binary (bin/<NODE_PLATFORM>/napper — see src/binaryUtils.ts). They differ
# on macOS (osx-arm64 vs darwin-arm64), so we MUST stage under the NODE_PLATFORM name.
case "${OS}" in
  Darwin)
    case "${ARCH}" in
      arm64)  RID="osx-arm64"; NODE_PLATFORM="darwin-arm64" ;;
      x86_64) RID="osx-x64";   NODE_PLATFORM="darwin-x64" ;;
      *)      echo "Unsupported arch: ${ARCH}" >&2; exit 1 ;;
    esac
    ;;
  Linux)
    case "${ARCH}" in
      x86_64)        RID="linux-x64";   NODE_PLATFORM="linux-x64" ;;
      aarch64|arm64) RID="linux-arm64"; NODE_PLATFORM="linux-arm64" ;;
      *)             echo "Unsupported arch: ${ARCH}" >&2; exit 1 ;;
    esac
    ;;
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

# PRIMARY: stage under the platform sub-dir the extension's bundled-binary resolver
# (bundledBinaryPath) and Shipwright look for — the SAME layout the shipped per-platform
# VSIX uses. This is what makes the extension + e2e tests resolve the REAL bundled binary.
PLATFORM_BIN="${EXT_BIN}/${NODE_PLATFORM}"
mkdir -p "${PLATFORM_BIN}"
cp "${OUT_DIR}/napper" "${PLATFORM_BIN}/napper"
chmod +x "${PLATFORM_BIN}/napper"
echo "==> Staged CLI → ${PLATFORM_BIN}/napper"

# SECONDARY: also keep a flat copy so tooling that resolves `napper` on PATH keeps working
# (the CI Shipwright version-contract gate adds bin/ to PATH and runs `napper --version`).
cp "${OUT_DIR}/napper" "${EXT_BIN}/napper"
chmod +x "${EXT_BIN}/napper"
echo "==> Staged CLI (flat, for PATH) → ${EXT_BIN}/napper"
