module Lmc.KafkaApplication.Test.GenericApplication

open Expecto
open Lmc.ServiceIdentification
open Lmc.Kafka
open Lmc.ErrorHandling
open Lmc.KafkaApplication
open Lmc.KafkaApplication.Deriver
open Lmc.KafkaApplication.Filter
open Lmc.KafkaApplication.Router

let okOrFail = function
    | Ok ok -> ok
    | Error error -> failtestf "Fail on %A" error

let instance (value: string) = Create.Instance(value) |> okOrFail

type InputEvent = string
type OutputEvent = string

[<Tests>]
let commitMessageTest =
    testList "KafkaApplication - generic application" [
        let fromDomain: FromDomain<string> = fun _serialize m -> MessageToProduce.create (MessageKey.Simple "", m)
        let fromDomainResult: FromDomainResult<string> = fun _serialize m -> MessageToProduce.create (MessageKey.Simple "", m) |> Ok
        let fromDomainAsyncResult: FromDomainAsyncResult<string> = fun _serialize m -> asyncResult { return MessageToProduce.create (MessageKey.Simple "", m) }

        testCase "should allow different form of FromDomain in base kafkaApplication" <| fun _ ->
            let instance = instance "development-kafkaApplication-genericApplication-test"

            let _app: Application<InputEvent, OutputEvent, _> =
                kafkaApplication {
                    useInstance instance

                    produceTo "output" fromDomain
                }

            let _appWithResult: Application<InputEvent, OutputEvent, _> =
                kafkaApplication {
                    useInstance instance

                    produceTo "output" fromDomainResult
                }

            let _appWithAsyncResult: Application<InputEvent, OutputEvent, _> =
                kafkaApplication {
                    useInstance instance

                    produceTo "output" fromDomainAsyncResult
                }

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of FromDomain in deriver pattern" <| fun _ ->
            let instance = instance "development-kafkaApplication-genericApplication-test"

            let baseApp = partialKafkaApplication {
                useInstance instance
            }

            let deriveEvent: DeriveEvent<InputEvent, OutputEvent> = fun _processedBy event -> [ event ]

            let _app: Application<InputEvent, OutputEvent, _> =
                deriver {
                    from baseApp

                    deriveTo "output" deriveEvent fromDomain
                }

            let _appWithResult: Application<InputEvent, OutputEvent, _> =
                deriver {
                    from baseApp

                    deriveTo "output" deriveEvent fromDomainResult
                }

            let _appWithAsyncResult: Application<InputEvent, OutputEvent, _> =
                deriver {
                    from baseApp

                    deriveTo "output" deriveEvent fromDomainAsyncResult
                }

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of FromDomain in filter pattern" <| fun _ ->
            let instance = instance "development-kafkaApplication-genericApplication-test"

            let baseApp = partialKafkaApplication {
                useInstance instance
            }

            let filterContent: FilterContent<InputEvent, OutputEvent> = fun _processedBy event -> Some event

            let _app: Application<InputEvent, OutputEvent, _> =
                filterContentFilter {
                    from baseApp

                    filterTo "output" filterContent fromDomain
                }

            let _appWithResult: Application<InputEvent, OutputEvent, _> =
                filterContentFilter {
                    from baseApp

                    filterTo "output" filterContent fromDomainResult
                }

            let _appWithAsyncResult: Application<InputEvent, OutputEvent, _> =
                filterContentFilter {
                    from baseApp

                    filterTo "output" filterContent fromDomainAsyncResult
                }

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

        testCase "should allow different form of FromDomain in router pattern" <| fun _ ->
            let instance = instance "development-kafkaApplication-genericApplication-test"

            let baseApp = partialKafkaApplication {
                useInstance instance
            }

            let _app: Application<InputEvent, OutputEvent, _> =
                contentBasedRouter {
                    from baseApp

                    routeToBrokerFromEnv "output" fromDomain
                }

            let _appWithResult: Application<InputEvent, OutputEvent, _> =
                contentBasedRouter {
                    from baseApp

                    routeToBrokerFromEnv "output" fromDomainResult
                }

            let _appWithAsyncResult: Application<InputEvent, OutputEvent, _> =
                contentBasedRouter {
                    from baseApp

                    routeToBrokerFromEnv "output" fromDomainAsyncResult
                }

            Expect.isTrue true "This test has no other expectations, that the code compiles correctly."

    ]
