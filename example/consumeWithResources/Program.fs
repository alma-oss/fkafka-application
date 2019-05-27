// Learn more about F# at http://fsharp.org

open System
open MF.ConsoleStyle
open KafkaApplication

[<EntryPoint>]
let main argv =
    Console.title "Admin - kafka"

    let argv = [|"reallife"|]

    match argv with
    | [|"reallife"|] ->
        Console.section "Real-life example"
        RealLifeWithComputationExpressionExample.Program.run()
    | [|"reallife"; "not-computation"|] ->
        Console.section "Real-life without computation expression example"
        RealLifeExample.Program.run()
    | [|"not-computation"|] ->
        Console.section "Dummy without computation expression example"
        DummyExample.Program.run()
    | _ ->
        Console.section "Dummy example"
        DummyWithComputationExpressionExample.Program.run()
    |> function
        | Successfully ->
            Console.success "Done"
            0
        | _ -> 1
