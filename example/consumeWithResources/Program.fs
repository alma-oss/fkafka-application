// Learn more about F# at http://fsharp.org

open System
open MF.ConsoleStyle

[<EntryPoint>]
let main argv =
    Console.title "Admin - kafka"

    let argv = [|"reallife"|]

    match argv with
    | [|"reallife"|] ->
        Console.section "Real-life example"
        RealLifeWithComputedExpressionExample.Program.run()
    | [|"reallife"; "not-computed"|] ->
        Console.section "Real-life without computed expression example"
        RealLifeExample.Program.run()
    | [|"not-computed"|] ->
        Console.section "Dummy without computed expression example"
        DummyExample.Program.run()
    | _ ->
        Console.section "Dummy example"
        DummyWithComputedExpressionExample.Program.run()

    Console.success "Done"
    0 // return an integer exit code
