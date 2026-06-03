module VersionContractTests
// e2e black-box tests for the Shipwright binary version contract and the release
// version stamper. Tests drive the REAL napper binary and the REAL stamper script
// through their CLI surface — never internal state.
// Implements [SWR-VERSION-CLI-OUTPUT], [SWR-VERSION-JSON-OUTPUT],
// [SWR-VERSION-BUILD-STAMPING], [SWR-VERSION-TEST-REQ].

open System.IO
open System.Text.Json
open Xunit
open TestHelpers

[<Literal>]
let private DevPlaceholder = "0.0.0-dev"

// Same semver shape the Shipwright manifest + version-manifest schemas accept.
[<Literal>]
let private SemverPattern =
    @"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$"

/// The version contract only trusts the first stdout line.
let private firstLine (s: string) =
    s.Replace("\r\n", "\n").Split('\n').[0].Trim()

let private repoRoot () =
    match findRepoRoot () with
    | Some r -> r
    | None -> failwith "repo root not found from test base directory"

// ─── napper --version (plain text) ───────────────── [SWR-VERSION-CLI-OUTPUT]

[<Fact>]
let ``napper --version prints 'napper <semver>' and exits 0`` () =
    let exitCode, stdout, _ = runCli "--version" (Directory.GetCurrentDirectory())
    Assert.Equal(0, exitCode)
    let line = firstLine stdout
    let parts = line.Split(' ')
    Assert.Equal(2, parts.Length)
    Assert.Equal("napper", parts.[0])
    Assert.Matches(SemverPattern, parts.[1])
    // The source tree always carries the placeholder. A hard-coded release version
    // in source is a release-engineering defect, so assert it here.
    Assert.Equal($"napper {DevPlaceholder}", line)

// ─── napper --version --json ─────────────────────── [SWR-VERSION-JSON-OUTPUT]

[<Fact>]
let ``napper --version --json conforms to the version manifest schema`` () =
    let exitCode, stdout, _ = runCli "--version --json" (Directory.GetCurrentDirectory())
    Assert.Equal(0, exitCode)
    use doc = JsonDocument.Parse(firstLine stdout)
    let root = doc.RootElement
    Assert.Equal(1, root.GetProperty("manifestVersion").GetInt32())
    Assert.Equal("napper", root.GetProperty("name").GetString())
    Assert.Equal("cli", root.GetProperty("kind").GetString())
    Assert.Equal("dotnet", root.GetProperty("language").GetString())
    let jsonVersion = root.GetProperty("version").GetString()
    Assert.Matches(SemverPattern, jsonVersion)
    // Plain and JSON forms MUST report the same version.
    let _, plainOut, _ = runCli "--version" (Directory.GetCurrentDirectory())
    Assert.Equal((firstLine plainOut).Split(' ').[1], jsonVersion)

// ─── version stamper ─────────────────────────────── [SWR-VERSION-BUILD-STAMPING]

let private vscodeDir = Path.Combine("src", "Napper.VsCode")
let private propsName = "Directory.Build.props"
let private pkgName = "package.json"
let private manifestName = "shipwright.json"

let private copyCarriers (root: string) (dest: string) =
    Directory.CreateDirectory(Path.Combine(dest, vscodeDir)) |> ignore
    File.Copy(Path.Combine(root, propsName), Path.Combine(dest, propsName))
    File.Copy(Path.Combine(root, vscodeDir, pkgName), Path.Combine(dest, vscodeDir, pkgName))
    File.Copy(Path.Combine(root, vscodeDir, manifestName), Path.Combine(dest, vscodeDir, manifestName))

let private runStamper (root: string) (extraArgs: string) =
    let script = Path.Combine(root, "scripts", "stamp-version.fsx")
    runProcessWithTimeout ScriptTimeoutMs "dotnet" $"fsi \"{script}\" {extraArgs}" root

[<Fact>]
let ``stamper rewrites every version carrier from a tag`` () =
    let root = repoRoot ()
    let temp = createTempDir "stamp"

    try
        copyCarriers root temp
        let version = "7.8.9"
        let exitCode, _, stderr = runStamper root $"--tag v{version} --root \"{temp}\""
        Assert.True((exitCode = 0), $"stamper exited {exitCode}: {stderr}")

        let props = File.ReadAllText(Path.Combine(temp, propsName))
        Assert.Contains($"<Version>{version}</Version>", props)

        use pkg = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp, vscodeDir, pkgName)))
        Assert.Equal(version, pkg.RootElement.GetProperty("version").GetString())

        use ship = JsonDocument.Parse(File.ReadAllText(Path.Combine(temp, vscodeDir, manifestName)))
        Assert.Equal(version, ship.RootElement.GetProperty("product").GetProperty("version").GetString())
        let mutable componentCount = 0

        for comp in ship.RootElement.GetProperty("components").EnumerateArray() do
            componentCount <- componentCount + 1
            Assert.Equal(version, comp.GetProperty("expectedVersion").GetString())

        Assert.True((componentCount >= 1), "manifest must declare at least one component")
    finally
        cleanupDir temp

[<Fact>]
let ``stamper dry-run changes nothing and rejects bad input`` () =
    let root = repoRoot ()
    let temp = createTempDir "stamp-dry"

    try
        copyCarriers root temp
        // dry-run: succeeds but leaves carriers at the placeholder.
        let exitDry, _, _ = runStamper root $"--version 5.5.5 --dry-run --root \"{temp}\""
        Assert.Equal(0, exitDry)
        Assert.Contains(DevPlaceholder, File.ReadAllText(Path.Combine(temp, propsName)))

        // invalid semver: non-zero exit, carriers untouched.
        let exitBad, _, _ = runStamper root $"--version not-a-semver --root \"{temp}\""
        Assert.NotEqual(0, exitBad)
        Assert.Contains(DevPlaceholder, File.ReadAllText(Path.Combine(temp, vscodeDir, pkgName)))
    finally
        cleanupDir temp
