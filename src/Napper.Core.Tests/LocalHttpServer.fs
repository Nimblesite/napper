module LocalHttpServer
// Hermetic, in-process HTTP server that impersonates the third-party services the
// black-box tests used to hit (httpbin.org, jsonplaceholder.typicode.com). Those
// services routinely flake/outage and abort the whole suite (stopOnFail), which is
// why unrelated modules showed phantom-low coverage. This server serves the SAME
// deterministic payloads with all the trimmings, on loopback, with zero network.
//
// Routes (mirroring the originals the tests depend on):
//   GET  /get            -> 200 application/json  {url, args, headers, origin}   (httpbin)
//   *    /post|/anything -> 200 application/json  {url, json, data, headers}     (httpbin)
//   *    /status/{code}  -> {code} with empty body                                (httpbin)
//   GET  /headers        -> 200 application/json  {headers}                       (httpbin)
//   GET  /posts/{id}     -> 200 application/json  {userId,id,title,body}          (jsonplaceholder)
//
// `baseUrl` starts the listener on first access (module init is thread-safe) and
// is reused for the whole test-host process lifetime. Black-box callers (the napper
// CLI subprocess) and in-process callers (Runner.runNapFile) both reach 127.0.0.1.

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Text.Json

[<Literal>]
let JsonContentType = "application/json"

[<Literal>]
let private StatusPrefix = "/status/"

[<Literal>]
let private PostsPrefix = "/posts/"

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

let private writeJson (ctx: HttpListenerContext) (status: int) (payload: obj) : unit =
    let bytes = Encoding.UTF8.GetBytes(serialize payload)
    ctx.Response.StatusCode <- status
    ctx.Response.ContentType <- JsonContentType
    ctx.Response.ContentLength64 <- int64 bytes.Length
    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
    ctx.Response.OutputStream.Close()

let private writeStatus (ctx: HttpListenerContext) (status: int) : unit =
    ctx.Response.StatusCode <- status
    ctx.Response.ContentLength64 <- 0L
    ctx.Response.OutputStream.Close()

let private getPayload (req: HttpListenerRequest) : obj =
    let d = Dictionary<string, obj>()
    d["url"] <- box (req.Url.ToString())
    d["args"] <- box (Dictionary<string, string>())
    d["headers"] <- box (headerMap req)
    d["origin"] <- box "127.0.0.1"
    box d

let private postPayload (req: HttpListenerRequest) : obj =
    let body = readBody req
    let d = Dictionary<string, obj>()
    d["url"] <- box (req.Url.ToString())
    d["data"] <- box body
    d["json"] <- parsedJson body
    d["headers"] <- box (headerMap req)
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

let private trailingInt (path: string) (prefix: string) : int option =
    match Int32.TryParse(path.Substring(prefix.Length)) with
    | true, n -> Some n
    | _ -> None

let private route (ctx: HttpListenerContext) : unit =
    let req = ctx.Request
    let path = req.Url.AbsolutePath

    if path = "/get" then
        writeJson ctx 200 (getPayload req)
    elif path = "/headers" then
        let d = Dictionary<string, obj>()
        d["headers"] <- box (headerMap req)
        writeJson ctx 200 (box d)
    elif path = "/post" || path = "/anything" then
        writeJson ctx 200 (postPayload req)
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
        let t = System.Threading.Thread(System.Threading.ThreadStart(acceptLoop listener))
        t.IsBackground <- true
        t.Name <- $"local-http-acceptor-{i}"
        t.Start()

    $"http://127.0.0.1:{port}"

/// Base URL (no trailing slash). Forces the listener to start on first reference.
let baseUrl: string = startServer ()
