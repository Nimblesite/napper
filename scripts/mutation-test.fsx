// Mutation-testing harness for the F# scripting engine.
//
// Stryker.NET — the standard .NET mutation tester — does NOT support F#: pointed at this
// repo it throws `System.NotSupportedException: Language not supported: Fsharp`. There is no
// mature F# mutation tool, so this is a curated, deterministic substitute. For each mutant we:
//   1. snapshot the target file,
//   2. apply the mutation (a literal find/replace — no regex on structured data),
//   3. rebuild the napper CLI binary,
//   4. run ONLY the test that is supposed to catch it,
//   5. classify: test FAILS => mutant KILLED (good); test PASSES => mutant SURVIVED (a test gap),
//   6. always restore the original file.
//
// A surviving mutant means the test suite does not actually enforce that behaviour.
// Run with:  make mutation   (or:  dotnet fsi scripts/mutation-test.fsx)

open System
open System.Diagnostics
open System.IO

let repoRoot = Path.GetDirectoryName(__SOURCE_DIRECTORY__)

// ─── A single mutant: a literal source edit + the test that must kill it ───────────────

type Mutant =
    { Id: string
      File: string // relative to repo root
      Find: string
      Replace: string
      Occurrences: int // expected literal occurrences of Find (catalog sanity check)
      KilledBy: string // dotnet test --filter expression of the test(s) that should fail
      Desc: string }

// The catalog mirrors the by-hand mutations: each targets one proven ctx behaviour.
let catalog =
    [ { Id = "fail-ignored"
        File = "src/Napper.Core/Runner.fs"
        Find = "let passed = proc.ExitCode = 0 && not directives.Failed"
        Replace = "let passed = proc.ExitCode = 0"
        Occurrences = 1
        KilledBy = "FullyQualifiedName~CtxScriptTests.JS ctx.fail"
        Desc = "ctx.fail no longer fails the step (pass keyed only on exit code)" }
      { Id = "setvars-dropped"
        File = "src/Napper.Cli/Program.fs"
        Find = "acc @ [ r ], Runner.mergeVars v r.SetVars"
        Replace = "acc @ [ r ], v"
        Occurrences = 2
        KilledBy = "FullyQualifiedName~CtxScriptTests.JS ctx.set"
        Desc = "ctx.set no longer threads a variable to downstream steps" }
      { Id = "post-hook-skipped"
        File = "src/Napper.Core/Runner.fs"
        Find = "executeWithPost filePath dir envName napFile.Script.Post preLogs preVars mergedVars resolved"
        Replace = "executeWithPost filePath dir envName None preLogs preVars mergedVars resolved"
        Occurrences = 1
        KilledBy = "FullyQualifiedName~CtxScriptTests.CSX post-hook"
        Desc = "[script] post hooks are never executed" }
      { Id = "response-null"
        File = "src/Napper.Core/ScriptContext.fs"
        Find = "| Some resp -> writeResponse w resp"
        Replace = "| Some _ -> w.WriteNull(\"response\")"
        Occurrences = 1
        KilledBy = "FullyQualifiedName~CtxScriptTests.JS post-hook can read ctx.response"
        Desc = "ctx.response is never populated for post hooks" }
      { Id = "logs-dropped"
        File = "src/Napper.Core/Runner.fs"
        Find = "Log = splitStdout stdout @ directives.Logs"
        Replace = "Log = splitStdout stdout"
        Occurrences = 1
        KilledBy = "FullyQualifiedName~CtxScriptTests.JS step can read"
        Desc = "ctx.log lines are dropped from the result" }
      { Id = "request-method"
        File = "src/Napper.Core/ScriptContext.fs"
        Find = "w.WriteString(\"method\", req.Method.Name)"
        Replace = "w.WriteString(\"method\", \"X\")"
        Occurrences = 1
        KilledBy = "FullyQualifiedName~CtxScriptTests.JS pre-hook can read ctx.request"
        Desc = "ctx.request.method is corrupted" } ]

// ─── Process helpers (ArgumentList: no shell-quoting of test filters with spaces) ──────

let private exec (fileName: string) (args: string list) : int * string =
    let psi = ProcessStartInfo(fileName)
    psi.WorkingDirectory <- repoRoot
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    args |> List.iter psi.ArgumentList.Add
    use p = Process.Start psi
    let out = p.StandardOutput.ReadToEnd()
    let err = p.StandardError.ReadToEnd()
    p.WaitForExit()
    p.ExitCode, out + err

let private buildCli () =
    exec "dotnet" [ "build"; "src/Napper.Cli/Napper.Cli.fsproj"; "-c"; "Debug"; "--nologo" ]

let private runKillingTest (filter: string) =
    exec
        "dotnet"
        [ "test"
          "src/Napper.Core.Tests/Napper.Core.Tests.fsproj"
          "--filter"
          filter
          "--nologo" ]

// ─── Crash-safe file mutation (snapshot to <file>.mutbak, always restore) ───────────────

let private backupSuffix = ".mutbak"

let private restoreLeftovers () =
    // Recover any file a previous interrupted run left mutated.
    Directory.GetFiles(repoRoot, "*" + backupSuffix, SearchOption.AllDirectories)
    |> Array.iter (fun bak ->
        let original = bak.Substring(0, bak.Length - backupSuffix.Length)
        eprintfn "  recovering leftover backup -> %s" original
        File.Copy(bak, original, true)
        File.Delete bak)

/// Apply the mutant, run `body`, and ALWAYS restore the original file.
let private withMutation (m: Mutant) (body: unit -> 'a) : 'a =
    let path = Path.Combine(repoRoot, m.File)
    let original = File.ReadAllText path
    let count = (original.Length - original.Replace(m.Find, "").Length) / m.Find.Length

    if count <> m.Occurrences then
        failwithf
            "STALE CATALOG: mutant '%s' expected %d occurrence(s) of its Find string in %s but found %d. Update scripts/mutation-test.fsx."
            m.Id
            m.Occurrences
            m.File
            count

    let bak = path + backupSuffix
    File.WriteAllText(bak, original)

    try
        File.WriteAllText(path, original.Replace(m.Find, m.Replace))
        body ()
    finally
        File.WriteAllText(path, original)
        File.Delete bak

// ─── Run one mutant: build, test, classify ─────────────────────────────────────────────

type Outcome =
    | Killed
    | Survived
    | CompileError

let private evaluate (m: Mutant) : Outcome =
    withMutation m (fun () ->
        let buildCode, buildOut = buildCli ()

        if buildCode <> 0 then
            // A mutant that does not compile is non-viable — the compiler "killed" it.
            printfn "  (mutant does not compile — counted as killed by the compiler)"
            ignore buildOut
            CompileError
        else
            // dotnet test exits non-zero iff a test failed (xunit stopOnFail halts at the first).
            let testCode, _ = runKillingTest m.KilledBy
            if testCode <> 0 then Killed else Survived)

// ─── Main ──────────────────────────────────────────────────────────────────────────────

printfn "F# mutation testing (Stryker.NET cannot target F#) — %d mutants\n" catalog.Length
restoreLeftovers ()

let results =
    catalog
    |> List.map (fun m ->
        printf "● %-18s %s ... " m.Id m.Desc
        Console.Out.Flush()
        let outcome = evaluate m

        let label =
            match outcome with
            | Killed -> "KILLED"
            | CompileError -> "KILLED (compile error)"
            | Survived -> "SURVIVED  <-- TEST GAP"

        printfn "%s" label
        m, outcome)

let killed = results |> List.filter (fun (_, o) -> o <> Survived) |> List.length

let total = results.Length
let score = float killed / float total * 100.0
printfn "\nMutation score: %d/%d killed (%.0f%%)" killed total score

let survivors = results |> List.filter (fun (_, o) -> o = Survived)

if not (List.isEmpty survivors) then
    printfn "\nSURVIVED mutants (tests do NOT enforce these behaviours):"

    survivors
    |> List.iter (fun (m, _) -> printfn "  - %s: %s (expected kill by: %s)" m.Id m.Desc m.KilledBy)

    exit 1
else
    printfn "All mutants killed — every catalogued behaviour is enforced by a test."
    exit 0
