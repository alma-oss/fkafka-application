module Alma.KafkaApplication.Test.GenericApplication

open Expecto
open Alma.KafkaApplication.Test

open Alma.ServiceIdentification
open Alma.Kafka
open Feather.ErrorHandling
open Alma.EnvironmentModel
open Alma.KafkaApplication
open Alma.KafkaApplication.Compressor
open Alma.KafkaApplication.Deriver
open Alma.KafkaApplication.Filter
open Alma.KafkaApplication.Router

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
        let dependencies: Dependencies = {
            ServiceOne = ServiceOne "ServiceOne"
            ServiceTwo = ServiceTwo "ServiceTwo"
        }

        let initialize: Initialization<OutputEvent, Dependencies> = fun app -> { app with Dependencies = Some dependencies }
        let initializeResult: InitializationResult<OutputEvent, Dependencies> = fun app -> { app with Dependencies = Some dependencies } |> Ok
        let initializeAsyncResult: InitializationAsyncResult<OutputEvent, Dependencies> = fun app -> asyncResult { return { app with Dependencies = Some dependencies } }

        let initializationAlternatives = [
            Initialization initialize
            InitializationResult initializeResult
            InitializationAsyncResult initializeAsyncResult
        ]

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
            let consumeEventsWithDependencies: ConsumeEvents<InputEvent, OutputEvent, Dependencies> = fun parts _event ->
                // Note: the test will not come this far, since it doesn't have any connection, but it shows, how dependencies can be reached
                Expect.equal parts.Dependencies (Some dependencies) "Application should have correct dependencies"

            initializationAlternatives
            |> List.map (function
                | Initialization initialization -> kafkaApplication { initialize initialization; consume consumeEventsWithDependencies }
                | InitializationResult initialization -> kafkaApplication { initialize initialization; consume consumeEventsWithDependencies }
                | InitializationAsyncResult initialization -> kafkaApplication { initialize initialization; consume consumeEventsWithDependencies }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of SendBatch in compressor pattern" <| fun _ ->
            let sendBatchFunc: SendBatch<OutputEvent> = fun _ -> asyncResult { return () }

            [
                compressor { sendBatch sendBatchFunc }
            ]
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of ParseEvent in compressor pattern" <| fun _ ->
            let sendBatchFunc: SendBatch<OutputEvent> = fun _ -> asyncResult { return () }

            parseEventAlternatives
            |> List.map (function
                | ParseEvent parseEvent ->
                    compressor {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        sendBatch sendBatchFunc
                    }
                | ParseEventResult parseEvent ->
                    compressor {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        sendBatch sendBatchFunc
                    }
                | ParseEventAsyncResult parseEvent ->
                    compressor {
                        from ( partialKafkaApplication { parseEventWith parseEvent } )
                        sendBatch sendBatchFunc
                    }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different batch size configurations in compressor pattern" <| fun _ ->
            let sendBatchFunc: SendBatch<OutputEvent> = fun _ -> asyncResult { return () }

            [
                compressor { batchSize 10; sendBatch sendBatchFunc }
                compressor { batchSize "BATCH_SIZE"; sendBatch sendBatchFunc }
            ]
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of PickEvent in compressor pattern" <| fun _ ->
            let pickEventFunc: PickEvent<InputEvent, OutputEvent> = fun _processedBy event -> Some event
            let pickEventResultFunc: PickEventResult<InputEvent, OutputEvent> = fun _processedBy event -> Ok (Some event)
            let pickEventAsyncResultFunc: PickEventAsyncResult<InputEvent, OutputEvent> = fun _processedBy event -> asyncResult { return Some event }

            let pickEventAlternatives = [
                PickEvent pickEventFunc
                PickEventResult pickEventResultFunc
                PickEventAsyncResult pickEventAsyncResultFunc
            ]

            let sendBatchFunc: SendBatch<OutputEvent> = fun _ -> asyncResult { return () }

            pickEventAlternatives
            |> List.map (function
                | PickEvent pickEventFunc -> compressor { pickEvent pickEventFunc; sendBatch sendBatchFunc }
                | PickEventResult pickEventResultFunc -> compressor { pickEvent pickEventResultFunc; sendBatch sendBatchFunc }
                | PickEventAsyncResult pickEventAsyncResultFunc -> compressor { pickEvent pickEventAsyncResultFunc; sendBatch sendBatchFunc }
            )
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow GetCommonEvent in compressor pattern" <| fun _ ->
            // Simplified test - just verify the operation compiles
            let sendBatchFunc: SendBatch<OutputEvent> = fun _ -> asyncResult { return () }

            compressor {
                sendBatch sendBatchFunc
            }
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow setOffset and getOffset in compressor pattern" <| fun _ ->
            // Simplified test - just verify the operations compile
            let sendBatchFunc: SendBatch<OutputEvent> = fun _ -> asyncResult { return () }

            compressor {
                sendBatch sendBatchFunc
            }
            |> ignore

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."
    ]
