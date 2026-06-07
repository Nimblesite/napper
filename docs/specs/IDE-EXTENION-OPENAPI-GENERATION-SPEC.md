# OpenAPI Test Generation — IDE Extension

> Extension-side integration for OpenAPI import and AI-assisted enrichment. The deterministic generator is the CLI's ([OPENAPI-GENERATE](./CLI-OPENAPI-GENERATION.md)); this spec covers the VSIX surface on top of it.

---

## [VSCODE-OPENAPI] Import surface

> **Status: Implemented.** `openApiImport.ts` + `openApiDownloader.ts` (URL fetch) + `openApiAiEnhancer.ts`.

The import command appears in the Nap explorer panel title bar (cloud-download icon) and the Command Palette.

### [VSCODE-OPENAPI-IMPORT] Import command

> **Status: Implemented** (`nap.importOpenApi`, registered in `editAndImportCommands.ts`).

1. User picks a spec file (JSON) or pastes a URL.
2. User picks an output folder.
3. The generator runs and writes files.
4. The generated `.naplist` opens in the editor.
5. A success notification shows the file count.

YAML input is not yet supported (matches the CLI's [OPENAPI-YAML](./CLI-OPENAPI-GENERATION.md) gap).

### [VSCODE-OPENAPI-AI] AI-assisted enrichment (Copilot)

> **Status: Implemented (code), test gap.** `openApiAiEnhancer.ts` (pure functions, no VS Code SDK) exists and is wired into `openApiImport.ts` via the VS Code Language Model API. There are **no unit tests for `openApiAiEnhancer.ts`** yet — that is the outstanding work for this section.

AI enrichment is an **optional layer** on top of the deterministic generator. The generator always works without Copilot; when Copilot is available and the user opts in, the output is enriched.

**Flow:** deterministic generator produces the base `GenerationResult` → if the user chooses "Generate with AI enhancement" and `vscode.lm.selectChatModels()` returns a model, the enricher sends batched prompts (grouped by tag, to respect rate limits) → responses are parsed and merged → enriched files are written.

| Area | Without AI | With AI |
|------|-----------|---------|
| Assertions | `status`, `body.field exists` | Semantic: format/range/relationship checks |
| Request bodies | Schema-derived defaults | Realistic emails, names, dates, UUIDs |
| Error cases | One per documented status, placeholder input | Inputs that actually trigger each error |
| Playlist ordering | File-sort | Logical flow: auth → create → read → CRUD lifecycle |
| Validation scripts | none | `.fsx` scripts for complex nested validation |

**Module split:** `openApiAiEnhancer.ts` is pure (input: `GenerationResult` + parsed `OpenApiSpec` + LLM responses; output: enriched `GenerationResult`; fully testable without VS Code). The extension layer in `openApiImport.ts` checks Copilot availability, presents the "Generate" vs "Generate with AI" choice, sends prompts, and shows progress.

**Future (paid):** a standalone Nap agent that maintains test suites outside VS Code; continuous generation that watches spec changes; AI-driven test prioritization by API-change impact.

---

## Related specs

- [OpenAPI Generation (CLI)](./CLI-OPENAPI-GENERATION.md) — the deterministic generator and its status
- [IDE Extension Spec](./IDE-EXTENSION-SPEC.md) — overall extension surface
