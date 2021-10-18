// Learn more about F# at http://fsharp.org

open Microsoft.Extensions.Logging
open Lmc.Logging
open Lmc.KafkaApplication

[<EntryPoint>]
let main argv =
    use loggerFactory = LoggerFactory.create [
        LoggerOption.UseLevel LogLevel.Trace

        LoggerOption.LogToSerilog [
            SerilogOption.LogToConsole
            SerilogOption.AddMeta ("facility", "kafka-app-example")
        ]
    ]

    RealLifeExample.Program.run loggerFactory
    |> ApplicationShutdown.withStatusCodeAndLogResult loggerFactory
