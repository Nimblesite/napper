# IDE Extension — CLI Install Plan

Implements [`vscode-cli-acquisition`](../specs/IDE-EXTENSION-SPEC.md#vscode-cli-acquisition).

**Canonical references (read these, don't duplicate them):**
- [Shipwright product repo adoption guide](https://github.com/MelbourneDeveloper/deployment_toolkit/blob/main/docs/agents/product-repo-adoption-guide.md)
- [Shipwright VSIX platform bundling spec](https://github.com/MelbourneDeveloper/deployment_toolkit/blob/main/docs/specs/vsix-platform-bundling.md)

---

## Approach

CLI resolution is handled by `@nimblesite/shipwright-vscode` (`activateDeploymentToolkit`) reading `shipwright.json`. The bespoke installer (cliResolver.ts, cliResolverUi.ts, cliResolverCommands.ts, cliInstaller.ts) has been deleted. Do not re-introduce it.

One install gives you both CLI and LSP. The LSP is `napper lsp` — the same binary, no second discovery ([`lsp-one-binary`](../specs/LSP-SPEC.md#lsp-one-binary)).

---

## VSIX Packaging

Per [SWR-VSIX-CI-MATRIX] and [SWR-VSIX-PACKAGE], we build **6 per-platform VSIXes**:

| Platform | Runner | vsceTarget | npm_config_arch |
|----------|--------|------------|-----------------|
| darwin-arm64 | macos-15 | darwin-arm64 | arm64 |
| darwin-x64 | macos-13 | darwin-x64 | x64 |
| linux-x64 | ubuntu-latest | linux-x64 | x64 |
| linux-arm64 | ubuntu-24.04-arm | linux-arm64 | arm64 |
| win32-x64 | windows-latest | win32-x64 | x64 |
| win32-arm64 | windows-11-arm | win32-arm64 | arm |

Each leg builds the **NativeAOT** binary on a runner whose OS+arch matches the target (NativeAOT
cannot cross-compile across OS/arch) and bundles it at `bin/${platform}/napper[.exe]`. The
Marketplace delivers the correct VSIX automatically.

Local dev: `make package-vsix` builds a single-platform VSIX for the current machine only.

---

## No NuGet / dotnet-tool deployment

`napper` deploys **only** as a self-contained NativeAOT native binary — via GitHub Releases
(consumed by Homebrew, Scoop, and `install.sh`/`install.ps1`) and bundled inside each per-platform
VSIX. There is **no `dotnet tool` / NuGet channel and no `publish-nuget` job**: a dotnet tool would
force end users to install the .NET runtime ([`cli-aot-migration`](../specs/CLI-SPEC.md#cli-aot-migration)).
The Shipwright resolution chain is `user-setting → env → bundled` only — `path` and `dotnet-tool`
are not startup sources ([SWR-IDE-RESOLUTION]).

---

## TODO

### Spec & release prerequisites
- [x] [`vscode-cli-acquisition`](../specs/IDE-EXTENSION-SPEC.md#vscode-cli-acquisition) updated to reference Shipwright approach
- [x] `@nimblesite/shipwright-vscode` wired in `extension.ts`
- [x] Bespoke installer files deleted (cliResolver.ts, cliResolverUi.ts, cliResolverCommands.ts, cliInstaller.ts)
- [x] `shipwright.json` present with correct `bundlePath` and `perPlatformArtifact: true`
- [x] `shipwright.json` `product.version` + `expectedVersion` are `0.0.0-dev` in source, stamped from the tag by `scripts/stamp-version.fsx` ([SWR-VERSION-BUILD-STAMPING])
- [x] `shipwright.json` `sources` are `user-setting → env → bundled` only (no `path` / `dotnet-tool`) per [SWR-IDE-RESOLUTION]
- [x] `shipwright.json` platforms list includes all 6: darwin-arm64, darwin-x64, linux-x64, linux-arm64, win32-x64, win32-arm64
- [x] Release CI builds 6 per-platform NativeAOT VSIXes (darwin-arm64, darwin-x64, linux-x64, linux-arm64, win32-x64, win32-arm64)
- [x] Release CI uses platform-native runners and `npm_config_arch` per [SWR-VSIX-CI-MATRIX]
- [x] Release CI stamps every version carrier from the tag; each build leg verifies the native binary reports `napper <version>`
- [x] `publish-marketplace` job publishes all 6 VSIXes atomically per [SWR-VSIX-PUBLISH]
- [x] `engines.vscode` set to `^1.99.0` per [SWR-VSIX-PACKAGE]
- [x] [DTK-NAPPER-VSCODE-RESOLVER] Complete — Shipwright replaces bespoke resolver
- [ ] Tag `v0.12.0` to exercise the full release pipeline end-to-end

### Testing
- [x] Unit test: `product.version` is resolved semver matching `package.json` version
- [x] Unit test: `expectedVersion` is resolved semver matching `product.version`
- [x] VSIX content verification in `make package-vsix` per [SWR-VSIX-VERIFY]: checks `shipwright.json` and `bin/${platform}/napper` present
- [x] Release CI VSIX content verification step per [SWR-VSIX-VERIFY]
- [ ] E2E test: install VSIX, assert Shipwright resolves bundled binary (source = `bundled`), assert `napper.runFile` succeeds against a real `.nap` fixture
