import { describe, expect, it } from "vitest";
import {
  attackWeight,
  cancel,
  createEnvelopeSlot,
  envelopeWeight,
} from "./envelope";

const FRAME_MS = 1000 / 60;
const MAX_STEP = 0.08;

describe("attackWeight", () => {
  it("is 0 at the start and 1 at the end", () => {
    expect(attackWeight(0)).toBe(0);
    expect(attackWeight(1)).toBe(1);
  });
});

describe("envelope cancel", () => {
  it("moves less than 0.08 per frame at 60 Hz", () => {
    const durationMs = 1_600;
    const probe = createEnvelopeSlot(0, durationMs);
    const holdEnd = Math.max(probe.attackMs, probe.durationMs - probe.releaseMs);
    expect(holdEnd).toBeGreaterThan(probe.attackMs);

    let sawAttack = false;
    let sawHold = false;

    for (let i = 0; i < 20; i += 1) {
      const slot = createEnvelopeSlot(0, durationMs);
      const atMs = ((i + 0.5) / 20) * holdEnd;
      const before = envelopeWeight(slot, atMs);
      if (slot.phase === "attack") {
        sawAttack = true;
      }
      if (slot.phase === "hold") {
        sawHold = true;
      }

      cancel(slot, atMs);
      expect(Math.abs(envelopeWeight(slot, atMs) - before)).toBeLessThan(1e-9);

      let previous = before;
      let nowMs = atMs;
      for (let frame = 0; frame < 180; frame += 1) {
        nowMs += FRAME_MS;
        const weight = envelopeWeight(slot, nowMs);
        expect(Math.abs(weight - previous)).toBeLessThan(MAX_STEP);
        previous = weight;
        if (slot.phase === "off") {
          break;
        }
      }
      expect(slot.phase).toBe("off");
    }

    expect(sawAttack).toBe(true);
    expect(sawHold).toBe(true);
  });
});
