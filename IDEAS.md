Ideas
=====

## Monadic InputEvent

```fs
type InputEvent<'InputEvent> =
    | Ok of 'InputEvent
    | Malformed of string

module InputEvent =
    let bind i f = 
        match i with
        | Ok input -> f input
        | Malformed message -> Malformed message

    let map i f =
        bind i (f >> Ok)

type ContentBasedRouter<'InputEvent, 'OutputEvent> = InputEvent<'InputEvent> -> 'OutputEvent
type FilterContentFilter<'InputEvent, 'OutputEvent> = InputEvent<'InputEvent> -> 'OutputEvent
type Deriver<'InputEvent, 'OutputEvent> = InputEvent<'InputEvent> -> 'OutputEvent

type Parse<'InputEvent> = string -> InputEvent<'InputEvent>
type Produce<'OutputEvent> = 'OutputEvent -> string

module Consents =
    let parse: Parse<_> =
        fun message ->
            try
                message
                |> RawEvent.parse
                |> Ok
            with
            | _ -> Malformed message

    let produce produceToOkStream produceToErrorStream: Produce<_> = function
        | Ok rawEvent -> produceToOkStream rawEvent
        | Malformed string -> produceToErrorStream string

    let consentorPipeline<'InputEvent, 'OutputEvent>
        (parse: Parse<'InputEvent>)
        (produce: Produce<'OutputEvent>)
        (router: ContentBasedRouter<'InputEvent, 'OutputEvent>) 
        (filter: FilterContentFilter<'InputEvent, 'OutputEvent>) 
        (deriver: Deriver<'InputEvent, 'OutputEvent>) 
        events =
        events
        |> Seq.map parse
        |> InputEvent.map router
        |> Seq.map (produce >> parse)
        |> InputEvent.map filter
        |> Seq.map (produce >> parse)
        |> InputEvent.map deriver
        |> Seq.map produce
```
NOTE: Neda se to takhle skompilovat, muselo by se trochu vyjasnit, co se ma stat a jak, kdyz to je malformed atd.. takze je to jen nastrel pipeliny a napadu
