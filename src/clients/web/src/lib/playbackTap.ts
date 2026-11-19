/**
 * Playback graph tap for lip-sync analysis.
 * Sources connect to a Gain that fans out to the speakers and a MediaStream.
 * Speech energy is the lip-sync engine volume, not a second analyser.
 */

interface PlaybackTap {
  gain: GainNode;
  analysis: MediaStreamAudioDestinationNode;
}

const taps = new WeakMap<AudioContext, PlaybackTap>();

function ensureTap(ctx: AudioContext): PlaybackTap {
  let tap = taps.get(ctx);
  if (!tap) {
    const gain = ctx.createGain();
    const analysis = ctx.createMediaStreamDestination();
    gain.connect(ctx.destination);
    gain.connect(analysis);
    tap = { gain, analysis };
    taps.set(ctx, tap);
  }
  return tap;
}

/**
 * Node that playback sources should connect to.
 * Fakes without `createGain` (Vitest) fall back to `destination`.
 */
export function getPlaybackInput(ctx: AudioContext): AudioNode {
  if (typeof ctx.createGain !== "function") {
    return ctx.destination;
  }
  return ensureTap(ctx).gain;
}

/** MediaStream of the same audio that reaches the speakers (analysis only). */
export function getPlaybackAnalysisStream(ctx: AudioContext): MediaStream {
  return ensureTap(ctx).analysis.stream;
}
