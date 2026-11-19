import { describe, expect, it } from "vitest";
import {
  downsampleTo16k,
  floatToS16le,
  frameRms,
  PCM_FRAME_BYTES,
  PCM_FRAME_MS,
  PCM_FRAME_SAMPLES,
  PCM_SAMPLE_RATE,
} from "./pcm";

describe("pcm", () => {
  it("defines a 20 ms frame as 640 bytes at 16 kHz s16le", () => {
    expect(PCM_SAMPLE_RATE).toBe(16_000);
    expect(PCM_FRAME_MS).toBe(20);
    expect(PCM_FRAME_SAMPLES).toBe(320);
    expect(PCM_FRAME_BYTES).toBe(640);
  });

  it("converts float32 to little-endian s16", () => {
    const input = new Float32Array([0, 1, -1, 0.5]);
    const bytes = floatToS16le(input);
    expect(bytes.byteLength).toBe(8);
    const view = new DataView(bytes.buffer);
    expect(view.getInt16(0, true)).toBe(0);
    expect(view.getInt16(2, true)).toBe(0x7fff);
    expect(view.getInt16(4, true)).toBe(-0x8000);
    expect(view.getInt16(6, true)).toBe(Math.round(0.5 * 0x7fff));
  });

  it("downsamples 48 kHz to 16 kHz and yields 640-byte 20 ms frames", () => {
    const inputRate = 48_000;
    const durationSec = PCM_FRAME_MS / 1000;
    const input = new Float32Array(Math.round(inputRate * durationSec));
    for (let i = 0; i < input.length; i++) {
      input[i] = Math.sin((2 * Math.PI * 440 * i) / inputRate);
    }

    const down = downsampleTo16k(input, inputRate);
    expect(down.length).toBe(PCM_FRAME_SAMPLES);

    const pcm = floatToS16le(down);
    expect(pcm.byteLength).toBe(PCM_FRAME_BYTES);
  });

  it("computes RMS of a constant frame", () => {
    const samples = new Float32Array(100).fill(0.5);
    expect(frameRms(samples)).toBeCloseTo(0.5, 5);
  });
});
