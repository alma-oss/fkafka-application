namespace Lmc.KafkaApplication

type Serialize = Serialize of (obj -> string)

[<AutoOpen>]
module internal Utils =
    let tee f a =
        f a
        a

    [<RequireQualifiedAccess>]
    module Map =
        /// Merge new values with the current values (replacing already defined values).
        let merge currentValues newValues =
            currentValues
            |> Map.fold (fun merged name connection ->
                if merged |> Map.containsKey name then merged
                else merged.Add(name, connection)
            ) newValues

    module FileParser =
        open System.IO
        open Lmc.ErrorHandling

        type FilePath = string

        let parseFromPath parse notFound (path: FilePath): Result<'Parsed, 'Error> =
            result {
                if not (File.Exists(path)) then
                    return! Error (notFound path)

                return! parse path
            }

    module Serializer =
        open Lmc.Serializer

        let toJson = Serialize Serialize.toJson
