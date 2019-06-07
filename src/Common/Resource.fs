namespace KafkaApplication

module ResourceChecker =
    open Kafka
    open Metrics
    open ServiceIdentification

    let updateResourceStatusOnCheck instance (BrokerList brokerList) kafkaChecker: Kafka.Checker =
        let kafkaClusterResource = ResourceAvailability.createFromStrings "kafka_cluster" brokerList brokerList Audience.Sys
        let kafkaTopicResource = function
            | (StreamName topic) ->
                ResourceAvailability.createFromStrings "kafka_topic" topic brokerList Audience.Sys
            | Instance topic ->
                ResourceAvailability.createForServiceFromStrings "kafka_topic" (topic |> Instance.concat "-") brokerList topic Audience.Sys

        let updateResourceStatus (resource: ResourceAvailability) = function
            | true -> ResourceAvailability.enable instance resource |> ignore
            | false -> ResourceAvailability.disable instance resource |> ignore

        {
            kafkaChecker with
                CheckCluster = (kafkaChecker.CheckCluster >> tee (updateResourceStatus kafkaClusterResource))
                CheckTopic = (fun topic -> kafkaChecker.CheckTopic topic >> tee (updateResourceStatus (kafkaTopicResource topic)))
        }
