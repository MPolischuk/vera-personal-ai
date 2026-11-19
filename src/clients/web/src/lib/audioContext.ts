/** Shared AudioContext for capture and playback (design §7 / PT-14 D2). */

let sharedContext: AudioContext | null = null;

/** Lazy singleton — do not create on module import (Safari). */
export function getSharedAudioContext(): AudioContext {
  if (!sharedContext) {
    sharedContext = new AudioContext();
  }
  return sharedContext;
}

/** Call from a user gesture (mic toggle) before capture / playback. */
export async function resumeSharedAudioContext(): Promise<AudioContext> {
  const ctx = getSharedAudioContext();
  if (ctx.state !== "running") {
    await ctx.resume();
  }
  return ctx;
}
