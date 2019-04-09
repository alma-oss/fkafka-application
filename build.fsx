#load ".fake/build.fsx/intellisense.fsx"
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators

let runDotNet cmd workingDir =
    let result =
        DotNet.exec (DotNet.Options.withWorkingDirectory workingDir) cmd ""
    if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir

let tee f a =
    f a
    a

let sourceDir = "."

let nugetServer = sprintf "http://development-nugetserver-common-stable.service.devel1-services.consul:%i"
let apiKey = "123456"

let sources = sprintf "-s %s -s https://api.nuget.org/v3/index.json"

let nugetServerUrl p =
    match p.Context.Arguments with
    | head::_ ->
        if head.StartsWith "http" then head
        else head |> int |> nugetServer
    | _ -> failwithf "Release target requires nuget server url or port"

Target.create "Clean" (fun _ ->
    !! "**/bin"
    ++ "**/obj"
    |> Shell.cleanDirs
)

Target.create "Build" (fun p ->
    let nugetServerUrl = nugetServerUrl p

    runDotNet (sprintf "restore --no-cache %s" (sources nugetServerUrl)) sourceDir
    runDotNet "build --no-restore" sourceDir

    !! "**/*.*proj"
    -- "example/**/*.*proj"
    |> Seq.iter (DotNet.build id)
)

Target.create "Release" (fun p ->
    let nugetServerUrl = nugetServerUrl p

    runDotNet "pack" sourceDir

    let pushToNuget path =
        sourceDir
        |> runDotNet (sprintf "nuget push %s -s %s -k %s" path nugetServerUrl apiKey)

    !! "**/bin/**/*.nupkg"
    |> Seq.iter (fun path ->
        path
        |> tee pushToNuget
        |> Shell.moveFile "release"
    )
)

Target.create "Watch" (fun _ ->
    runDotNet "watch run" sourceDir
)

"Clean"
    ==> "Build"
    ==> "Release"

"Build"
    ==> "Watch"

Target.runOrDefaultWithArguments "Build"
