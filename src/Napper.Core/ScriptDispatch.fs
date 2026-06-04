// [script-dispatch] Single source of truth: script file extension -> runtime invocation.
// Both the .naplist step classifier (Parser) and the script executor (Runner) consult this one
// table — there is exactly one place that knows which extensions are scripts and how to run them.
// Adding a language is a single row here.
// Implements [script-dispatch], [script-runtime], [script-fsx], [script-csx], [script-js], [script-py]
module Napper.Core.ScriptDispatch

[<Literal>]
let RuntimeDotnet = "dotnet"

[<Literal>]
let RuntimeNode = "node"

[<Literal>]
let RuntimePython = "python3"

/// Maps a lowercase file extension to (executable, argument prefix placed before the quoted script path).
let table: Map<string, string * string> =
    Map
        [ ".fsx", (RuntimeDotnet, "fsi")
          ".csx", (RuntimeDotnet, "script")
          ".js", (RuntimeNode, "")
          ".mjs", (RuntimeNode, "")
          ".cjs", (RuntimeNode, "")
          ".py", (RuntimePython, "") ]

let private extensionOf (path: string) : string =
    System.IO.Path.GetExtension(path).ToLowerInvariant()

/// True when the path's extension has a registered script runtime — i.e. it is a script step, not a .nap file.
let isScript (path: string) : bool = table.ContainsKey(extensionOf path)

/// Resolve (executable, arguments) for a script path, or an actionable error if the extension is unsupported.
let resolve (scriptPath: string) : Result<string * string, string> =
    let ext = extensionOf scriptPath

    match Map.tryFind ext table with
    | Some(exe, argPrefix) ->
        let quoted = $"\"{scriptPath}\""
        Ok(exe, (if argPrefix = "" then quoted else $"{argPrefix} {quoted}"))
    | None ->
        let supported = table |> Map.toList |> List.map fst |> String.concat ", "
        Error $"No runtime registered for script extension '{ext}' ({scriptPath}). Supported: {supported}"
