#load ".fake/build.fsx/intellisense.fsx"
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators

// --------------------------------------------------------------------------------------
// Information about the project to be used at NuGet and in AssemblyInfo files
// --------------------------------------------------------------------------------------

let project = "KafkaApplication"
let summary = "Framework for kafka application. It contains computation expressions to help with building this kind of application and have in-build metrics, logging, parsing, etc.."

let release = ReleaseNotes.parse (System.IO.File.ReadAllLines "CHANGELOG.md" |> Seq.filter ((<>) "## Unreleased"))

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

Target.create "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [
            AssemblyInfo.Title projectName
            AssemblyInfo.Product project
            AssemblyInfo.Description summary
            AssemblyInfo.Version release.AssemblyVersion
            AssemblyInfo.FileVersion release.AssemblyVersion
            AssemblyInfo.InternalsVisibleTo "tests"
        ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        (
            projectPath,
            projectName,
            System.IO.Path.GetDirectoryName(projectPath),
            (getAssemblyInfoAttributes projectName)
        )

    !! "**/*.*proj"
    -- "example/**/*.*proj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, _, folderName, attributes) ->
        match projFileName with
        | proj when proj.EndsWith("fsproj") -> AssemblyInfoFile.createFSharp (folderName </> "AssemblyInfo.fs") attributes
        | _ -> ()
    )
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
    DotnetCore.runOrFail "run" "tests"
)

Target.create "Watch" (fun _ ->
    DotnetCore.runInSrcOrFail "watch run"
)

"Clean"
    ==> "AssemblyInfo"
    ==> "Build"
    ==> "Lint"
    ==> "Tests"
    ==> "Release" <=> "Watch"

Target.runOrDefaultWithArguments "Build"
