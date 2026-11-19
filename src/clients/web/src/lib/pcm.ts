/** PCM helpers: float32 ↔ s16le and downsample to 16 kHz (PT-14 D3/D4). */

export const PCM_SAMPLE_RATE = 16_000;
/** Frame duration targeted by capture (PT-15 may retune). */
export const PCM_FRAME_MS = 20;
/** Samples per 20 ms frame at 16 kHz. */
export const PCM_FRAME_SAMPLES = (PCM_SAMPLE_RATE * PCM_FRAME_MS) / 1000;
/** Bytes per 20 ms s16le mono frame. */
export const PCM_FRAME_BYTES = PCM_FRAME_SAMPLES * 2;

export function floatToS16le(samples: Float32Array): Uint8Array {
  const out = new Uint8Array(samples.length * 2);
  const view = new DataView(out.buffer);
  for (let i = 0; i < samples.length; i++) {
    const clamped = Math.max(-1, Math.min(1, samples[i]!));
    const int16 = clamped < 0 ? Math.round(clamped * 0x8000) : Math.round(clamped * 0x7fff);
    view.setInt16(i * 2, int16, true);
  }
  return out;
}

/** Linear downsample from an arbitrary rate to 16 kHz mono. */
export function downsampleTo16k(
  input: Float32Array,
  inputSampleRate: number,
): Float32Array {
  if (inputSampleRate === PCM_SAMPLE_RATE) {
    return input.slice();
  }
  if (inputSampleRate <= 0) {
    throw new Error("inputSampleRate must be positive.");
  }

  const ratio = inputSampleRate / PCM_SAMPLE_RATE;
  const outLength = Math.floor(input.length / ratio);
  const output = new Float32Array(outLength);

  for (let i = 0; i < outLength; i++) {
    const srcIndex = i * ratio;
    const i0 = Math.floor(srcIndex);
    const i1 = Math.min(i0 + 1, input.length - 1);
    const frac = srcIndex - i0;
    const s0 = input[i0] ?? 0;
    const s1 = input[i1] ?? s0;
    output[i] = s0 + (s1 - s0) * frac;
  }

  return output;
}

/** RMS of a float32 frame (for VAD energy). */
export function frameRms(samples: Float32Array): number {
  if (samples.length === 0) {
    return 0;
  }
  let sum = 0;
  for (let i = 0; i < samples.length; i++) {
    const s = samples[i]!;
    sum += s * s;
  }
  return Math.sqrt(sum / samples.length);
}
