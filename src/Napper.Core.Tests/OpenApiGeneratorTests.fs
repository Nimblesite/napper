module OpenApiGeneratorTests
// Tests [OPENAPI-GENERATE], [OPENAPI-OAS3], [OPENAPI-SWAGGER2], [OPENAPI-NAP-GEN], [OPENAPI-TAG-DIRS], [OPENAPI-NAPLIST-GEN], [OPENAPI-NAPENV-GEN], [OPENAPI-BASEURL], [OPENAPI-PARAMS], [OPENAPI-BODY-GEN], [OPENAPI-ASSERT-GEN], [OPENAPI-QUERY-PARAMS], [OPENAPI-AUTH], [OPENAPI-META-FLAG], [NAP-META], [NAP-REQUEST], [NAP-HEADERS], [NAP-BODY], [NAP-VARS], [NAP-ASSERT]

open Xunit
open Napper.Core
open Napper.Core.OpenApiGenerator

// --- Helpers ---

let private unwrap (jsonText: string) : GenerationResult =
    match generate jsonText with
    | Ok result -> result
    | Error e -> failwith $"Expected generation to succeed but got: {e}"

let private firstFile (gen: GenerationResult) : GeneratedFile =
    match gen.NapFiles with
    | first :: _ -> first
    | [] -> failwith "Expected at least one generated nap file"

let private fileAt (gen: GenerationResult) (index: int) : GeneratedFile =
    if index < gen.NapFiles.Length then
        gen.NapFiles[index]
    else
        failwith $"Expected nap file at index {index}"

// --- Minimal specs ---

let private minimalOas3 =
    """
{
  "openapi": "3.0.0",
  "info": { "title": "Test API" },
  "servers": [{ "url": "https://api.test.com/v1" }],
  "paths": {
    "/users": {
      "get": {
        "summary": "List users",
        "responses": { "200": { "description": "OK" } }
      }
    }
  }
}"""

let private minimalSwagger2 =
    """
{
  "swagger": "2.0",
  "info": { "title": "Legacy API" },
  "host": "legacy.test.com",
  "basePath": "/api",
  "schemes": ["https"],
  "paths": {
    "/items": {
      "get": {
        "summary": "List items",
        "responses": { "200": { "description": "OK" } }
      }
    }
  }
}"""

let private multiMethodSpec =
    """
{
  "openapi": "3.0.0",
  "info": { "title": "CRUD API" },
  "servers": [{ "url": "https://crud.test.com" }],
  "paths": {
    "/pets": {
      "get": {
        "summary": "List pets",
        "responses": { "200": { "description": "OK" } }
      },
      "post": {
        "summary": "Create pet",
        "requestBody": {
          "content": {
            "application/json": {
              "schema": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "age": { "type": "integer" }
                }
              }
            }
          }
        },
        "responses": { "201": { "description": "Created" } }
      }
    },
    "/pets/{petId}": {
      "get": {
        "operationId": "getPetById",
        "responses": { "200": { "description": "OK" } }
      },
      "delete": {
        "summary": "Delete pet",
        "responses": { "204": { "description": "Deleted" } }
      }
    }
  }
}"""

// --- Error handling --- Spec: [OPENAPI-GENERATE]

[<Fact>]
let ``Rejects invalid JSON`` () =
    match generate "not json{{{" with
    | Error e -> Assert.Equal("Failed to parse specification", e)
    | Ok _ -> failwith "Expected error"

[<Fact>]
let ``Rejects spec without paths`` () =
    match generate """{ "openapi": "3.0.0" }""" with
    | Error _ -> ()
    | Ok _ -> failwith "Expected error"

[<Fact>]
let ``Rejects spec with empty paths`` () =
    match generate """{ "openapi": "3.0.0", "paths": {} }""" with
    | Error e -> Assert.Equal("No endpoints found in specification", e)
    | Ok _ -> failwith "Expected error"

[<Fact>]
let ``Rejects null input`` () =
    match generate "null" with
    | Error _ -> ()
    | Ok _ -> failwith "Expected error"

// --- OpenAPI 3.x --- Spec: [OPENAPI-OAS3], [OPENAPI-NAP-GEN], [OPENAPI-NAPLIST-GEN], [OPENAPI-NAPENV-GEN], [OPENAPI-BASEURL], [OPENAPI-META-FLAG]

[<Fact>]
let ``OAS3 generates correct number of nap files`` () =
    let gen = unwrap minimalOas3
    Assert.Equal(1, gen.NapFiles.Length)

[<Fact>]
let ``OAS3 nap file has nap extension`` () =
    let gen = unwrap minimalOas3
    let file = firstFile gen
    Assert.EndsWith(".nap", file.FileName)

[<Fact>]
let ``OAS3 nap file contains meta section with name`` () =
    let content = (unwrap minimalOas3 |> firstFile).Content
    Assert.Contains("[meta]", content)
    Assert.Contains("name = List users", content)

[<Fact>]
let ``OAS3 nap file contains request section`` () =
    let content = (unwrap minimalOas3 |> firstFile).Content
    Assert.Contains("[request]", content)
    Assert.Contains("method = GET", content)
    Assert.Contains("url = {{baseUrl}}/users", content)

[<Fact>]
let ``OAS3 nap file contains assert section`` () =
    let content = (unwrap minimalOas3 |> firstFile).Content
    Assert.Contains("[assert]", content)
    Assert.Contains("status = 200", content)

[<Fact>]
let ``OAS3 extracts base URL from servers`` () =
    let gen = unwrap minimalOas3
    Assert.Contains("https://api.test.com/v1", gen.Environment.Content)

[<Fact>]
let ``OAS3 environment has napenv extension`` () =
    let gen = unwrap minimalOas3
    Assert.Equal(".napenv", gen.Environment.FileName)

[<Fact>]
let ``OAS3 playlist has naplist extension`` () =
    let gen = unwrap minimalOas3
    Assert.EndsWith(".naplist", gen.Playlist.FileName)

[<Fact>]
let ``OAS3 playlist references generated files`` () =
    let gen = unwrap minimalOas3

    for f in gen.NapFiles do
        Assert.Contains(f.FileName, gen.Playlist.Content)

[<Fact>]
let ``OAS3 generated flag in meta`` () =
    let content = (unwrap minimalOas3 |> firstFile).Content
    Assert.Contains("generated = true", content)

// --- Swagger 2.x --- Spec: [OPENAPI-SWAGGER2], [OPENAPI-BASEURL]

[<Fact>]
let ``Swagger2 extracts base URL from host and basePath`` () =
    let gen = unwrap minimalSwagger2
    Assert.Contains("https://legacy.test.com/api", gen.Environment.Content)

[<Fact>]
let ``Swagger2 generates nap file`` () =
    let gen = unwrap minimalSwagger2
    Assert.Equal(1, gen.NapFiles.Length)
    let content = (firstFile gen).Content
    Assert.Contains("method = GET", content)
    Assert.Contains("url = {{baseUrl}}/items", content)

// --- Multiple endpoints --- Spec: [OPENAPI-NAP-GEN], [OPENAPI-PARAMS], [OPENAPI-ASSERT-GEN]

[<Fact>]
let ``Generates one nap file per operation`` () =
    let gen = unwrap multiMethodSpec
    Assert.Equal(4, gen.NapFiles.Length)

[<Fact>]
let ``Files are numbered sequentially`` () =
    let gen = unwrap multiMethodSpec
    Assert.StartsWith("01_", (fileAt gen 0).FileName)
    Assert.StartsWith("02_", (fileAt gen 1).FileName)
    Assert.StartsWith("04_", (fileAt gen 3).FileName)

[<Fact>]
let ``Path params converted from single to double braces`` () =
    let gen = unwrap multiMethodSpec
    let petFile = gen.NapFiles |> List.find (fun f -> f.Content.Contains("getPetById"))
    Assert.Contains("{{petId}}", petFile.Content)
    Assert.DoesNotContain("/pets/{petId}", petFile.Content)

[<Fact>]
let ``POST gets status 201 assertion`` () =
    let gen = unwrap multiMethodSpec
    let postFile = gen.NapFiles |> List.find (fun f -> f.Content.Contains("Create pet"))
    Assert.Contains("status = 201", postFile.Content)

[<Fact>]
let ``DELETE gets status 204 assertion`` () =
    let gen = unwrap multiMethodSpec

    let deleteFile =
        gen.NapFiles |> List.find (fun f -> f.Content.Contains("Delete pet"))

    Assert.Contains("status = 204", deleteFile.Content)

[<Fact>]
let ``Uses operationId for file name`` () =
    let gen = unwrap multiMethodSpec

    let opIdFile =
        gen.NapFiles |> List.tryFind (fun f -> f.FileName.Contains("getPetById"))

    Assert.True(opIdFile.IsSome, "must use operationId in filename")

// --- Request bodies --- Spec: [OPENAPI-BODY-GEN], [NAP-HEADERS], [NAP-BODY]

[<Fact>]
let ``POST includes Content-Type and Accept headers`` () =
    let gen = unwrap multiMethodSpec
    let postFile = gen.NapFiles |> List.find (fun f -> f.Content.Contains("Create pet"))
    Assert.Contains("[request.headers]", postFile.Content)
    Assert.Contains("Content-Type = application/json", postFile.Content)
    Assert.Contains("Accept = application/json", postFile.Content)

[<Fact>]
let ``GET does not get request headers section`` () =
    let content = (unwrap minimalOas3 |> firstFile).Content
    Assert.DoesNotContain("[request.headers]", content)

[<Fact>]
let ``POST generates request body from schema`` () =
    let gen = unwrap multiMethodSpec
    let postFile = gen.NapFiles |> List.find (fun f -> f.Content.Contains("Create pet"))
    Assert.Contains("[request.body]", postFile.Content)
    Assert.Contains("\"\"\"", postFile.Content)

// --- Vars block --- Spec: [OPENAPI-PARAMS], [NAP-VARS]

[<Fact>]
let ``Path with params generates vars section`` () =
    let gen = unwrap multiMethodSpec
    let petFile = gen.NapFiles |> List.find (fun f -> f.Content.Contains("getPetById"))
    Assert.Contains("[vars]", petFile.Content)
    Assert.Contains("petId = \"REPLACE_ME\"", petFile.Content)

[<Fact>]
let ``Path without params has no vars section`` () =
    let content = (unwrap minimalOas3 |> firstFile).Content
    Assert.DoesNotContain("[vars]", content)

[<Fact>]
let ``Multiple path params each get a var entry`` () =
    let spec =
        """
    {
      "openapi": "3.0.0",
      "info": { "title": "Multi Param" },
      "paths": {
        "/orgs/{orgId}/users/{userId}": {
          "get": {
            "summary": "Get org user",
            "responses": { "200": { "description": "OK" } }
          }
        }
      }
    }"""

    let gen = unwrap spec
    let content = (firstFile gen).Content
    Assert.Contains("orgId = \"REPLACE_ME\"", content)
    Assert.Contains("userId = \"REPLACE_ME\"", content)

// --- Response body assertions --- Spec: [OPENAPI-ASSERT-GEN], [NAP-ASSERT]

[<Fact>]
let ``OAS3 response schema generates body field assertions`` () =
    let spec =
        """
    {
      "openapi": "3.0.0",
      "info": { "title": "Schema API" },
      "servers": [{ "url": "https://schema.test.com" }],
      "paths": {
        "/users/{userId}": {
          "get": {
            "summary": "Get user",
            "responses": {
              "200": {
                "description": "OK",
                "content": {
                  "application/json": {
                    "schema": {
                      "type": "object",
                      "properties": {
                        "id": { "type": "integer" },
                        "name": { "type": "string" },
                        "email": { "type": "string" }
                      }
                    }
                  }
                }
              }
            }
          }
        }
      }
    }"""

    let gen = unwrap spec
    let content = (firstFile gen).Content
    Assert.Contains("body.id exists", content)
    Assert.Contains("body.name exists", content)
    Assert.Contains("body.email exists", content)

[<Fact>]
let ``No body assertions when response has no schema`` () =
    let content = (unwrap minimalOas3 |> firstFile).Content
    Assert.DoesNotContain("body.", content)

// --- Tag-based folders --- Spec: [OPENAPI-TAG-DIRS]

let private taggedSpec =
    """
{
  "openapi": "3.0.0",
  "info": { "title": "Tagged API" },
  "servers": [{ "url": "https://tagged.test.com" }],
  "paths": {
    "/users": {
      "get": {
        "tags": ["users"],
        "summary": "List users",
        "responses": { "200": { "description": "OK" } }
      },
      "post": {
        "tags": ["users"],
        "summary": "Create user",
        "requestBody": {
          "content": {
            "application/json": {
              "schema": { "type": "object", "properties": { "name": { "type": "string" } } }
            }
          }
        },
        "responses": { "201": { "description": "Created" } }
      }
    },
    "/pets": {
      "get": {
        "tags": ["pets"],
        "summary": "List pets",
        "responses": { "200": { "description": "OK" } }
      }
    },
    "/health": {
      "get": {
        "summary": "Health check",
        "responses": { "200": { "description": "OK" } }
      }
    }
  }
}"""

[<Fact>]
let ``Tagged operations get tag subdirectory`` () =
    let gen = unwrap taggedSpec

    let userFiles =
        gen.NapFiles |> List.filter (fun f -> f.FileName.StartsWith("users/"))

    Assert.Equal(2, userFiles.Length)

[<Fact>]
let ``Different tags create different subdirectories`` () =
    let gen = unwrap taggedSpec
    let petFiles = gen.NapFiles |> List.filter (fun f -> f.FileName.StartsWith("pets/"))
    Assert.Equal(1, petFiles.Length)

[<Fact>]
let ``Untagged operations stay in root`` () =
    let gen = unwrap taggedSpec

    let healthFile =
        gen.NapFiles |> List.find (fun f -> f.Content.Contains("Health check"))

    Assert.DoesNotContain("/", healthFile.FileName)

[<Fact>]
let ``Playlist references files with subdirectory paths`` () =
    let gen = unwrap taggedSpec
    Assert.Contains("./users/", gen.Playlist.Content)
    Assert.Contains("./pets/", gen.Playlist.Content)

// --- Query parameters --- Spec: [OPENAPI-QUERY-PARAMS], [NAP-REQUEST], [NAP-VARS]

[<Fact>]
let ``Query params appended to URL`` () =
    let spec =
        """
    {
      "openapi": "3.0.0",
      "info": { "title": "Query API" },
      "paths": {
        "/search": {
          "get": {
            "summary": "Search",
            "parameters": [
              { "name": "q", "in": "query" },
              { "name": "limit", "in": "query" }
            ],
            "responses": { "200": { "description": "OK" } }
          }
        }
      }
    }"""

    let content = (unwrap spec |> firstFile).Content
    Assert.Contains("?q={{q}}&limit={{limit}}", content)

[<Fact>]
let ``Query params added to vars section`` () =
    let spec =
        """
    {
      "openapi": "3.0.0",
      "info": { "title": "Query API" },
      "paths": {
        "/search": {
          "get": {
            "summary": "Search",
            "parameters": [
              { "name": "q", "in": "query" }
            ],
            "responses": { "200": { "description": "OK" } }
          }
        }
      }
    }"""

    let content = (unwrap spec |> firstFile).Content
    Assert.Contains("[vars]", content)
    Assert.Contains("q = \"REPLACE_ME\"", content)

// --- Auth schemes --- Spec: [OPENAPI-AUTH], [NAP-HEADERS]

[<Fact>]
let ``Bearer auth adds Authorization header`` () =
    let spec =
        """
    {
      "openapi": "3.0.0",
      "info": { "title": "Auth API" },
      "paths": {
        "/protected": {
          "get": {
            "summary": "Protected",
            "security": [{ "bearerAuth": [] }],
            "responses": { "200": { "description": "OK" } }
          }
        }
      },
      "components": {
        "securitySchemes": {
          "bearerAuth": { "type": "http", "scheme": "bearer" }
        }
      }
    }"""

    let content = (unwrap spec |> firstFile).Content
    Assert.Contains("[request.headers]", content)
    Assert.Contains("Authorization = Bearer {{token}}", content)
    Assert.Contains("token = \"REPLACE_ME\"", content)

[<Fact>]
let ``API key auth adds custom header`` () =
    let spec =
        """
    {
      "openapi": "3.0.0",
      "info": { "title": "API Key API" },
      "paths": {
        "/data": {
          "get": {
            "summary": "Get data",
            "security": [{ "apiKeyAuth": [] }],
            "responses": { "200": { "description": "OK" } }
          }
        }
      },
      "components": {
        "securitySchemes": {
          "apiKeyAuth": { "type": "apiKey", "in": "header", "name": "X-API-Key" }
        }
      }
    }"""

    let content = (unwrap spec |> firstFile).Content
    Assert.Contains("X-API-Key = {{apiKey}}", content)
    Assert.Contains("apiKey = \"REPLACE_ME\"", content)

[<Fact>]
let ``Global security applies to all operations`` () =
    let spec =
        """
    {
      "openapi": "3.0.0",
      "info": { "title": "Global Auth" },
      "security": [{ "bearerAuth": [] }],
      "paths": {
        "/items": {
          "get": {
            "summary": "List items",
            "responses": { "200": { "description": "OK" } }
          }
        }
      },
      "components": {
        "securitySchemes": {
          "bearerAuth": { "type": "http", "scheme": "bearer" }
        }
      }
    }"""

    let content = (unwrap spec |> firstFile).Content
    Assert.Contains("Authorization = Bearer {{token}}", content)

[<Fact>]
let ``No auth headers when no security defined`` () =
    let content = (unwrap minimalOas3 |> firstFile).Content
    Assert.DoesNotContain("Authorization", content)

[<Fact>]
let ``Basic auth adds Authorization header with Basic prefix`` () =
    let spec =
        """
    {
      "openapi": "3.0.0",
      "info": { "title": "Basic Auth API" },
      "paths": {
        "/secure": {
          "get": {
            "summary": "Secure endpoint",
            "security": [{ "basicAuth": [] }],
            "responses": { "200": { "description": "OK" } }
          }
        }
      },
      "components": {
        "securitySchemes": {
          "basicAuth": { "type": "http", "scheme": "basic" }
        }
      }
    }"""

    let content = (unwrap spec |> firstFile).Content
    Assert.Contains("[request.headers]", content)
    Assert.Contains("Authorization = Basic {{basicAuth}}", content)
    Assert.Contains("[vars]", content)
    Assert.Contains("basicAuth = \"REPLACE_ME\"", content)

// --- Body content verification --- Spec: [OPENAPI-BODY-GEN], [NAP-BODY]

[<Fact>]
let ``POST body contains actual JSON from schema`` () =
    let gen = unwrap multiMethodSpec
    let postFile = gen.NapFiles |> List.find (fun f -> f.Content.Contains("Create pet"))
    Assert.Contains("[request.body]", postFile.Content)
    Assert.Contains("\"\"\"", postFile.Content)
    Assert.Contains("\"name\"", postFile.Content)
    Assert.Contains("\"age\"", postFile.Content)

[<Fact>]
let ``Nested object schema generates nested JSON body`` () =
    let spec =
        """
    {
      "openapi": "3.0.0",
      "info": { "title": "Nested API" },
      "paths": {
        "/orders": {
          "post": {
            "summary": "Create order",
            "requestBody": {
              "content": {
                "application/json": {
                  "schema": {
                    "type": "object",
                    "properties": {
                      "item": { "type": "string" },
                      "quantity": { "type": "integer" },
                      "address": {
                        "type": "object",
                        "properties": {
                          "street": { "type": "string" },
                          "city": { "type": "string" }
                        }
                      }
                    }
                  }
                }
              }
            },
            "responses": { "201": { "description": "Created" } }
          }
        }
      }
    }"""

    let content = (unwrap spec |> firstFile).Content
    Assert.Contains("[request.body]", content)
    Assert.Contains("\"item\"", content)
    Assert.Contains("\"street\"", content)
    Assert.Contains("\"city\"", content)

// --- All path param endpoints must have vars --- Spec: [OPENAPI-PARAMS], [NAP-VARS]

[<Fact>]
let ``Every endpoint with path params has vars section`` () =
    let gen = unwrap multiMethodSpec

    let paramFiles =
        gen.NapFiles |> List.filter (fun f -> f.Content.Contains("{{petId}}"))

    Assert.True(paramFiles.Length >= 2, $"Must have at least 2 petId endpoints, got {paramFiles.Length}")

    for f in paramFiles do
        Assert.Contains("[vars]", f.Content)
        Assert.Contains("petId = \"REPLACE_ME\"", f.Content)

// --- Complete .nap file format validation --- Spec: [NAP-FILE], [NAP-META], [NAP-REQUEST], [NAP-ASSERT]

[<Fact>]
let ``Generated nap file has correct section ordering`` () =
    let content = (unwrap minimalOas3 |> firstFile).Content
    let metaIdx = content.IndexOf("[meta]")
    let requestIdx = content.IndexOf("[request]")
    let assertIdx = content.IndexOf("[assert]")
    Assert.True(metaIdx >= 0, "Must have [meta]")
    Assert.True(requestIdx > metaIdx, "[request] must come after [meta]")
    Assert.True(assertIdx > requestIdx, "[assert] must come after [request]")

[<Fact>]
let ``POST nap file has full section chain`` () =
    let gen = unwrap multiMethodSpec
    let postFile = gen.NapFiles |> List.find (fun f -> f.Content.Contains("Create pet"))
    let content = postFile.Content
    let metaIdx = content.IndexOf("[meta]")
    let requestIdx = content.IndexOf("[request]")
    let headersIdx = content.IndexOf("[request.headers]")
    let bodyIdx = content.IndexOf("[request.body]")
    let assertIdx = content.IndexOf("[assert]")
    Assert.True(metaIdx >= 0, "Must have [meta]")
    Assert.True(requestIdx > metaIdx, "[request] must come after [meta]")
    Assert.True(headersIdx > requestIdx, "[request.headers] must come after [request]")
    Assert.True(bodyIdx > headersIdx, "[request.body] must come after [request.headers]")
    Assert.True(assertIdx > bodyIdx, "[assert] must come after [request.body]")

// --- Playlist format validation --- Spec: [NAPLIST-FILE]

[<Fact>]
let ``Playlist has meta section with API title`` () =
    let gen = unwrap minimalOas3
    Assert.Contains("[meta]", gen.Playlist.Content)
    Assert.Contains("name = Test API", gen.Playlist.Content)

[<Fact>]
let ``Playlist steps reference files with relative paths`` () =
    let gen = unwrap minimalOas3
    Assert.Contains("[steps]", gen.Playlist.Content)

    for f in gen.NapFiles do
        Assert.Contains($"./{f.FileName}", gen.Playlist.Content)

// --- Environment file format --- Spec: [ENV-FILE]

[<Fact>]
let ``Environment file has baseUrl key-value pair`` () =
    let gen = unwrap minimalOas3
    Assert.Equal(".napenv", gen.Environment.FileName)
    Assert.Contains("baseUrl = https://api.test.com/v1", gen.Environment.Content)

// --- Base URL fallback --- Spec: [OPENAPI-BASEURL]

// --- Generated files must be parseable --- Spec: [OPENAPI-NAP-GEN], [NAP-FILE]

[<Fact>]
let ``Generated nap files are parseable by the nap parser`` () =
    let gen = unwrap minimalOas3

    for f in gen.NapFiles do
        match Napper.Core.Parser.parseNapFile f.Content with
        | Ok parsed ->
            Assert.Equal(GET, parsed.Request.Method)
            Assert.Contains("{{baseUrl}}/users", parsed.Request.Url)
        | Error e -> failwith $"Generated file '{f.FileName}' failed to parse: {e}"

[<Fact>]
let ``Generated POST nap file is parseable with correct method and body`` () =
    let gen = unwrap multiMethodSpec
    let postFile = gen.NapFiles |> List.find (fun f -> f.Content.Contains("Create pet"))

    match Napper.Core.Parser.parseNapFile postFile.Content with
    | Ok parsed ->
        Assert.Equal(POST, parsed.Request.Method)
        Assert.Contains("{{baseUrl}}/pets", parsed.Request.Url)
        Assert.True(parsed.Request.Body.IsSome, "POST must have a request body")
    | Error e -> failwith $"Generated POST file failed to parse: {e}"

[<Fact>]
let ``Generated nap file with path params is parseable`` () =
    let gen = unwrap multiMethodSpec
    let petFile = gen.NapFiles |> List.find (fun f -> f.Content.Contains("getPetById"))

    match Napper.Core.Parser.parseNapFile petFile.Content with
    | Ok parsed ->
        Assert.Contains("{{petId}}", parsed.Request.Url)
        Assert.True(parsed.Vars.ContainsKey("petId"), "Must have petId var")
    | Error e -> failwith $"Generated file with path params failed to parse: {e}"

[<Fact>]
let ``Falls back to default URL when no servers or host`` () =
    let spec =
        """
    {
      "openapi": "3.0.0",
      "info": { "title": "No Servers" },
      "paths": {
        "/health": {
          "get": {
            "summary": "Health",
            "responses": { "200": { "description": "OK" } }
          }
        }
      }
    }"""

    let gen = unwrap spec
    Assert.Contains("https://api.example.com", gen.Environment.Content)

// ─── Request-body schema example generation ─── Spec: openapi-body-gen

[<Fact>]
let ``generate emits example values for every JSON schema type in a request body`` () =
    // Drives writeByType through string/number/integer/boolean/array(no items)/object/untyped.
    let spec =
        """
{
  "openapi": "3.0.0",
  "info": { "title": "Body API" },
  "paths": {
    "/things": {
      "post": {
        "summary": "Create thing",
        "requestBody": {
          "content": {
            "application/json": {
              "schema": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "count": { "type": "integer" },
                  "ratio": { "type": "number" },
                  "active": { "type": "boolean" },
                  "tags": { "type": "array" },
                  "meta": { "type": "object", "properties": { "k": { "type": "string" } } },
                  "untyped": {}
                }
              }
            }
          }
        },
        "responses": { "201": { "description": "Created" } }
      }
    }
  }
}"""

    let gen = unwrap spec
    let content = (firstFile gen).Content
    Assert.Contains("[request.body]", content)
    Assert.Contains("\"name\": \"example\"", content) // string -> SchemaExampleString
    Assert.Contains("\"ratio\": 0", content) // number -> 0
    Assert.Contains("\"active\": true", content) // boolean -> true
    Assert.Contains("\"tags\": []", content) // array with no items -> empty array

[<Fact>]
let ``generate uses an inline request-body example when present`` () =
    let spec =
        """
{
  "openapi": "3.0.0",
  "info": { "title": "Example API" },
  "paths": {
    "/things": {
      "post": {
        "summary": "Create",
        "requestBody": {
          "content": { "application/json": { "example": { "hello": "world" } } }
        },
        "responses": { "200": { "description": "OK" } }
      }
    }
  }
}"""

    let gen = unwrap spec
    Assert.Contains("hello", (firstFile gen).Content)

// ─── Meta name fallback ─────────────────────── Spec: openapi-meta-flag, nap-meta

[<Fact>]
let ``generate falls back to a path-derived name when summary and operationId are absent`` () =
    let spec =
        """
{
  "openapi": "3.0.0",
  "info": { "title": "Fallback API" },
  "paths": {
    "/widgets": { "get": { "responses": { "200": { "description": "OK" } } } }
  }
}"""

    let gen = unwrap spec
    // No summary, no operationId -> name derived from method+path slug "get-widgets".
    Assert.Contains("get-widgets", (firstFile gen).Content)

// ─── Auth: unsupported http scheme yields no auth header ── Spec: openapi-auth

[<Fact>]
let ``generate ignores an http security scheme that is neither bearer nor basic`` () =
    let spec =
        """
{
  "openapi": "3.0.0",
  "info": { "title": "Digest API" },
  "components": { "securitySchemes": { "d": { "type": "http", "scheme": "digest" } } },
  "security": [ { "d": [] } ],
  "paths": { "/secure": { "get": { "responses": { "200": { "description": "OK" } } } } }
}"""

    let gen = unwrap spec
    // digest is unsupported -> no Authorization header is generated.
    Assert.DoesNotContain("Authorization", (firstFile gen).Content)

// ─── Padding width for large specs ──────────── Spec: openapi-nap-gen

[<Fact>]
let ``generate pads file indexes to three digits for 100+ endpoints`` () =
    let paths =
        [ for i in 1..100 ->
              sprintf "\"/r%d\": { \"get\": { \"responses\": { \"200\": { \"description\": \"OK\" } } } }" i ]
        |> String.concat ","

    let spec =
        sprintf "{ \"openapi\": \"3.0.0\", \"info\": { \"title\": \"Big API\" }, \"paths\": { %s } }" paths

    let gen = unwrap spec
    Assert.Equal(100, gen.NapFiles.Length)
    // total >= 100 -> three-digit zero-padded prefixes (e.g. 001_...).
    Assert.True(
        gen.NapFiles |> List.exists (fun f -> f.FileName.Contains "001_"),
        "expected a 3-digit padded index like 001_"
    )

// ─── Title slug fallback + parse errors ─────── Spec: openapi-naplist-gen, openapi-error-gen

[<Fact>]
let ``generate falls back to api-tests when the title slugifies to empty`` () =
    let spec =
        """
{
  "openapi": "3.0.0",
  "info": { "title": "/" },
  "paths": { "/x": { "get": { "responses": { "200": { "description": "OK" } } } } }
}"""

    let gen = unwrap spec
    Assert.Contains("api-tests", gen.Playlist.FileName)

[<Fact>]
let ``generate returns an error for unparseable input`` () =
    match generate "{ this is not a valid openapi document" with
    | Ok _ -> failwith "expected a parse error"
    | Error _ -> ()
