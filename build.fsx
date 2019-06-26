#load ".fake/build.fsx/intellisense.fsx"
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators

// ===============================
// === F# / Library fake build ===
// ===============================

let sourceDir = "."

let tee f a =
    f a
    a

module private DotnetCore =
    let run cmd workingDir =
        let options =
            DotNet.Options.withWorkingDirectory workingDir
            >> DotNet.Options.withRedirectOutput true

        DotNet.exec options cmd ""

    let runOrFail cmd workingDir =
        run cmd workingDir
        |> tee (fun result ->
            if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir
        )
        |> ignore

    let runInSrc cmd = run cmd sourceDir
    let runInSrcOrFail cmd = runOrFail cmd sourceDir

    let installOrUpdateTool tool =
        // Global tool dir must be in PATH - ${PATH}:/root/.dotnet/tools
        let toolCommand action =
            sprintf "tool %s --global %s" action tool

        match runInSrc (toolCommand "install") with
        | { ExitCode = code } when code <> 0 -> runInSrcOrFail (toolCommand "update")
        | _ -> ()

Target.create "Clean" (fun p ->
    if p.Context.Arguments |> Seq.contains "no-clean"
    then Trace.tracefn "Clean is skipped"
    else
        !! "**/bin"
        ++ "**/obj"
        |> Shell.cleanDirs
)

Target.create "Build" (fun _ ->
    !! "**/*.*proj"
    -- "example/**/*.*proj"
    |> Seq.iter (DotNet.build id)
)

Target.create "Lint" (fun p ->
    DotnetCore.installOrUpdateTool "dotnet-fsharplint"

    let checkResult (result: ProcessResult) =
        let rec check: string list -> unit = function
            | [] -> failwithf "Lint does not yield a summary."
            | head::rest ->
                if head.Contains("Summary") then
                    match head.Replace("= ", "").Replace(" =", "").Replace("=", "").Replace("Summary: ", "") with
                    | "0 warnings" -> ()
                    | warnings ->
                        if p.Context.Arguments |> List.contains "no-lint"
                        then Trace.traceErrorfn "Lint ends up with %s." warnings
                        else failwithf "Lint ends up with %s." warnings
                else check rest
        result.Messages
        |> List.rev
        |> check

    !! "**/*.fsproj"
    |> Seq.map (sprintf "fsharplint -f %s" >> DotnetCore.runInSrc)
    |> Seq.iter checkResult
)

Target.create "Release" (fun _ ->
    DotnetCore.runInSrcOrFail "pack"

    !! "**/bin/**/*.nupkg"
    |> Seq.iter (Shell.moveFile "release")
)

Target.create "Tests" (fun _ ->
    Trace.tracefn "There are no tests yet."
)

Target.create "Watch" (fun _ ->
    DotnetCore.runInSrcOrFail "watch run"
)

"Clean"
    ==> "Build"
    ==> "Lint"
    ==> "Tests"
    ==> "Release" <=> "Watch"

Target.runOrDefaultWithArguments "Build"
