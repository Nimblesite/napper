// scripts/stamp-version.fsx
//
// First-class, testable version stamper. Implements [SWR-VERSION-BUILD-STAMPING].
//
// Source-controlled version carriers MUST stay at the placeholder 0.0.0-dev. The
// real release version is an explicit build input derived from the git tag and is
// stamped into the runner working tree BEFORE build/verify/package — never committed.
//
// Why a repo-local stamper instead of `shipwright-version-stamp`: that tool only
// rewrites Cargo.toml / *.csproj / package.json / pubspec.yaml. Napper keeps its
// .NET <Version> in Directory.Build.props (not a .csproj) and has a shipwright.json
// manifest, neither of which that tool touches. This script stamps Napper's ACTUAL
// carriers using structured parsers (XDocument + System.Text.Json.Nodes) — never
// sed/regex over structured data (CLAUDE.md rule).
//
// Usage:
//   dotnet fsi scripts/stamp-version.fsx --tag v1.2.3            # stamp from a tag
//   dotnet fsi scripts/stamp-version.fsx --version 1.2.3         # stamp from a bare version
//   dotnet fsi scripts/stamp-version.fsx --version 1.2.3 --dry-run   # show, change nothing
//   dotnet fsi scripts/stamp-version.fsx --version 1.2.3 --root /tmp/copy   # stamp a copy (tests)

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Xml.Linq

// ---- Constants (no string literals scattered through the code) -------------------
let [<Literal>] DevPlaceholder = "0.0.0-dev"
let [<Literal>] PropsRelPath = "Directory.Build.props"
let [<Literal>] PackageJsonRelPath = "src/Napper.VsCode/package.json"
let [<Literal>] ShipwrightRelPath = "src/Napper.VsCode/shipwright.json"
let [<Literal>] VersionElement = "Version"
let [<Literal>] ProductKey = "product"
let [<Literal>] VersionKey = "version"
let [<Literal>] ComponentsKey = "components"
let [<Literal>] ExpectedVersionKey = "expectedVersion"
// Same shape the Shipwright manifest + version-manifest schemas accept.
let [<Literal>] SemverPattern =
    @"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$"

let log (msg: string) = printfn "[stamp] %s" msg

// ---- Argument parsing (structured fold, no regex on the arg list) ----------------
type Options =
    { Version: string option
      Root: string option
      DryRun: bool }

let private emptyOptions = { Version = None; Root = None; DryRun = false }

/// One leading `v` is stripped per [SWR-VERSION-MATCHING].
let private stripTag (raw: string) =
    if raw.StartsWith "v" then raw.Substring 1 else raw

let rec private parse (opts: Options) args =
    match args with
    | [] -> Ok opts
    | "--tag" :: value :: rest
    | "--version" :: value :: rest -> parse { opts with Version = Some(stripTag value) } rest
    | "--root" :: value :: rest -> parse { opts with Root = Some value } rest
    | "--dry-run" :: rest -> parse { opts with DryRun = true } rest
    | unknown :: _ -> Error $"Unknown or incomplete argument: {unknown}"

// ---- Carrier stampers (structured parsers only) ----------------------------------

/// Rewrite <Version> in an MSBuild props file via the XML DOM.
let private stampProps (path: string) (version: string) (dryRun: bool) =
    let doc = XDocument.Load(path, LoadOptions.PreserveWhitespace)

    match doc.Descendants(XName.Get VersionElement) |> Seq.tryHead with
    | None -> Error $"{PropsRelPath}: no <{VersionElement}> element found"
    | Some el ->
        log $"{path}: <{VersionElement}> {el.Value} -> {version}"

        if not dryRun then
            el.Value <- version
            doc.Save(path, SaveOptions.DisableFormatting)

        Ok()

/// Rewrite version fields in a JSON carrier via the JSON DOM. `mutate` applies the
/// version to the relevant nodes and returns a human-readable change list.
let private stampJson (path: string) (version: string) (dryRun: bool) (mutate: JsonNode -> string list) =
    let root = JsonNode.Parse(File.ReadAllText path)
    let changes = mutate root
    changes |> List.iter (fun c -> log $"{path}: {c}")

    if not dryRun then
        let opts = JsonSerializerOptions(WriteIndented = true, IndentSize = 2)
        File.WriteAllText(path, root.ToJsonString(opts) + "\n")

    Ok()

let private mutatePackageJson (version: string) (root: JsonNode) =
    let before = root[VersionKey].GetValue<string>()
    root[VersionKey] <- JsonValue.Create version
    [ $"{VersionKey} {before} -> {version}" ]

let private mutateShipwright (version: string) (root: JsonNode) =
    let product = root[ProductKey]
    let productBefore = product[VersionKey].GetValue<string>()
    product[VersionKey] <- JsonValue.Create version

    let componentChanges =
        root[ComponentsKey].AsArray()
        |> Seq.mapi (fun i comp ->
            let before = comp[ExpectedVersionKey].GetValue<string>()
            comp[ExpectedVersionKey] <- JsonValue.Create version
            $"components[{i}].{ExpectedVersionKey} {before} -> {version}")
        |> List.ofSeq

    $"{ProductKey}.{VersionKey} {productBefore} -> {version}" :: componentChanges

// ---- Driver ----------------------------------------------------------------------

let private repoRootDefault =
    // scripts/ -> repo root
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let private stampAll (root: string) (version: string) (dryRun: bool) =
    let propsPath = Path.Combine(root, PropsRelPath)
    let packagePath = Path.Combine(root, PackageJsonRelPath)
    let shipwrightPath = Path.Combine(root, ShipwrightRelPath)

    [ propsPath; packagePath; shipwrightPath ]
    |> List.tryFind (File.Exists >> not)
    |> function
        | Some missing -> Error $"Carrier not found: {missing}"
        | None ->
            stampProps propsPath version dryRun
            |> Result.bind (fun () -> stampJson packagePath version dryRun (mutatePackageJson version))
            |> Result.bind (fun () -> stampJson shipwrightPath version dryRun (mutateShipwright version))

let private run () =
    match parse emptyOptions (Array.toList (fsi.CommandLineArgs |> Array.skip 1)) with
    | Error e -> Error e
    | Ok opts ->
        match opts.Version with
        | None -> Error "Missing required --version <semver> or --tag <vX.Y.Z>"
        | Some version when not (Regex.IsMatch(version, SemverPattern)) ->
            Error $"Not a valid semantic version: {version}"
        | Some version when version = DevPlaceholder ->
            Error $"Refusing to stamp the dev placeholder {DevPlaceholder}; pass a real release version"
        | Some version ->
            let root = opts.Root |> Option.defaultValue repoRootDefault
            log $"Stamping version {version} into {root} (dry-run={opts.DryRun})"
            stampAll root version opts.DryRun

match run () with
| Ok() ->
    log "Done. All carriers stamped."
    exit 0
| Error e ->
    eprintfn "[stamp] ERROR: %s" e
    exit 1
