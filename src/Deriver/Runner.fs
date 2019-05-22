namespace KafkaApplication.Deriver

module DeriverRunner =
    let runDeriver<'InputEvent, 'OutputEvent> run (DeriverApplication application: DeriverApplication<'InputEvent, 'OutputEvent>): unit =
        match application with
        | Ok deriverApplication -> deriverApplication.Application |> run
        | Error error -> failwithf "[Deriver Application] Error:\n%A" error
