// Learn more about F# at http://fsharp.org

open System
open Metrics
open MF.ConsoleStyle
open Metrics

[<EntryPoint>]
let main argv =
    Console.title "Admin - kafka"

    Example.resources()

    Console.success "Done"
    0 // return an integer exit code
