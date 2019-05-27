// Learn more about F# at http://fsharp.org

open System
open MF.ConsoleStyle
open KafkaApplication

[<EntryPoint>]
let main argv =
    Console.title "Admin - kafka"

    RealLifeExample.Program.run()
    |> function
        | Successfully ->
            Console.success "Done"
            0
        | _ -> 1
