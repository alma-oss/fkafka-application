// Learn more about F# at http://fsharp.org

open System
open Microsoft.Extensions.Logging
open Alma.Logging
open Alma.KafkaApplication
open Alma.ErrorHandling

let tee f a =
    f a
    a

[<EntryPoint>]
let main argv =
    let envFiles = [ "./.env" ]

    let loggerFactory =
        envFiles
        |> LoggerFactory.common {
            Instance = "INSTANCE"
            LogTo = "LOG_TO"
            Verbosity = "VERBOSITY"
            LoggerTags = "LOGGER_TAGS"
            EnableTraceProvider = true
        }
        |> Result.orFail

    // printfn "Wait 10s before start ..."
    // System.Threading.Thread.Sleep 10_000

    RealLifeExample.Program.run envFiles loggerFactory
    |> ApplicationShutdown.withStatusCodeAndLogResult loggerFactory
