/**
 * Client-side end-of-speech detection (PT-14 D5).
 * V1 = energy (RMS + continuous silence). Knobs live in vadDefaults for PT-15.
 */

export interface SpeechEndpointDetector {
  push(frame: { rms: number; tMs: number }): "continue" | "endpoint";
  reset(): void;
}

export const vadDefaults: {
  speechRmsThreshold: number;
  silenceMs: number;
  minSpeechMs: number;
  bargeInRmsThreshold: number;
} = {
  /** RMS above this counts as speech. */
  speechRmsThreshold: 0.015,
  /** Continuous silence after speech before endpoint (ms). */
  silenceMs: 800,
  /** Ignore clicks shorter than this once speech started (ms). */
  minSpeechMs: 180,
  /** Barge-in while playing; keep ≥ speech threshold (echo). */
  bargeInRmsThreshold: 0.02,
};

export type VadDefaults = typeof vadDefaults;

export function createEnergyEndpointDetector(
  options: Partial<VadDefaults> = {},
): SpeechEndpointDetector {
  const speechRmsThreshold =
    options.speechRmsThreshold ?? vadDefaults.speechRmsThreshold;
  const silenceMs = options.silenceMs ?? vadDefaults.silenceMs;
  const minSpeechMs = options.minSpeechMs ?? vadDefaults.minSpeechMs;

  let speechStartedAt: number | null = null;
  let lastSpeechAt: number | null = null;

  return {
    push(frame) {
      const isSpeech = frame.rms >= speechRmsThreshold;

      if (isSpeech) {
        if (speechStartedAt === null) {
          speechStartedAt = frame.tMs;
        }
        lastSpeechAt = frame.tMs;
        return "continue";
      }

      if (speechStartedAt === null || lastSpeechAt === null) {
        return "continue";
      }

      const spokenMs = lastSpeechAt - speechStartedAt;
      const silenceFor = frame.tMs - lastSpeechAt;

      if (spokenMs >= minSpeechMs && silenceFor >= silenceMs) {
        return "endpoint";
      }

      return "continue";
    },

    reset() {
      speechStartedAt = null;
      lastSpeechAt = null;
    },
  };
}

export function isSpeechEnergy(
  rms: number,
  threshold: number = vadDefaults.speechRmsThreshold,
): boolean {
  return rms >= threshold;
}

export function isBargeInEnergy(
  rms: number,
  threshold: number = vadDefaults.bargeInRmsThreshold,
): boolean {
  return rms >= threshold;
}
