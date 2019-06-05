namespace KafkaApplication

[<AutoOpen>]
module Common =
    let tee f a =
        f a
        a

    let logApplicationError context error =
        error
        |> sprintf "[%s] Error:\n%A" context
        |> tee (printfn "%s")
        |> tee (eprintfn "%s")
