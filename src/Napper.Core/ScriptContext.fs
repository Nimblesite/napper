// [SCRIPT-PROTOCOL] Language-agnostic context protocol shared by every script runtime.
// Napper hands a script its context as JSON (path in NAPPER_CONTEXT) and reads back the
// directives the script emitted (path in NAPPER_RESULT): variables it set, whether it
// failed, a fail message, and any ctx.log lines. A tiny per-language SDK shim — injected
// before the user script runs — turns that protocol into the documented `ctx` object.
// Implements [SCRIPT-PROTOCOL-IN], [SCRIPT-PROTOCOL-OUT], [SCRIPT-CONTEXT], [SCRIPT-SDK].
module Napper.Core.ScriptContext

open System
open System.IO
open System.Text.Json
open Napper.Core

[<Literal>]
let EnvContext = "NAPPER_CONTEXT"

[<Literal>]
let EnvResult = "NAPPER_RESULT"

/// Which lifecycle slot a script occupies. A naplist script step is `Step`; a `.nap`
/// file's `[script] pre`/`post` hooks are `Pre`/`Post`. Only `Post` carries a response.
type Phase =
    | Pre
    | Post
    | Step

/// Everything the runtime hands INTO a script (becomes the `ctx` object).
type Invocation =
    { Phase: Phase
      Vars: Map<string, string>
      Env: string option
      Request: NapRequest option
      Response: NapResponse option }

/// Everything a script hands BACK (parsed from NAPPER_RESULT).
type Directives =
    { Vars: Map<string, string>
      Failed: bool
      FailMessage: string option
      Logs: string list }

let emptyDirectives =
    { Vars = Map.empty
      Failed = false
      FailMessage = None
      Logs = [] }

/// How to launch the runtime with the SDK shim injected, plus the temp files to clean up.
type LaunchPlan =
    { Exe: string
      Args: string
      Env: (string * string) list
      ResultPath: string
      TempFiles: string list }

let stepInvocation (vars: Map<string, string>) (env: string option) : Invocation =
    { Phase = Step
      Vars = vars
      Env = env
      Request = None
      Response = None }

let private phaseName =
    function
    | Pre -> "pre"
    | Post -> "post"
    | Step -> "step"

// ─── Inbound: write the context JSON ────────────────────────── [SCRIPT-PROTOCOL-IN]

let private writeMap (w: Utf8JsonWriter) (name: string) (map: Map<string, string>) =
    w.WriteStartObject(name)

    for kv in map do
        w.WriteString(kv.Key, kv.Value)

    w.WriteEndObject()

let private writeRequest (w: Utf8JsonWriter) (req: NapRequest) =
    w.WriteStartObject("request")
    w.WriteString("method", req.Method.Name)
    w.WriteString("url", req.Url)
    writeMap w "headers" req.Headers

    match req.Body with
    | Some body -> w.WriteString("body", body.Content)
    | None -> w.WriteNull("body")

    w.WriteEndObject()

let private writeResponse (w: Utf8JsonWriter) (resp: NapResponse) =
    w.WriteStartObject("response")
    w.WriteNumber("status", resp.StatusCode)
    writeMap w "headers" resp.Headers
    w.WriteString("body", resp.Body)
    w.WriteNumber("durationMs", int64 resp.Duration.TotalMilliseconds)
    w.WriteEndObject()

let private writeContextFile (inv: Invocation) (path: string) : unit =
    use stream = File.Create(path)
    use w = new Utf8JsonWriter(stream)
    w.WriteStartObject()
    w.WriteString("phase", phaseName inv.Phase)
    w.WriteString("env", inv.Env |> Option.defaultValue "")
    writeMap w "vars" inv.Vars

    match inv.Request with
    | Some req -> writeRequest w req
    | None -> w.WriteNull("request")

    match inv.Response with
    | Some resp -> writeResponse w resp
    | None -> w.WriteNull("response")

    w.WriteEndObject()
    w.Flush()

// ─── Outbound: read the directives JSON ─────────────────────── [SCRIPT-PROTOCOL-OUT]

let private readVars (root: JsonElement) : Map<string, string> =
    match root.TryGetProperty("vars") with
    | true, v when v.ValueKind = JsonValueKind.Object ->
        seq { for p in v.EnumerateObject() -> p.Name, p.Value.GetString() } |> Map.ofSeq
    | _ -> Map.empty

let private readLogs (root: JsonElement) : string list =
    match root.TryGetProperty("logs") with
    | true, l when l.ValueKind = JsonValueKind.Array -> [ for e in l.EnumerateArray() -> e.GetString() ]
    | _ -> []

let readDirectives (path: string) : Directives =
    try
        if File.Exists path then
            use doc = JsonDocument.Parse(File.ReadAllText path)
            let root = doc.RootElement

            let failed =
                match root.TryGetProperty("failed") with
                | true, f -> f.ValueKind = JsonValueKind.True
                | _ -> false

            let failMessage =
                match root.TryGetProperty("failMessage") with
                | true, m when m.ValueKind = JsonValueKind.String -> Some(m.GetString())
                | _ -> None

            { Vars = readVars root
              Failed = failed
              FailMessage = failMessage
              Logs = readLogs root }
        else
            emptyDirectives
    with _ ->
        emptyDirectives

// ─── Per-language SDK shims (injected before the user script) ── [SCRIPT-SDK]
// These are source files in the target language, so they are necessarily one literal
// each — the single authoritative copy of every SDK lives here, bundled in the binary.

[<Literal>]
let private JsPreload =
    """
const fs = require('fs');
const _ctxPath = process.env.NAPPER_CONTEXT;
const _resultPath = process.env.NAPPER_RESULT;
let _inbound = {};
try { if (_ctxPath) _inbound = JSON.parse(fs.readFileSync(_ctxPath, 'utf8')); } catch (e) {}
const _out = { vars: {}, failed: false, failMessage: null, logs: [] };
const _vars = Object.assign({}, _inbound.vars || {});
let _response = null;
if (_inbound.response) {
  const _r = _inbound.response;
  _response = {
    status: _r.status,
    headers: _r.headers || {},
    body: _r.body,
    durationMs: _r.durationMs,
    get json() { try { return JSON.parse(_r.body); } catch (e) { return null; } },
  };
}
const ctx = {
  env: _inbound.env || '',
  vars: _vars,
  request: _inbound.request || null,
  response: _response,
  set(k, v) { _out.vars[String(k)] = String(v); _vars[String(k)] = String(v); },
  fail(m) { _out.failed = true; _out.failMessage = String(m); },
  log(m) { _out.logs.push(String(m)); },
};
globalThis.ctx = ctx;
globalThis.nap = ctx;
process.on('exit', () => { try { if (_resultPath) fs.writeFileSync(_resultPath, JSON.stringify(_out)); } catch (e) {} });
"""

[<Literal>]
let private PyBoot =
    """
import os, sys, json, atexit, builtins, runpy

_ctx_path = os.environ.get('NAPPER_CONTEXT')
_result_path = os.environ.get('NAPPER_RESULT')
_inbound = {}
try:
    if _ctx_path:
        with open(_ctx_path, 'r') as _f:
            _inbound = json.load(_f)
except Exception:
    _inbound = {}
_out = {'vars': {}, 'failed': False, 'failMessage': None, 'logs': []}


class _Response:
    def __init__(self, raw):
        self.status = raw.get('status')
        self.headers = raw.get('headers') or {}
        self.body = raw.get('body')
        self.duration_ms = raw.get('durationMs')

    @property
    def json(self):
        try:
            return json.loads(self.body)
        except Exception:
            return None


class _Ctx:
    def __init__(self, inbound):
        self.env = inbound.get('env', '')
        self.vars = dict(inbound.get('vars') or {})
        self.request = inbound.get('request')
        _r = inbound.get('response')
        self.response = _Response(_r) if _r else None

    def set(self, key, value):
        _out['vars'][str(key)] = str(value)
        self.vars[str(key)] = str(value)

    def fail(self, message):
        _out['failed'] = True
        _out['failMessage'] = str(message)

    def log(self, message):
        _out['logs'].append(str(message))


ctx = _Ctx(_inbound)
builtins.ctx = ctx
builtins.nap = ctx


def _flush():
    if _result_path:
        try:
            with open(_result_path, 'w') as _f:
                json.dump(_out, _f)
        except Exception:
            pass


atexit.register(_flush)

if len(sys.argv) > 1:
    _script = sys.argv[1]
    sys.argv = [_script] + sys.argv[2:]
    runpy.run_path(_script, run_name='__main__')
"""

// ─── Launch plan: inject the right shim by extension ────────── [SCRIPT-SDK], [SCRIPT-DISPATCH]

let private quote (p: string) = "\"" + p + "\""

/// Build the context file + SDK shim and return how to launch the runtime with `ctx`
/// injected. `exe`/`args` are the bare dispatch from [SCRIPT-DISPATCH]; JS prepends a
/// `--require` preload, Python wraps the script in a bootstrap. Other runtimes still get
/// the NAPPER_CONTEXT/NAPPER_RESULT env vars (the SDK for them lands in a later phase).
let prepare (inv: Invocation) (scriptPath: string) (exe: string) (args: string) : LaunchPlan =
    let stamp = Guid.NewGuid().ToString("N")

    let tmp (name: string) =
        Path.Combine(Path.GetTempPath(), $"nap-ctx-{stamp}-{name}")

    let contextPath = tmp "context.json"
    let resultPath = tmp "result.json"
    writeContextFile inv contextPath
    let env = [ EnvContext, contextPath; EnvResult, resultPath ]
    let ext = Path.GetExtension(scriptPath).ToLowerInvariant()

    let withShim (shimName: string) (shimBody: string) (mkArgs: string -> string) =
        let shim = tmp shimName
        File.WriteAllText(shim, shimBody)

        { Exe = exe
          Args = mkArgs shim
          Env = env
          ResultPath = resultPath
          TempFiles = [ shim; contextPath; resultPath ] }

    match ext with
    | ".js"
    | ".mjs"
    | ".cjs" -> withShim "preload.cjs" JsPreload (fun shim -> $"--require {quote shim} {args}")
    | ".py" -> withShim "boot.py" PyBoot (fun shim -> $"{quote shim} {args}")
    | _ ->
        { Exe = exe
          Args = args
          Env = env
          ResultPath = resultPath
          TempFiles = [ contextPath; resultPath ] }

let cleanup (plan: LaunchPlan) : unit =
    for f in plan.TempFiles do
        try
            if File.Exists f then
                File.Delete f
        with _ ->
            ()
