// Implements [LSP-CUSTOM]
/// Generate curl commands from parsed NapRequest data.
/// Shared by CLI and LSP — no IDE-specific code.
module Napper.Core.CurlGenerator

open Napper.Core

let private escapeShellArg (s: string) : string = s.Replace("'", "'\\''")

let private headerFlag (key: string) (value: string) : string =
    $" -H '{escapeShellArg key}: {escapeShellArg value}'"

let private bodyFlag (body: RequestBody) : string = $" -d '{escapeShellArg body.Content}'"

/// Generate a curl command string from a NapRequest
let toCurl (request: NapRequest) : string =
    let sb = System.Text.StringBuilder()

    sb.Append($"curl -X {request.Method.Name} '{escapeShellArg request.Url}'")
    |> ignore

    request.Headers |> Map.iter (fun k v -> sb.Append(headerFlag k v) |> ignore)

    request.Body
    |> Option.iter (fun b ->
        sb.Append($" -H 'Content-Type: {escapeShellArg b.ContentType}'") |> ignore
        sb.Append(bodyFlag b) |> ignore)

    sb.ToString()
