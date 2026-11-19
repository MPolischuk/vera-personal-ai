import { PCM_FRAME_MS } from "../../lib/pcm";

/**
 * How much audio to keep before the VAD speech threshold.
 * Quiet onsets (Spanish /p t k/, "qué", "che") sit below RMS until the vowel.
 */
export const PRE_ROLL_MS = 300;

export const PRE_ROLL_FRAMES = PRE_ROLL_MS / PCM_FRAME_MS;

export interface PcmPreRoll {
  push(pcm: Uint8Array): void;
  /** Concatenate buffered frames and empty the ring. */
  drain(): Uint8Array | null;
  clear(): void;
  readonly size: number;
}

export function createPcmPreRoll(maxFrames: number = PRE_ROLL_FRAMES): PcmPreRoll {
  if (maxFrames <= 0 || !Number.isInteger(maxFrames)) {
    throw new Error("maxFrames must be a positive integer.");
  }

  const frames: Uint8Array[] = [];

  return {
    push(pcm) {
      if (pcm.byteLength === 0) {
        return;
      }
      frames.push(pcm.slice());
      if (frames.length > maxFrames) {
        frames.shift();
      }
    },

    drain() {
      if (frames.length === 0) {
        return null;
      }
      let total = 0;
      for (const frame of frames) {
        total += frame.byteLength;
      }
      const out = new Uint8Array(total);
      let offset = 0;
      for (const frame of frames) {
        out.set(frame, offset);
        offset += frame.byteLength;
      }
      frames.length = 0;
      return out;
    },

    clear() {
      frames.length = 0;
    },

    get size() {
      return frames.length;
    },
  };
}
