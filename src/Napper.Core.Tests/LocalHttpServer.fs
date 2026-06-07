module LocalHttpServer
// Rich, hermetic, in-process HTTP server — the workhorse for the BULK of black-box
// tests. It deliberately offers a BROAD surface so tests can exercise the tool's
// behaviour deterministically and offline: many status/error codes, several content
// types, variable latency, echoing of method/headers/body, and sized payloads.
//
// This is NOT a replacement for real-world tests. A SMALL number of real-world smoke
// tests (see RealWorldSmokeTests.fs) still hit the genuine public APIs (jsonplaceholder
// etc.) so a real outage or contract break tanks the suite — that is the whole point of
// a real-world test. The rule is: hammer THIS local server as much as you like; make
// only one or two calls per suite, per real API. See CLAUDE.md "Testing".
//
// Routes:
//   GET  /get               -> 200 json   {url, args, headers, origin}            (httpbin-shape)
//   *    /post|/put|/patch|/delete|/anything -> 200 json {method, url, json, data, headers}
//   *    /status/{code}      -> {code} with an empty body
//   *    /delay/{ms}         -> 200 json after sleeping {ms} (latency simulation)
//   GET  /headers            -> 200 json   {headers}
//   GET  /json               -> 200 json   a rich nested document (deep body.* assertions)
//   GET  /html               -> 200 text/html
//   GET  /xml                -> 200 application/xml
//   GET  /bytes/{n}          -> 200 application/octet-stream of {n} bytes
//   GET  /posts/{id}         -> 200 json   {userId,id,title,body}                  (jsonplaceholder-shape)
//   (anything else)          -> 404
//
// `baseUrl` starts the listener on first access (module init is thread-safe) and is
// reused for the whole test-host process. Acceptors run on dedicated threads so the
// listener is immune to thread-pool starvation when the suite spawns many subprocess
// interpreters (node/python/dotnet-script) that block pool threads on WaitForExit.

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading

[<Literal>]
let JsonContentType = "application/json"

[<Literal>]
let private HtmlContentType = "text/html; charset=utf-8"

[<Literal>]
let private XmlContentType = "application/xml"

[<Literal>]
let private OctetContentType = "application/octet-stream"

[<Literal>]
let private StatusPrefix = "/status/"

[<Literal>]
let private PostsPrefix = "/posts/"

[<Literal>]
let private DelayPrefix = "/delay/"

[<Literal>]
let private BytesPrefix = "/bytes/"

[<Literal>]
let private MaxDelayMs = 10_000

[<Literal>]
let private MaxBytes = 65_536

let private serialize (payload: obj) : string = JsonSerializer.Serialize(payload)

/// Echo request headers as a plain string map (httpbin returns the inbound headers).
let private headerMap (req: HttpListenerRequest) : Dictionary<string, string> =
    let d = Dictionary<string, string>()

    for key in req.Headers.AllKeys do
        if not (isNull key) then
            d[key] <- req.Headers[key]

    d

let private readBody (req: HttpListenerRequest) : string =
    use reader = new StreamReader(req.InputStream, req.ContentEncoding)
    reader.ReadToEnd()

/// Parse the request body as JSON, returning null when it is not valid JSON
/// (httpbin sets "json" to null in that case).
let private parsedJson (body: string) : obj =
    if String.IsNullOrWhiteSpace(body) then
        null
    else
        try
            JsonSerializer.Deserialize<JsonElement>(body) |> box
        with _ ->
            null

let private writeText (ctx: HttpListenerContext) (status: int) (contentType: string) (body: string) : unit =
    let bytes = Encoding.UTF8.GetBytes(body)
    ctx.Response.StatusCode <- status
    ctx.Response.ContentType <- contentType
    ctx.Response.ContentLength64 <- int64 bytes.Length
    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
    ctx.Response.OutputStream.Close()

let private writeBytes (ctx: HttpListenerContext) (payload: byte[]) : unit =
    ctx.Response.StatusCode <- 200
    ctx.Response.ContentType <- OctetContentType
    ctx.Response.ContentLength64 <- int64 payload.Length
    ctx.Response.OutputStream.Write(payload, 0, payload.Length)
    ctx.Response.OutputStream.Close()

let private writeJson (ctx: HttpListenerContext) (status: int) (payload: obj) : unit =
    writeText ctx status JsonContentType (serialize payload)

let private writeStatus (ctx: HttpListenerContext) (status: int) : unit =
    ctx.Response.StatusCode <- status
    ctx.Response.ContentLength64 <- 0L
    ctx.Response.OutputStream.Close()

let private echoPayload (req: HttpListenerRequest) (withBody: bool) : obj =
    let d = Dictionary<string, obj>()
    d["method"] <- box req.HttpMethod
    d["url"] <- box (req.Url.ToString())
    d["args"] <- box (Dictionary<string, string>())
    d["headers"] <- box (headerMap req)
    d["origin"] <- box "127.0.0.1"

    if withBody then
        let body = readBody req
        d["data"] <- box body
        d["json"] <- parsedJson body

    box d

/// jsonplaceholder /posts/{id}: a deterministic post whose id echoes the path so
/// `body.id = {{seededId}}` and `ctx.response.json.id` assertions hold for any id.
let private postsPayload (id: int) : obj =
    let d = Dictionary<string, obj>()
    d["userId"] <- box 1
    d["id"] <- box id
    d["title"] <- box "napper test post"
    d["body"] <- box "deterministic body served by the local test server"
    box d

/// A rich nested document for deep body.* assertions (arrays, nested objects, null).
let private sampleJson () : obj =
    let address = Dictionary<string, obj>()
    address["city"] <- box "Testville"
    address["zip"] <- box "12345"
    let d = Dictionary<string, obj>()
    d["id"] <- box 42
    d["name"] <- box "napper"
    d["active"] <- box true
    d["score"] <- (null: obj) // serialises to JSON null, mirroring httpbin's null fields
    d["tags"] <- box [| "alpha"; "beta"; "gamma" |]
    d["address"] <- box address
    box d

let private trailingInt (path: string) (prefix: string) : int option =
    match Int32.TryParse(path.Substring(prefix.Length)) with
    | true, n -> Some n
    | _ -> None

let private htmlBody =
    "<!DOCTYPE html><html><head><title>napper</title></head><body><h1>napper local</h1></body></html>"

let private xmlBody =
    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><note><to>napper</to><body>local xml</body></note>"

let private isEcho (path: string) : bool =
    [ "/post"; "/put"; "/patch"; "/delete"; "/anything" ] |> List.contains path

let private route (ctx: HttpListenerContext) : unit =
    let req = ctx.Request
    let path = req.Url.AbsolutePath

    if path = "/get" then
        writeJson ctx 200 (echoPayload req false)
    elif isEcho path then
        writeJson ctx 200 (echoPayload req true)
    elif path = "/headers" then
        let d = Dictionary<string, obj>()
        d["headers"] <- box (headerMap req)
        writeJson ctx 200 (box d)
    elif path = "/json" then
        writeJson ctx 200 (sampleJson ())
    elif path = "/html" then
        writeText ctx 200 HtmlContentType htmlBody
    elif path = "/xml" then
        writeText ctx 200 XmlContentType xmlBody
    elif path.StartsWith(BytesPrefix) then
        match trailingInt path BytesPrefix with
        | Some n -> writeBytes ctx (Array.init (min n MaxBytes) (fun i -> byte (i % 256)))
        | None -> writeStatus ctx 400
    elif path.StartsWith(DelayPrefix) then
        match trailingInt path DelayPrefix with
        | Some ms ->
            Thread.Sleep(min ms MaxDelayMs)
            let d = Dictionary<string, obj>()
            d["delayedMs"] <- box (min ms MaxDelayMs)
            writeJson ctx 200 (box d)
        | None -> writeStatus ctx 400
    elif path.StartsWith(StatusPrefix) then
        match trailingInt path StatusPrefix with
        | Some code -> writeStatus ctx code
        | None -> writeStatus ctx 400
    elif path.StartsWith(PostsPrefix) then
        match trailingInt path PostsPrefix with
        | Some id -> writeJson ctx 200 (postsPayload id)
        | None -> writeStatus ctx 404
    else
        writeStatus ctx 404

let private handle (ctx: HttpListenerContext) : unit =
    try
        route ctx
    with _ ->
        try
            writeStatus ctx 500
        with _ ->
            ()

/// Reserve an ephemeral loopback port, then hand it to the HttpListener.
let private freePort () : int =
    let probe = new TcpListener(IPAddress.Loopback, 0)
    probe.Start()
    let port = (probe.LocalEndpoint :?> IPEndPoint).Port
    probe.Stop()
    port

[<Literal>]
let private AcceptorCount = 24

/// One acceptor: block on GetContext and handle inline, forever. Runs on a DEDICATED
/// thread (not the thread pool) because the full suite saturates the pool with
/// subprocess WaitForExit calls — a starved pool would delay accepts and surface as
/// dropped/refused connections, i.e. flaky request failures.
let private acceptLoop (listener: HttpListener) () : unit =
    let mutable running = true

    while running do
        try
            handle (listener.GetContext())
        with
        | :? HttpListenerException -> running <- false
        | :? ObjectDisposedException -> running <- false
        | _ -> ()

let private startServer () : string =
    let port = freePort ()
    let listener = new HttpListener()
    listener.Prefixes.Add($"http://127.0.0.1:{port}/")
    listener.Start()

    for i in 1..AcceptorCount do
        let t = Thread(ThreadStart(acceptLoop listener))
        t.IsBackground <- true
        t.Name <- $"local-http-acceptor-{i}"
        t.Start()

    $"http://127.0.0.1:{port}"

/// Base URL (no trailing slash). Forces the listener to start on first reference.
let baseUrl: string = startServer ()
