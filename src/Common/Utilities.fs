namespace KafkaApplication

module internal OptionOperators =
    open Option.Operators

    /// Default value - if value is None, default value will be used
    let inline (<?=>) o defaultValue = o <?=> defaultValue

    /// Or else - if value is None, other option will be used
    let inline (<??>) o other = o <??> other

    /// Mandatory - if value is None, error will be returned
    let inline (<?!>) o errorMessage = o <?!> (sprintf "[KafkaApplicationBuilder] %s" errorMessage |> KafkaApplicationError)

    /// Option.iter
    let inline (|>!) o f = o |>! f

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
