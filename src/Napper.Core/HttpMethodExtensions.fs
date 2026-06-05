// Implements [NAP-METHODS].
// Behavior augmenting the generated HttpMethod union (Types.Generated.fs, from Types.td).
// typeDiagram models DATA only; these members are behavior and live here by hand —
// never in the generated file (which `make generate-types` overwrites).
namespace Napper.Core

[<AutoOpen>]
module HttpMethodExtensions =

    type HttpMethod with

        /// The BCL System.Net.Http.HttpMethod equivalent.
        member this.ToNetMethod() =
            match this with
            | GET -> System.Net.Http.HttpMethod.Get
            | POST -> System.Net.Http.HttpMethod.Post
            | PUT -> System.Net.Http.HttpMethod.Put
            | PATCH -> System.Net.Http.HttpMethod.Patch
            | DELETE -> System.Net.Http.HttpMethod.Delete
            | HEAD -> System.Net.Http.HttpMethod.Head
            | OPTIONS -> System.Net.Http.HttpMethod.Options

        /// The HTTP verb as an uppercase string. Single source of truth for
        /// method-name rendering across the CLI, curl generation, and the LSP.
        member this.Name =
            match this with
            | GET -> "GET"
            | POST -> "POST"
            | PUT -> "PUT"
            | PATCH -> "PATCH"
            | DELETE -> "DELETE"
            | HEAD -> "HEAD"
            | OPTIONS -> "OPTIONS"
