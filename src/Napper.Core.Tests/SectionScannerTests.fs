module SectionScannerTests
// Covers Napper.Core.SectionScanner — section locations + step-path extraction.
// Used by the LSP for document symbols/outline and by the runner to resolve naplist steps.

open Xunit
open Napper.Core.SectionScanner

[<Fact>]
let ``scanNapSections returns known bracket sections in file order`` () =
    let content = "[meta]\nname = x\n[request]\nGET https://e.com\n[assert]\nstatus 200"
    let names = scanNapSections content |> List.map (fun s -> s.Name)
    Assert.Equal<string list>([ "meta"; "request"; "assert" ], names)

[<Fact>]
let ``scanNapSections records start and end line for each section`` () =
    let content = "[meta]\nname = x\n[request]\nGET https://e.com"
    let secs = scanNapSections content
    let meta = secs |> List.find (fun s -> s.Name = "meta")
    Assert.Equal(0, meta.Line)
    Assert.Equal(1, meta.EndLine)
    let request = secs |> List.find (fun s -> s.Name = "request")
    Assert.Equal(2, request.Line)
    Assert.Equal(3, request.EndLine)

[<Fact>]
let ``scanNapSections detects a shorthand request on the first line`` () =
    let content = "GET https://example.com\n[assert]\nstatus 200"
    let secs = scanNapSections content
    Assert.Contains(secs, (fun s -> s.Name = "request" && s.Line = 0))
    Assert.Contains(secs, (fun s -> s.Name = "assert"))

[<Fact>]
let ``scanNapSections only treats line zero as a shorthand request`` () =
    // A shorthand verb below line 0 is request body, not a synthetic section.
    let content = "# comment\nGET https://e.com"
    Assert.Empty(scanNapSections content)

[<Fact>]
let ``scanNapSections recognizes dotted request subsections`` () =
    let content = "[request]\nGET x\n[request.headers]\nAccept: */*\n[request.body]\nbody"
    let names = scanNapSections content |> List.map (fun s -> s.Name)
    Assert.Contains("request.headers", names)
    Assert.Contains("request.body", names)

[<Fact>]
let ``scanNapSections ignores unknown bracket sections`` () =
    let content = "[bogus]\nx\n[meta]\nname = y"
    let secs = scanNapSections content
    Assert.DoesNotContain(secs, (fun s -> s.Name = "bogus"))
    Assert.Contains(secs, (fun s -> s.Name = "meta"))

[<Fact>]
let ``scanNapSections returns empty for content with no sections`` () =
    Assert.Empty(scanNapSections "just some text\nmore text")

[<Fact>]
let ``scanNaplistSections returns the naplist sections in order`` () =
    let content = "[meta]\nname = x\n[vars]\na = 1\n[steps]\nstep1.nap"
    let names = scanNaplistSections content |> List.map (fun s -> s.Name)
    Assert.Equal<string list>([ "meta"; "vars"; "steps" ], names)

[<Fact>]
let ``scanNaplistSections ignores nap-only section names`` () =
    let content = "[request]\nGET x\n[steps]\nstep1.nap"
    let secs = scanNaplistSections content
    Assert.DoesNotContain(secs, (fun s -> s.Name = "request"))
    Assert.Contains(secs, (fun s -> s.Name = "steps"))

[<Fact>]
let ``scanNaplistSections returns empty when there are no naplist sections`` () =
    Assert.Empty(scanNaplistSections "nothing here\nat all")

[<Fact>]
let ``scanNaplistStepPaths returns step files in declared order`` () =
    let content = "[meta]\nname = x\n[steps]\nfirst.nap\nsecond.nap\nthird.nap"
    let steps = scanNaplistStepPaths content
    Assert.Equal<string list>([ "first.nap"; "second.nap"; "third.nap" ], steps)

[<Fact>]
let ``scanNaplistStepPaths skips blank lines and comments`` () =
    let content = "[steps]\nfirst.nap\n\n# a comment\nsecond.nap"
    let steps = scanNaplistStepPaths content
    Assert.Equal<string list>([ "first.nap"; "second.nap" ], steps)

[<Fact>]
let ``scanNaplistStepPaths only collects lines inside the steps section`` () =
    let content = "[meta]\nname = x\nbefore.nap\n[steps]\nin.nap\n[vars]\nafter.nap"
    let steps = scanNaplistStepPaths content
    Assert.Equal<string list>([ "in.nap" ], steps)

[<Fact>]
let ``scanNaplistStepPaths returns empty when there is no steps section`` () =
    Assert.Empty(scanNaplistStepPaths "[meta]\nname = x")
