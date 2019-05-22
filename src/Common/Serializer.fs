namespace KafkaApplication

module Serializer = // todo - internal?
    module private Json =
        open Newtonsoft.Json

        let serialize obj =
            JsonConvert.SerializeObject obj

    let serialize = Serialize Json.serialize
