// ========================================================================================================
// === F# / Project fake build ==================================================================== 1.3.0 =
// --------------------------------------------------------------------------------------------------------
// Options:
//  - no-clean   - disables clean of dirs in the first step (required on CI)
//  - no-lint    - lint will be executed, but the result is not validated
// ========================================================================================================

open Fake.Core
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators

open ProjectBuild
open Utils

[<EntryPoint>]
let main args =
    args |> Args.init

    Targets.init {
        Project = {
            Name = "Alma.KafkaApplication"
            Summary = "Framework for kafka application. It contains computation expressions to help with building this kind of application and have in-build metrics, logging, parsing, etc.."
            Git = Git.init ()
        }
        Specs =
            Spec.defaultLibrary
            |> Spec.mapLibrary (fun library -> { library with NugetApi = NugetApi.Organization "almacareer" })
    }

    args |> Args.run
