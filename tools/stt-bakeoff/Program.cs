using SttBakeoff;

var command = args.Length == 0 ? "help" : args[0];
switch (command)
{
    case "self-test":
        return Scoring.SelfTest();
    case "probe":
        Recorder.Probe();
        Console.WriteLine("capture device ready");
        return 0;
    case "record":
        Recorder.Run(RequireOption(args, "--sample-dir"), ReadOption(args, "--device"));
        return 0;
    case "rescore":
        return Rescore.Run(RequireOption(args, "--results"));
    case "run":
        return await Runner.RunAsync(
            RequireOption(args, "--sample-dir"),
            ReadOption(args, "--env-file"),
            ReadOption(args, "--only"),
            CancellationToken.None)
            .ConfigureAwait(false);
    default:
        Console.WriteLine(
            """
            stt-bakeoff self-test
            stt-bakeoff rescore --results <bakeoff-results.json outside the repo>
            stt-bakeoff record --sample-dir <dir outside the repo> [--device <part of the input name>]
            stt-bakeoff run --sample-dir <dir outside the repo> [--env-file <path>] [--only deepgram|voxtral-realtime|voxtral-batch|scribe|scribe-realtime|flux|openai|nemotron]

            Keys, never printed: STT_API_KEY (or DEEPGRAM_API_KEY), MISTRAL_API_KEY,
            OPENROUTER_API_KEY (or LLM_API_KEY), OPENAI_API_KEY,
            ELEVENLABS_API_KEY (or TTS_API_KEY) for Scribe Realtime.
            A missing key writes "sin clave". Nemotron stays "sin GPU" unless a later client is added.
            WAV files and bakeoff-results.json are written only under --sample-dir.
            """);
        return command == "help" ? 0 : 1;
}

static string RequireOption(string[] args, string name)
{
    return ReadOption(args, name)
        ?? throw new InvalidOperationException("Missing " + name + ".");
}

static string? ReadOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.Ordinal))
        {
            return args[i + 1];
        }
    }

    return null;
}
