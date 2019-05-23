namespace KafkaApplication

module internal Serializer =
    module private Json =
        open Newtonsoft.Json

        let serialize obj =
            JsonConvert.SerializeObject obj

    let serialize = Serialize Json.serialize
