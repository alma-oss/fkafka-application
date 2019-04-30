namespace KafkaApplication

[<AutoOpen>]
module Utilities =
    let tee f a =
        f a
        a

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

module internal List =
    /// Merge new values with the current values (ignoring already defined values) if there is any new values.
    let merge currentValues newValues =
        if newValues |> List.isEmpty then currentValues
        else newValues
