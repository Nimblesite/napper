module CurlGeneratorTests
// Covers Napper.Core.CurlGenerator.toCurl — shared curl rendering for CLI + LSP.
// Tests [NAP-METHODS], [LSP-CUSTOM] — curl verb rendering and POSIX single-quote shell escaping.

open Xunit
open Napper.Core

let private req method url headers body : NapRequest =
    { Method = method
      Url = url
      Headers = headers
      Body = body }

[<Fact>]
let ``toCurl renders bare GET with method and quoted url`` () =
    let c = CurlGenerator.toCurl (req GET "https://example.com" Map.empty None)
    Assert.Equal("curl -X GET 'https://example.com'", c)

[<Fact>]
let ``toCurl renders every HTTP verb`` () =
    for m in [ GET; POST; PUT; PATCH; DELETE; HEAD; OPTIONS ] do
        let c = CurlGenerator.toCurl (req m "https://e.com" Map.empty None)
        Assert.StartsWith($"curl -X {m.Name} 'https://e.com'", c)

[<Fact>]
let ``toCurl includes a single header as -H flag`` () =
    let c =
        CurlGenerator.toCurl (req GET "https://e.com" (Map.ofList [ "Accept", "application/json" ]) None)

    Assert.Contains("-H 'Accept: application/json'", c)

[<Fact>]
let ``toCurl includes every header when there are multiple`` () =
    let headers = Map.ofList [ "Accept", "application/json"; "X-Token", "abc123" ]
    let c = CurlGenerator.toCurl (req GET "https://e.com" headers None)
    Assert.Contains("-H 'Accept: application/json'", c)
    Assert.Contains("-H 'X-Token: abc123'", c)

[<Fact>]
let ``toCurl with body emits content-type header and data flag`` () =
    let body =
        Some
            { ContentType = "application/json"
              Content = "{\"a\":1}" }

    let c = CurlGenerator.toCurl (req POST "https://e.com" Map.empty body)
    Assert.Contains("-X POST", c)
    Assert.Contains("-H 'Content-Type: application/json'", c)
    Assert.Contains("-d '{\"a\":1}'", c)

[<Fact>]
let ``toCurl without a body emits neither content-type nor data flag`` () =
    let c = CurlGenerator.toCurl (req GET "https://e.com" Map.empty None)
    Assert.DoesNotContain(" -d ", c)
    Assert.DoesNotContain("Content-Type", c)

[<Fact>]
let ``toCurl escapes single quotes in the url`` () =
    let c = CurlGenerator.toCurl (req GET "https://e.com/it's" Map.empty None)
    Assert.Contains("it'\\''s", c)

[<Fact>]
let ``toCurl escapes single quotes in header key and value`` () =
    let headers = Map.ofList [ "X-It's", "va'lue" ]
    let c = CurlGenerator.toCurl (req GET "https://e.com" headers None)
    Assert.Contains("X-It'\\''s", c)
    Assert.Contains("va'\\''lue", c)

[<Fact>]
let ``toCurl escapes single quotes in the body content`` () =
    let body =
        Some
            { ContentType = "text/plain"
              Content = "it's a body" }

    let c = CurlGenerator.toCurl (req POST "https://e.com" Map.empty body)
    Assert.Contains("it'\\''s a body", c)

[<Fact>]
let ``toCurl combines verb, headers and body into one command`` () =
    let headers = Map.ofList [ "Authorization", "Bearer t" ]

    let body =
        Some
            { ContentType = "application/json"
              Content = "{}" }

    let c = CurlGenerator.toCurl (req PUT "https://e.com/x" headers body)
    Assert.StartsWith("curl -X PUT 'https://e.com/x'", c)
    Assert.Contains("-H 'Authorization: Bearer t'", c)
    Assert.Contains("-H 'Content-Type: application/json'", c)
    Assert.Contains("-d '{}'", c)
