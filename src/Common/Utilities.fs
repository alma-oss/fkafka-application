namespace KafkaApplication

[<AutoOpen>]
module Utilities =
    open System

    let tee f a =
        f a
        a

    let wait (seconds: int<second>) =
        Threading.Thread.Sleep(TimeSpan.FromSeconds (float seconds))

    let logApplicationError context error =
        error
        |> sprintf "[%s] Error:\n%A" context
        |> tee (printfn "%s")
        |> tee (eprintfn "%s")

module internal OptionOperators =
    /// Default value - if value is None, default value will be used
    let (<?=>) defaultValue opt = Option.defaultValue opt defaultValue

    /// Or else - if value is None, other option will be used
    let (<??>) other opt = Option.orElse opt other

    /// Mandatory - if value is None, error will be returned
    let (<?!>) (opt: 'a option) (errorMessage: string): Result<'a, KafkaApplicationError> =
        match opt with
        | Some value -> Ok value
        | None -> sprintf "[KafkaApplicationBuilder] %s" errorMessage |> KafkaApplicationError |> Result.Error

    /// Map action with side-effect and ignore the unit option result
    let (|>!) (opt: 'a option) (action: 'a -> unit) =
        opt
        |> Option.map action
        |> ignore

module internal Map =
    /// Merge new values with the current values (replacing already defined values).
    let merge currentValues newValues =
        currentValues
        |> Map.fold (fun merged name connection ->
            if merged |> Map.containsKey name then merged
            else merged.Add(name, connection)
        ) newValues

module internal FileParser =
    open System.IO

    type FilePath = string

    let parseFromPath parse notFound (path: FilePath): Result<'Parsed, 'NotFoundError> =
        result {
            if not (File.Exists(path)) then
                return! Error (notFound path)

            return parse path
        }

module internal Serializer =
    module private Json =
        open Newtonsoft.Json

        let serialize obj =
            JsonConvert.SerializeObject obj

    let toJson = Serialize Json.serialize
