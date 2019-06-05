namespace KafkaApplication

type Context = string
type Message = string
type Log = Context -> Message -> unit

type ApplicationLogger = {
    Debug: Log
    Log: Log
    Verbose: Log
    VeryVerbose: Log
    Warning: Log
    Error: Log
}

module ApplicationLogger =
    open Logging
    open ServiceIdentification

    let quietLogger =
        let ignore: Log = fun _ _ -> ()
        {
            Debug = ignore
            Log = ignore
            Verbose = ignore
            VeryVerbose = ignore
            Warning = ignore
            Error = ignore
        }

    let defaultLogger =
        {
            Debug = Log.debug
            Log = Log.normal
            Verbose = Log.verbose
            VeryVerbose = Log.veryVerbose
            Warning = Log.warning
            Error = Log.error
        }

    let graylogLogger instance host =
        let logger =
            instance
            |> Instance.concat "-"
            |> Graylog.Facility
            |> Graylog.Configuration.createDefault host
            |> Graylog.Logger.create
            |> Graylog.Logger.withArgs

        let createMessage (message: string) =
            message
                .Replace("{", "(")
                .Replace("}", ")")
            |> sprintf "[{context}] %s"

        {
            Debug = fun context message ->
                if Log.isDebug() then
                    logger.Debug(createMessage message, context)

            Log = fun context message ->
                logger.Info(createMessage message, context)

            Verbose = fun context message ->
                if Log.isVerbose() then
                    logger.Info(createMessage message, context)

            VeryVerbose = fun context message ->
                if Log.isVeryVerbose() then
                    logger.Info(createMessage message, context)

            Warning = fun context message ->
                logger.Warning(createMessage message, context)

            Error = fun context message ->
                logger.Error(createMessage message, context)
        }

    /// Compose two functions with 2 parameters and returning unit
    let private (>*>) (f1: 'a -> 'b -> unit) (f2: 'a -> 'b -> unit) a =
        tee (f1 a)
        >> f2 a

    let combine logger additionalLogger =
        {
            Debug = logger.Debug >*> additionalLogger.Debug
            Log = logger.Log >*> additionalLogger.Log
            Verbose = logger.Verbose >*> additionalLogger.Verbose
            VeryVerbose = logger.VeryVerbose >*> additionalLogger.VeryVerbose
            Warning = logger.Warning >*> additionalLogger.Warning
            Error = logger.Error >*> additionalLogger.Error
        }