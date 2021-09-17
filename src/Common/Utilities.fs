namespace Lmc.KafkaApplication

module internal OptionOperators =
    open Lmc.ErrorHandling.Option.Operators

    /// Mandatory - if value is None, error will be returned
    let inline (<?!>) o errorMessage = o <?!> (sprintf "[KafkaApplicationBuilder] %s" errorMessage |> KafkaApplicationError)

[<RequireQualifiedAccess>]
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
    open Lmc.ErrorHandling

    type FilePath = string

    let parseFromPath parse notFound (path: FilePath): Result<'Parsed, 'NotFoundError> =
        result {
            if not (File.Exists(path)) then
                return! Error (notFound path)

            return parse path
        }

module internal Serializer =
    open Lmc.Serializer

    let toJson = Serialize Serialize.toJson
