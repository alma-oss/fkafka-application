// Learn more about F# at http://fsharp.org

open System
open MF.ConsoleStyle

[<EntryPoint>]
let main argv =
    Console.title "Admin - kafka"

    match argv with
    | [|"reallife"|] ->
        Console.section "Real-life example"
        RealLifeExample.Program.run()
    | _ ->
        Console.section "Dummy example"
        DummyExample.Program.run()

    Console.success "Done"
    0 // return an integer exit code
