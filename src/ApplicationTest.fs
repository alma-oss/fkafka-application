namespace KafkaApplication

module ResourceAvailabilityComputedExpression =
    open Metrics

    let checkResource check resource =
        match check resource with
        | Up -> Available resource
        | Down -> NotAvailable resource

    type ResourceAvailabilityBuilder internal () =
        member __.Bind(x, f) =
            checkResource f x

        member __.Return(f, x) =
            checkResource f x

        member __.Do() =
            ()

        //member __.Combine() =
        // todo - this will combine results (if any of them is Down, all are down)
        //    ResourceAvailabilityMonad

    let resourceAvailability = ResourceAvailabilityBuilder()

// ----------------------

//module KafkaResource =
//    let kafka (_: ResourceAvailability) =
//        Up

module Computations =
    [<Measure>] type second

    type ApplicationWithResources = {
        IsUp: bool
    }
    with
        member __.Run(checkers) =
            printfn "Run app - whatever it means .. :D"

    type Resource = {
        Name: string
    }

    type ResourceStatus =
        | Up
        | Down

    type ResourceWithStatus = {
        Resource: Resource
        Status: ResourceStatus
    }

    type ResourceState = {
        // todo - later this should be Resources = ResourcesGroup list to allow multiple groups
        // kafka "kfall-dev1" ==> kafkaTopic "kfall-dev1" inputStream       // group 1
        // kafka "kfall-dev1" ==> kafkaTopics "kfall-dev1" outputStreams    // group 2
        //
        // there should be a "sequential" evulation in every group and the computed result should be the total status of all resources,
        // yet it must return whole state (or something) to allow checkInInterval and map methods above them

        Resources: Resource list
        Statuses: ResourceWithStatus list
    } with
        member state.Add resource =
            printfn " -- State.Add %A --" resource
            { state with Resources = state.Resources @ [ resource ] |> List.distinct }

        member state.GetStatus() =
            match state.Statuses with
            | [] -> Down
            | resourceGroup ->
                resourceGroup
                |> List.fold (
                    fun acc res ->
                        printfn " -- State.GetStatus %A (acc: %A) --" res acc
                        match (acc, res.Status) with
                        | Up, Up -> Up
                        | _, _ -> Down
                ) Up

    let checkResource check resource =
        { Resource = resource; Status = check resource }

    type ResourcesBuilder internal () =

        member __.Yield(_) =
            printfn " -- Yield: New empty resource state --"
            {
                Resources = []
                Statuses = []
            }

        member __.Run(state: ResourceState)    =
            state.GetStatus()
            |> printfn " -- Run: Current resources state <%A> --"
            state

        //member __.Bind(resource, f) =
        //    printfn " -- Bind: Resource<%A> and f<%A> --" resource f
        //    f resource

        //member __.Return(resource) =
        //    printfn " -- Return: Resource<%A> --" resource
        //    { Resource = resource; Status = Up }

        //member __.Zero(resource) =
        //    printfn " -- Zero: Resource<%A> --" resource
        //    { Resource = resource; Status = Down }

        ///Adds handler for `kafka` cluster check.
        [<CustomOperation("kafka")>]
        member __.Kafka((state: ResourceState), resource) : ResourceState =
            state.Add resource

    let resources = ResourcesBuilder()

    let checkAvailability resource =
        {
            Resource = resource
            Status = Up             // todo :)
        }

    let checkStatuses resourceState =
        { resourceState with
            Statuses =
                resourceState.Resources
                |> List.map checkAvailability
        }

    let checkInInterval (interval: int<second>) (resourceState: ResourceState) =
        async {
            while true do
                resourceState
                |> checkStatuses
                |> ignore // todo - this should be propagated somewhere (to metrics, and to change the "state")

                do! Async.Sleep (int interval)
        }
        |> Async.Start

    type Checker = Resource -> ResourceStatus

    type Checkers = {
        KafkaChecker: Checker
        KafkaTopicChecker: Checker
        KafkaTopicsChecker: Checker
    }

    let run checkers (applicationWithResources: ApplicationWithResources) =
        applicationWithResources.Run(checkers)

module Example =
    //open ResourceAvailability
    open Computations
    //open KafkaResource

    let resources () =
        //let kafkaCluster = createFromStrings "kafka" "kfall-dev1" "kfall-dev1" Audience.Sys
        let kafkaCluster = { Name = "kfall-dev1" }

        //let kafka (_: Resource) =
        //    Down

        let x = resources {
            kafka kafkaCluster
        }

        x
        |> printfn "\n===================\n%A"

        ()
