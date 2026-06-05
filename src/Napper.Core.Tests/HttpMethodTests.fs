// Tests [NAP-METHODS]
module HttpMethodTests

open Xunit
open Napper.Core

[<Fact>]
let ``GET.ToNetMethod returns HttpMethod.Get`` () =
    Assert.Equal(System.Net.Http.HttpMethod.Get, GET.ToNetMethod())

[<Fact>]
let ``POST.ToNetMethod returns HttpMethod.Post`` () =
    Assert.Equal(System.Net.Http.HttpMethod.Post, POST.ToNetMethod())

[<Fact>]
let ``PUT.ToNetMethod returns HttpMethod.Put`` () =
    Assert.Equal(System.Net.Http.HttpMethod.Put, PUT.ToNetMethod())

[<Fact>]
let ``PATCH.ToNetMethod returns HttpMethod.Patch`` () =
    Assert.Equal(System.Net.Http.HttpMethod.Patch, PATCH.ToNetMethod())

[<Fact>]
let ``DELETE.ToNetMethod returns HttpMethod.Delete`` () =
    Assert.Equal(System.Net.Http.HttpMethod.Delete, DELETE.ToNetMethod())

[<Fact>]
let ``HEAD.ToNetMethod returns HttpMethod.Head`` () =
    Assert.Equal(System.Net.Http.HttpMethod.Head, HEAD.ToNetMethod())

[<Fact>]
let ``OPTIONS.ToNetMethod returns HttpMethod.Options`` () =
    Assert.Equal(System.Net.Http.HttpMethod.Options, OPTIONS.ToNetMethod())

// .Name is the single source of truth for verb rendering across CLI, curl, and LSP.
// Covers every branch of HttpMethodExtensions.Name (Implements [NAP-METHODS]).
[<Fact>]
let ``Name returns the uppercase verb for every method`` () =
    Assert.Equal("GET", GET.Name)
    Assert.Equal("POST", POST.Name)
    Assert.Equal("PUT", PUT.Name)
    Assert.Equal("PATCH", PATCH.Name)
    Assert.Equal("DELETE", DELETE.Name)
    Assert.Equal("HEAD", HEAD.Name)
    Assert.Equal("OPTIONS", OPTIONS.Name)

[<Fact>]
let ``Name and ToNetMethod agree on the verb string for every method`` () =
    for m in [ GET; POST; PUT; PATCH; DELETE; HEAD; OPTIONS ] do
        Assert.Equal(m.ToNetMethod().Method, m.Name)
