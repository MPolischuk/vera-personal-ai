import { describe, expect, it } from "vitest";
import { PCM_FRAME_BYTES, PCM_FRAME_MS } from "../../lib/pcm";
import {
  createPcmPreRoll,
  PRE_ROLL_FRAMES,
  PRE_ROLL_MS,
} from "./pcmPreRoll";

function frameWithMarker(marker: number): Uint8Array {
  const pcm = new Uint8Array(PCM_FRAME_BYTES);
  pcm[0] = marker;
  return pcm;
}

describe("pcm pre-roll", () => {
  it("keeps 300 ms of 20 ms frames", () => {
    expect(PRE_ROLL_MS).toBe(300);
    expect(PRE_ROLL_FRAMES).toBe(PRE_ROLL_MS / PCM_FRAME_MS);
    expect(PRE_ROLL_FRAMES).toBe(15);
  });

  it("drops the oldest frame once the ring is full", () => {
    const roll = createPcmPreRoll(3);
    roll.push(frameWithMarker(1));
    roll.push(frameWithMarker(2));
    roll.push(frameWithMarker(3));
    roll.push(frameWithMarker(4));
    expect(roll.size).toBe(3);

    const drained = roll.drain();
    expect(drained).not.toBeNull();
    expect(drained!.byteLength).toBe(PCM_FRAME_BYTES * 3);
    expect(drained![0]).toBe(2);
    expect(drained![PCM_FRAME_BYTES]).toBe(3);
    expect(drained![PCM_FRAME_BYTES * 2]).toBe(4);
    expect(roll.size).toBe(0);
  });

  it("does not alias the caller's buffer", () => {
    const roll = createPcmPreRoll(1);
    const source = frameWithMarker(9);
    roll.push(source);
    source[0] = 0;
    const drained = roll.drain();
    expect(drained![0]).toBe(9);
  });

  it("drain of an empty ring returns null", () => {
    const roll = createPcmPreRoll(2);
    expect(roll.drain()).toBeNull();
  });
});
