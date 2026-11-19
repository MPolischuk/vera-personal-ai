namespace Assistant.Core.Speech;

/// <summary>
/// PCM s16le 16 kHz mono payload. Sample rate and encoding are port invariants (D3),
/// not fields on the chunk.
/// </summary>
public sealed record AudioChunk(int Sequence, ReadOnlyMemory<byte> Data);
