module Lmc.KafkaApplication.Test.GenericApplication

open Expecto
open Lmc.KafkaApplication.Test

open Lmc.ServiceIdentification
open Lmc.Kafka
open Lmc.ErrorHandling
open Lmc.EnvironmentModel
open Lmc.KafkaApplication
open Lmc.KafkaApplication.Deriver
open Lmc.KafkaApplication.Filter
open Lmc.KafkaApplication.Router

let okOrFail = function
    | Ok ok -> ok
    | Error error -> failtestf "Fail on %A" error

let instance (value: string) = Create.Instance(value) |> okOrFail
let environment = Environment.parse >> okOrFail

type InputEvent = string
type OutputEvent = string
type NoDependencies = NoDependencies

type Dependencies = {
    ServiceOne: ServiceOne
    ServiceTwo: ServiceTwo
}

and ServiceOne = ServiceOne of string
and ServiceTwo = ServiceTwo of string

[<Tests>]
let genericApplicationTest =
    testList "KafkaApplication - generic application" [
        let fromDomain: FromDomain<OutputEvent> = fun _serialize m -> MessageToProduce.create (MessageKey.Simple "", m)
        let fromDomainResult: FromDomainResult<OutputEvent> = fun _serialize m -> MessageToProduce.create (MessageKey.Simple "", m) |> Ok
        let fromDomainAsyncResult: FromDomainAsyncResult<OutputEvent> = fun _serialize m -> asyncResult { return MessageToProduce.create (MessageKey.Simple "", m) }

        let fromDomainAlternatives = [
            FromDomain fromDomain
            FromDomainResult fromDomainResult
            FromDomainAsyncResult fromDomainAsyncResult
        ]

        let parseEvent: ParseEvent<InputEvent> = id
        let parseEventResult: ParseEventResult<InputEvent> = Ok
        let parseEventAsyncResult: ParseEventAsyncResult<InputEvent> = AsyncResult.ofSuccess

        let parseEventAlternatives = [
            ParseEvent parseEvent
            ParseEventResult parseEventResult
            ParseEventAsyncResult parseEventAsyncResult
        ]

        let consumeEvents: ConsumeEvents<InputEvent, OutputEvent, NoDependencies> = fun _parts _event -> ()
        let consumeEventsResult: ConsumeEventsResult<InputEvent, OutputEvent, NoDependencies> = fun _parts _event -> Ok ()
        let consumeEventsAsyncResult: ConsumeEventsAsyncResult<InputEvent, OutputEvent, NoDependencies> = fun _parts _event -> AsyncResult.ofSuccess ()

        let consumeEventAlternatives = [
            ConsumeEvents consumeEvents
            ConsumeEventsResult consumeEventsResult
            ConsumeEventsAsyncResult consumeEventsAsyncResult
        ]

        testCase "should allow different form of FromDomain in base kafkaApplication produceTo" <| fun _ ->
            fromDomainAlternatives
            |> List.map (function
                | FromDomain fromDomain -> kafkaApplication { produceTo "output" fromDomain }
                | FromDomainResult fromDomain -> kafkaApplication { produceTo "output" fromDomain }
                | FromDomainAsyncResult fromDomain -> kafkaApplication { produceTo "output" fromDomain }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of FromDomain in base kafkaApplication produceToMany" <| fun _ ->
            fromDomainAlternatives
            |> List.map (function
                | FromDomain fromDomain -> kafkaApplication { produceToMany [ "output" ] fromDomain }
                | FromDomainResult fromDomain -> kafkaApplication { produceToMany [ "output" ] fromDomain }
                | FromDomainAsyncResult fromDomain -> kafkaApplication { produceToMany [ "output" ] fromDomain }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of ParseEvent in base kafkaApplication" <| fun _ ->
            parseEventAlternatives
            |> List.map (function
                | ParseEvent parseEvent -> kafkaApplication { parseEventWith parseEvent }
                | ParseEventResult parseEvent -> kafkaApplication { parseEventWith parseEvent }
                | ParseEventAsyncResult parseEvent -> kafkaApplication { parseEventWith parseEvent }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of ParseEvent with application in base kafkaApplication" <| fun _ ->
            parseEventAlternatives
            |> List.map (function
                | ParseEvent parseEvent -> kafkaApplication { parseEventAndUseApplicationWith (fun _app -> parseEvent) }
                | ParseEventResult parseEvent -> kafkaApplication { parseEventAndUseApplicationWith (fun _app -> parseEvent) }
                | ParseEventAsyncResult parseEvent -> kafkaApplication { parseEventAndUseApplicationWith (fun _app -> parseEvent) }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of consume with application in base kafkaApplication" <| fun _ ->
            consumeEventAlternatives
            |> List.map (function
                | ConsumeEvents consumeEvents -> kafkaApplication { consume consumeEvents }
                | ConsumeEventsResult consumeEvents -> kafkaApplication { consume consumeEvents }
                | ConsumeEventsAsyncResult consumeEvents -> kafkaApplication { consume consumeEvents }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of consumeFrom with application in base kafkaApplication" <| fun _ ->
            consumeEventAlternatives
            |> List.map (function
                | ConsumeEvents consumeEvents -> kafkaApplication { consumeFrom "input" consumeEvents }
                | ConsumeEventsResult consumeEvents -> kafkaApplication { consumeFrom "input" consumeEvents }
                | ConsumeEventsAsyncResult consumeEvents -> kafkaApplication { consumeFrom "input" consumeEvents }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of DeriveTo and FromDomain in deriver pattern" <| fun _ ->
            let deriveEvent: DeriveEvent<InputEvent, OutputEvent> = fun _processedBy event -> [ event ]
            let deriveEventResult: DeriveEventResult<InputEvent, OutputEvent> = fun _processedBy event -> Ok [ event ]
            let deriveEventAsyncResult: DeriveEventAsyncResult<InputEvent, OutputEvent> = fun _processedBy event -> AsyncResult.ofSuccess [ event ]

            let deriveAlternatives = [
                DeriveEvent deriveEvent
                DeriveEventResult deriveEventResult
                DeriveEventAsyncResult deriveEventAsyncResult
            ]

            fromDomainAlternatives
            |> List.cartesian deriveAlternatives
            |> List.map (function
                | DeriveEvent deriveEvent, FromDomain fromDomain -> deriver { deriveTo "output" deriveEvent fromDomain }
                | DeriveEvent deriveEvent, FromDomainResult fromDomain -> deriver { deriveTo "output" deriveEvent fromDomain }
                | DeriveEvent deriveEvent, FromDomainAsyncResult fromDomain -> deriver { deriveTo "output" deriveEvent fromDomain }

                | DeriveEventResult deriveEvent, FromDomain fromDomain -> deriver { deriveTo "output" deriveEvent fromDomain }
                | DeriveEventResult deriveEvent, FromDomainResult fromDomain -> deriver { deriveTo "output" deriveEvent fromDomain }
                | DeriveEventResult deriveEvent, FromDomainAsyncResult fromDomain -> deriver { deriveTo "output" deriveEvent fromDomain }

                | DeriveEventAsyncResult deriveEvent, FromDomain fromDomain -> deriver { deriveTo "output" deriveEvent fromDomain }
                | DeriveEventAsyncResult deriveEvent, FromDomainResult fromDomain -> deriver { deriveTo "output" deriveEvent fromDomain }
                | DeriveEventAsyncResult deriveEvent, FromDomainAsyncResult fromDomain -> deriver { deriveTo "output" deriveEvent fromDomain }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of DeriveTo and ParseEvent in deriver pattern" <| fun _ ->
            let deriveEvent: DeriveEvent<InputEvent, OutputEvent> = fun _processedBy event -> [ event ]
            let deriveEventResult: DeriveEventResult<InputEvent, OutputEvent> = fun _processedBy event -> Ok [ event ]
            let deriveEventAsyncResult: DeriveEventAsyncResult<InputEvent, OutputEvent> = fun _processedBy event -> AsyncResult.ofSuccess [ event ]

            let deriveAlternatives = [
                DeriveEvent deriveEvent
                DeriveEventResult deriveEventResult
                DeriveEventAsyncResult deriveEventAsyncResult
            ]

            parseEventAlternatives
            |> List.cartesian deriveAlternatives
            |> List.map (function
                | DeriveEvent deriveEvent, ParseEvent parseEvent ->
                    deriver {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        deriveTo "output" deriveEvent fromDomain
                    }
                | DeriveEvent deriveEvent, ParseEventResult parseEvent ->
                    deriver {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        deriveTo "output" deriveEvent fromDomain
                    }
                | DeriveEvent deriveEvent, ParseEventAsyncResult parseEvent ->
                    deriver {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        deriveTo "output" deriveEvent fromDomain
                    }

                | DeriveEventResult deriveEvent, ParseEvent parseEvent ->
                    deriver {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        deriveTo "output" deriveEvent fromDomain
                    }
                | DeriveEventResult deriveEvent, ParseEventResult parseEvent ->
                    deriver {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        deriveTo "output" deriveEvent fromDomain
                    }
                | DeriveEventResult deriveEvent, ParseEventAsyncResult parseEvent ->
                    deriver {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        deriveTo "output" deriveEvent fromDomain
                    }

                | DeriveEventAsyncResult deriveEvent, ParseEvent parseEvent ->
                    deriver {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        deriveTo "output" deriveEvent fromDomain
                    }
                | DeriveEventAsyncResult deriveEvent, ParseEventResult parseEvent ->
                    deriver {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        deriveTo "output" deriveEvent fromDomain
                    }
                | DeriveEventAsyncResult deriveEvent, ParseEventAsyncResult parseEvent ->
                    deriver {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        deriveTo "output" deriveEvent fromDomain
                    }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of DeriveToWithApplication and FromDomain in deriver pattern" <| fun _ ->
            let deriveEvent: DeriveEvent<InputEvent, OutputEvent> = fun _processedBy event -> [ event ]
            let deriveEventResult: DeriveEventResult<InputEvent, OutputEvent> = fun _processedBy event -> Ok [ event ]
            let deriveEventAsyncResult: DeriveEventAsyncResult<InputEvent, OutputEvent> = fun _processedBy event -> AsyncResult.ofSuccess [ event ]

            let deriveAlternatives = [
                DeriveEvent deriveEvent
                DeriveEventResult deriveEventResult
                DeriveEventAsyncResult deriveEventAsyncResult
            ]

            fromDomainAlternatives
            |> List.cartesian deriveAlternatives
            |> List.map (function
                | DeriveEvent deriveEvent, FromDomain fromDomain -> deriver { deriveToWithApplication "output" (fun _app -> deriveEvent) fromDomain }
                | DeriveEvent deriveEvent, FromDomainResult fromDomain -> deriver { deriveToWithApplication "output" (fun _app -> deriveEvent) fromDomain }
                | DeriveEvent deriveEvent, FromDomainAsyncResult fromDomain -> deriver { deriveToWithApplication "output" (fun _app -> deriveEvent) fromDomain }

                | DeriveEventResult deriveEvent, FromDomain fromDomain -> deriver { deriveToWithApplication "output" (fun _app -> deriveEvent) fromDomain }
                | DeriveEventResult deriveEvent, FromDomainResult fromDomain -> deriver { deriveToWithApplication "output" (fun _app -> deriveEvent) fromDomain }
                | DeriveEventResult deriveEvent, FromDomainAsyncResult fromDomain -> deriver { deriveToWithApplication "output" (fun _app -> deriveEvent) fromDomain }

                | DeriveEventAsyncResult deriveEvent, FromDomain fromDomain -> deriver { deriveToWithApplication "output" (fun _app -> deriveEvent) fromDomain }
                | DeriveEventAsyncResult deriveEvent, FromDomainResult fromDomain -> deriver { deriveToWithApplication "output" (fun _app -> deriveEvent) fromDomain }
                | DeriveEventAsyncResult deriveEvent, FromDomainAsyncResult fromDomain -> deriver { deriveToWithApplication "output" (fun _app -> deriveEvent) fromDomain }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of FromDomain in filter pattern" <| fun _ ->
            let filterContent: FilterContent<InputEvent, OutputEvent> = fun _processedBy event -> Some event

            fromDomainAlternatives
            |> List.map (function
                | FromDomain fromDomain -> filterContentFilter { filterTo "output" filterContent fromDomain }
                | FromDomainResult fromDomain -> filterContentFilter { filterTo "output" filterContent fromDomain }
                | FromDomainAsyncResult fromDomain -> filterContentFilter { filterTo "output" filterContent fromDomain }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of FromDomain in router pattern" <| fun _ ->
            fromDomainAlternatives
            |> List.map (function
                | FromDomain fromDomain -> contentBasedRouter { routeToBrokerFromEnv "output" fromDomain }
                | FromDomainResult fromDomain -> contentBasedRouter { routeToBrokerFromEnv "output" fromDomain }
                | FromDomainAsyncResult fromDomain -> contentBasedRouter { routeToBrokerFromEnv "output" fromDomain }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow initialize function which defines application dependencies" <| fun _ ->
            let dependencies: Dependencies = {
                ServiceOne = ServiceOne "ServiceOne"
                ServiceTwo = ServiceTwo "ServiceTwo"
            }

            let consumeEventsWithDependencies: ConsumeEvents<InputEvent, OutputEvent, Dependencies> = fun parts _event ->
                // Note: the test will not come this far, since it doesn't have any connection, but it shows, how dependencies can be reached
                Expect.equal parts.Dependencies (Some dependencies) "Application should have correct dependencies"

            let app: Application<InputEvent, OutputEvent, Dependencies, _> = kafkaApplication {
                initialize (fun app ->
                    { app with Dependencies = Some dependencies }
                )
                consume consumeEventsWithDependencies
            }

            ignore app
            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."
    ]
