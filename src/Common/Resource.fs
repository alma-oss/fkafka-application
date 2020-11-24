namespace Lmc.KafkaApplication

module internal ResourceChecker =
    open Lmc.Kafka
    open Lmc.Metrics
    open Lmc.ServiceIdentification

    let private kafkaClusterResource brokerList = ResourceAvailability.createFromStrings "kafka_cluster" brokerList brokerList Audience.Sys

    let private kafkaTopicResource brokerList = function
        | (StreamName topic) ->
            ResourceAvailability.createFromStrings "kafka_topic" topic brokerList Audience.Sys
        | Instance topic ->
            ResourceAvailability.createForServiceFromStrings "kafka_topic" (topic |> Instance.concat "-") brokerList topic Audience.Sys

    let private updateResourceStatus instance (resource: ResourceAvailability) = function
        | true -> ResourceAvailability.enable instance resource |> ignore
        | false -> ResourceAvailability.disable instance resource |> ignore

    let updateResourceStatusOnCheck instance (BrokerList brokerList) kafkaChecker: Checker =
        let kafkaClusterResource = kafkaClusterResource brokerList
        let kafkaTopicResource = kafkaTopicResource brokerList

        {
            kafkaChecker with
                CheckCluster = (kafkaChecker.CheckCluster >> tee (updateResourceStatus instance kafkaClusterResource))
                CheckTopic = (fun topic -> kafkaChecker.CheckTopic topic >> tee (updateResourceStatus instance (kafkaTopicResource topic)))
        }

    let updateResourceStatusOnIntervalCheck instance (BrokerList brokerList) kafkaIntervalChecker: IntervalChecker =
        let kafkaClusterResource = kafkaClusterResource brokerList
        let kafkaTopicResource = kafkaTopicResource brokerList

        {
            kafkaIntervalChecker with
                ClusterHandler = updateResourceStatus instance kafkaClusterResource
                TopicHandler = kafkaTopicResource >> updateResourceStatus instance
        }
