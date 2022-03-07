// Learn more about F# at http://fsharp.org

open System
open Microsoft.Extensions.Logging
open Lmc.Logging
open Lmc.KafkaApplication
open Lmc.ErrorHandling

let tee f a =
    f a
    a

[<EntryPoint>]
let main argv =
    let envFiles = [ "./.env" ]

    use loggerFactory =
        envFiles
        |> LoggerFactory.common {
            Instance = "INSTANCE"
            LogTo = "LOG_TO"
            Verbosity = "VERBOSITY"
            LoggerTags = "LOGGER_TAGS"
            EnableTraceProvider = true
        }
        |> Result.orFail

    RealLifeExample.Program.run envFiles loggerFactory
    |> ApplicationShutdown.withStatusCodeAndLogResult loggerFactory
