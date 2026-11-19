namespace Assistant.Providers.Speech;

internal static class SpeechCost
{
    public const int PcmSampleRateHz = 16_000;
    public const int PcmBytesPerSample = 2;

    public static double PcmBytesToSeconds(long byteCount)
        => byteCount / (double)(PcmSampleRateHz * PcmBytesPerSample);

    public static decimal ForStt(double seconds, decimal pricePerMinuteUsd)
        => decimal.Round((decimal)(seconds / 60.0) * pricePerMinuteUsd, 6, MidpointRounding.AwayFromZero);

    public static decimal ForTts(int characters, decimal pricePerMillionCharactersUsd)
        => decimal.Round(
            characters / 1_000_000m * pricePerMillionCharactersUsd,
            6,
            MidpointRounding.AwayFromZero);
}
