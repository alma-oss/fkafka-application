namespace KafkaApplication.Pattern

open KafkaApplication

// Errors

type ApplicationConfigurationError =    // todo - common
    | ConfigurationNotSet
    | AlreadySetConfiguration
    | InvalidConfiguration of KafkaApplicationError
