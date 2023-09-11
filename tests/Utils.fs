namespace Alma.KafkaApplication.Test

[<RequireQualifiedAccess>]
module internal List =
    /// see https://stackoverflow.com/questions/9213761/cartesian-product-two-lists
    let cartesian xs ys =
        xs |> List.collect (fun x -> ys |> List.map (fun y -> x, y))

    let cartesianBy f xs ys =
        xs |> List.collect (fun x -> ys |> List.map (f x))
