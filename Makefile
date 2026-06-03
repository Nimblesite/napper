# =============================================================================
# Standard Makefile — Napper
# =============================================================================
# agent-pmo:74cf183

.PHONY: build test lint fmt clean ci setup package-vsix test-fsharp build-zed stamp

# --- Cross-platform support ---
ifeq ($(OS),Windows_NT)
  SHELL      := powershell.exe
  .SHELLFLAGS := -NoProfile -Command
  _RM        = Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
  _MKDIR     = New-Item -ItemType Directory -Force
  HOME       ?= $(USERPROFILE)
else
  SHELL      := /usr/bin/env bash
  .SHELLFLAGS := -euo pipefail -c
  _RM        = rm -rf
  _MKDIR     = mkdir -p
endif

# --- Platform detection for .NET RID and Shipwright/vsce target ---
ifeq ($(OS),Windows_NT)
  _NAP_RID     ?= win-x64
  _DTK_PLATFORM := win32-x64
else
  _ARCH    := $(shell uname -m)
  _UNAME_S := $(shell uname -s)
  ifeq ($(_UNAME_S),Darwin)
    ifeq ($(filter arm64,$(_ARCH)),arm64)
      _NAP_RID     ?= osx-arm64
      _DTK_PLATFORM := darwin-arm64
    else
      _NAP_RID     ?= osx-x64
      _DTK_PLATFORM := darwin-x64
    endif
  else
    _NAP_RID     ?= linux-x64
    _DTK_PLATFORM := linux-x64
  endif
endif

_EXT_BIN     := src/Napper.VsCode/bin/$(_DTK_PLATFORM)
_LOG_DIR     := .commandtree/logs
_COV         := coverage
_FSHARP_COV  := $(_COV)/fsharp
_DOTHTTP_COV := $(_COV)/dothttp
_LSP_COV     := $(_COV)/lsp
_TS_COV      := $(_COV)/typescript
_RUST_COV    := $(_COV)/rust

# Runs dotnet test + reportgenerator for one project.
# $(1)=project dir  $(2)=coverage dir  $(3)=log name
define _dotnet_test
	$(_RM) "$(2)" && $(_MKDIR) "$(2)"
	dotnet test $(1) --nologo \
	  --settings $(1)/coverage.runsettings \
	  --results-directory "$(2)/raw" \
	  --logger "console;verbosity=detailed" \
	  -- RunConfiguration.FailFastEnabled=true \
	  2>&1 | tee "$(_LOG_DIR)/$(3).log"
	reportgenerator \
	  -reports:"$(2)/raw/*/coverage.cobertura.xml" \
	  -targetdir:"$(2)/report" \
	  -reporttypes:"Html;TextSummary;Cobertura;lcov"
endef

# Checks one coverage result against coverage-thresholds.json.
# Exits non-zero immediately (FAIL FAST) if coverage < threshold.
# Ratchets threshold up to floor(coverage)-1 when coverage improves.
# $(1)=project key  $(2)=summary file  $(3)=label
define _cov_check
	@{ \
	  t=$$(jq -r '.projects["$(1)"].threshold // .default_threshold' coverage-thresholds.json); \
	  if [ -f "$(2)" ]; then \
	    c=$$(awk '/Line coverage:/ {gsub(/%/,""); print $$3}' "$(2)" 2>/dev/null || echo "0"); \
	    echo "  $(3): $${c}% (threshold $${t}%)"; \
	    if [ $$(echo "$${c} < $${t}" | bc -l) -eq 1 ]; then \
	      echo "  *** FAIL: $(3) coverage $${c}% is below threshold $${t}% — ABORTING ***"; \
	      exit 1; \
	    fi; \
	    new_t=$$(( $$(echo "scale=0; $${c}/1" | bc) - 1 )); \
	    if [ $$(echo "$${new_t} > $${t}" | bc -l) -eq 1 ]; then \
	      tmp=$$(mktemp); \
	      jq --argjson nt "$${new_t}" '.projects["$(1)"].threshold = $$nt' coverage-thresholds.json > "$${tmp}" && mv "$${tmp}" coverage-thresholds.json; \
	      echo "  RATCHET: $(3) threshold -> $${new_t}%"; \
	    else \
	      echo "  OK"; \
	    fi; \
	  else echo "  $(3): no data (skipping)"; fi; \
	}
endef

# =============================================================================
# Standard Targets
#
# The 7 portfolio-wide targets. See REPO-STANDARDS-SPEC [MAKE-TARGETS].
# Repo-specific targets live in their own section below.
# =============================================================================

# build: compile/assemble all shippable artifacts (CLI native binary + extension bundle).
build: _build_cli _build_extension

test: _test_fsharp _test_rust _test_vsix _coverage_check

lint:
	dotnet build --nologo -warnaserror
	cd src/Napper.VsCode && npm run lint
	cargo clippy --manifest-path src/Napper.Zed/Cargo.toml

fmt:
	dotnet fantomas src/
	cd src/Napper.VsCode && npx prettier --write "src/**/*.ts"
	cargo fmt --manifest-path src/Napper.Zed/Cargo.toml

clean:
	$(_RM) out/ $(_COV)/
	$(_RM) src/Napper.Core/bin/ src/Napper.Core/obj/
	$(_RM) src/Napper.Cli/bin/ src/Napper.Cli/obj/
	$(_RM) src/Napper.VsCode/bin/ src/Napper.VsCode/dist/ src/Napper.VsCode/out/
	$(_RM) src/Napper.VsCode/*.vsix

ci: lint test build

setup:
	dotnet tool restore && dotnet restore
	cd src/Napper.VsCode && npm ci
	cd website && npm ci
	rustup component add clippy rustfmt 2>/dev/null || true
	dotnet tool install --global dotnet-reportgenerator-globaltool 2>/dev/null || true

# stamp: write a release version into every source version carrier
# (Directory.Build.props, the extension package.json, and shipwright.json) using
# structured parsers. Implements [SWR-VERSION-BUILD-STAMPING]. Source stays at
# 0.0.0-dev; only the release/runner working tree is stamped — never committed.
#   make stamp VERSION=1.2.3      (or)      make stamp TAG=v1.2.3
stamp:
	dotnet fsi scripts/stamp-version.fsx $(if $(TAG),--tag $(TAG),--version $(VERSION))

# =============================================================================
# Repo-Specific Targets
#
# Specific to this repo; NOT part of the standard 7. Preserved during
# remediation per REPO-STANDARDS-SPEC [MAKE-TARGETS].
# =============================================================================

# package-vsix: build then package the platform VSIX and verify its contents.
package-vsix: clean build
	cd src/Napper.VsCode && npx @vscode/vsce package --no-dependencies --skip-license --target $(_DTK_PLATFORM)
	@VSIX=$$(ls src/Napper.VsCode/*.vsix 2>/dev/null | head -1); \
	  [ -n "$$VSIX" ] || { echo "ERROR: no VSIX file found"; exit 1; }; \
	  echo "==> Verifying VSIX contents: $$VSIX"; \
	  unzip -l "$$VSIX" > /tmp/vsix-contents.txt; \
	  grep -q "shipwright.json" /tmp/vsix-contents.txt || { echo "ERROR: shipwright.json missing from VSIX"; exit 1; }; \
	  grep -q "bin/$(_DTK_PLATFORM)/napper" /tmp/vsix-contents.txt || { echo "ERROR: bin/$(_DTK_PLATFORM)/napper missing from VSIX"; exit 1; }; \
	  echo "  shipwright.json: OK"; \
	  echo "  bin/$(_DTK_PLATFORM)/napper: OK"; \
	  echo "==> VSIX packaged and verified"

# test-fsharp: F#-only test subset (consumed by CI's F# coverage step).
test-fsharp: _test_fsharp

# build-zed: build the Zed extension wasm (requires the tree-sitter CLI).
build-zed:
	@command -v cargo &>/dev/null || { echo "ERROR: cargo not found"; exit 1; }
	@command -v tree-sitter &>/dev/null || { echo "ERROR: tree-sitter not found"; exit 1; }
	@if ! rustup target list --installed 2>/dev/null | grep -q wasm32-wasi; then \
	  rustup target add wasm32-wasip1; \
	fi
	@for g in nap naplist napenv; do \
	  (cd src/Napper.Zed/grammars/tree-sitter-$$g && tree-sitter generate); \
	done
	cd src/Napper.Zed && cargo build --release --target wasm32-wasip1
	cd src/Napper.Zed && cargo clippy --target wasm32-wasip1

# =============================================================================
# Private helpers
# =============================================================================

# NativeAOT publish per [CLI-AOT-MIGRATION]: a single statically-linked native
# binary per RID, zero runtime deps. The LSP (napper lsp) ships inside it.
_build_cli:
	dotnet publish src/Napper.Cli/Napper.Cli.fsproj \
	  -r "$(_NAP_RID)" \
	  -p:PublishAot=true \
	  -o "out/$(_NAP_RID)" --nologo
	@$(_MKDIR) "$(_EXT_BIN)"
	cp "out/$(_NAP_RID)/napper" "$(_EXT_BIN)/napper"
	chmod +x "$(_EXT_BIN)/napper"
	@# Verify the AOT binary honors the version contract [SWR-VERSION-CLI-OUTPUT].
	@# Glob-match the plain text output — never regex/sed over the props XML.
	@ACTUAL=$$("out/$(_NAP_RID)/napper" --version); \
	case "$$ACTUAL" in \
	  "napper "?*) echo "  napper --version: $$ACTUAL" ;; \
	  *) echo "ERROR: bad --version output: '$$ACTUAL' (expected 'napper <semver>')"; exit 1 ;; \
	esac

_build_extension:
	cd src/Napper.VsCode && npm ci && npx webpack --mode production

_test_fsharp:
	$(_MKDIR) "$(_LOG_DIR)"
	$(call _dotnet_test,src/Napper.Core.Tests,$(_FSHARP_COV),test-fsharp-core)
	$(call _dotnet_test,src/DotHttp.Tests,$(_DOTHTTP_COV),test-dothttp)
	$(call _dotnet_test,src/Napper.Lsp.Tests,$(_LSP_COV),test-lsp)

_test_rust:
	$(_MKDIR) "$(_LOG_DIR)" "$(_RUST_COV)"
	cd src/Napper.Zed && cargo tarpaulin \
	  --out html lcov xml \
	  --output-dir "../../$(_RUST_COV)/report" \
	  --skip-clean 2>&1 | tee "../../$(_LOG_DIR)/test-rust.log"

_test_vsix: _build_cli _build_extension
	$(_MKDIR) "$(_LOG_DIR)" "$(_TS_COV)"
	cd src/Napper.VsCode && npm run compile && npm run compile:tests
	cd src/Napper.VsCode && NODE_V8_COVERAGE="../../$(_TS_COV)/tmp" \
	  npx mocha out/test/unit/**/*.test.js --ui tdd --timeout 5000 \
	  2>&1 | tee "../../$(_LOG_DIR)/test-vsix-unit.log"
	cd src/Napper.VsCode && NODE_V8_COVERAGE="../../$(_TS_COV)/tmp" \
	  npx vscode-test 2>&1 | tee "../../$(_LOG_DIR)/test-vsix-e2e.log"
	cd src/Napper.VsCode && npx c8 report \
	  --temp-directory "../../$(_TS_COV)/tmp" \
	  --report-dir "../../$(_TS_COV)/report" \
	  --reporter html --reporter text --reporter lcov \
	  2>&1 | tee "../../$(_LOG_DIR)/test-vsix-coverage.log"

_coverage_check:
	@echo "==> Coverage check..."
	$(call _cov_check,src/Napper.Core.Tests,$(_FSHARP_COV)/report/Summary.txt,Napper.Core)
	$(call _cov_check,src/DotHttp.Tests,$(_DOTHTTP_COV)/report/Summary.txt,DotHttp)
	$(call _cov_check,src/Napper.Lsp.Tests,$(_LSP_COV)/report/Summary.txt,Napper.Lsp)
	@{ \
	  t=$$(jq -r '.projects["src/Napper.Zed"].threshold // .default_threshold' coverage-thresholds.json); \
	  if [ -f "$(_RUST_COV)/report/cobertura.xml" ]; then \
	    lr=$$(sed -n 's/.*line-rate="\([0-9.]*\)".*/\1/p' "$(_RUST_COV)/report/cobertura.xml" | head -1); \
	    c=$$(echo "$${lr:-0} * 100" | bc -l | xargs printf "%.1f"); \
	    echo "  Rust: $${c}% (threshold $${t}%)"; \
	    if [ $$(echo "$${c} < $${t}" | bc -l) -eq 1 ]; then \
	      echo "  *** FAIL: Rust coverage $${c}% is below threshold $${t}% — ABORTING ***"; \
	      exit 1; \
	    fi; \
	    new_t=$$(( $$(echo "scale=0; $${c}/1" | bc) - 1 )); \
	    if [ $$(echo "$${new_t} > $${t}" | bc -l) -eq 1 ]; then \
	      tmp=$$(mktemp); \
	      jq --argjson nt "$${new_t}" '.projects["src/Napper.Zed"].threshold = $$nt' coverage-thresholds.json > "$${tmp}" && mv "$${tmp}" coverage-thresholds.json; \
	      echo "  RATCHET: Rust threshold -> $${new_t}%"; \
	    else \
	      echo "  OK"; \
	    fi; \
	  else echo "  Rust: no data (skipping)"; fi; \
	}
	@{ \
	  t=$$(jq -r '.projects["src/Napper.VsCode"].threshold // .default_threshold' coverage-thresholds.json); \
	  if [ -f "$(_TS_COV)/report/index.html" ]; then \
	    c=$$(cd src/Napper.VsCode && npx c8 report --reporter text 2>/dev/null | grep 'All files' | awk '{print $$4}' | tr -d '%' || echo "0"); \
	    echo "  TypeScript: $${c}% (threshold $${t}%)"; \
	    if [ $$(echo "$${c} < $${t}" | bc -l) -eq 1 ]; then \
	      echo "  *** FAIL: TypeScript coverage $${c}% is below threshold $${t}% — ABORTING ***"; \
	      exit 1; \
	    fi; \
	    new_t=$$(( $$(echo "scale=0; $${c}/1" | bc) - 1 )); \
	    if [ $$(echo "$${new_t} > $${t}" | bc -l) -eq 1 ]; then \
	      tmp=$$(mktemp); \
	      jq --argjson nt "$${new_t}" '.projects["src/Napper.VsCode"].threshold = $$nt' coverage-thresholds.json > "$${tmp}" && mv "$${tmp}" coverage-thresholds.json; \
	      echo "  RATCHET: TypeScript threshold -> $${new_t}%"; \
	    else \
	      echo "  OK"; \
	    fi; \
	  else echo "  TypeScript: no data (skipping)"; fi; \
	}
	@echo "==> Coverage OK"
