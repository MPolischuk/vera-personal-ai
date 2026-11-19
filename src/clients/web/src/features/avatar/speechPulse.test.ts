import { describe, expect, it } from "vitest";
import type { VRM } from "@pixiv/three-vrm";
import { VRMExpressionPresetName } from "@pixiv/three-vrm";
import { createDirector, directGestures, type DirectorCues } from "./director";
import {
  advanceSpeechEnergy,
  applyVisemeRelease,
  createSpeechEnergyState,
  explainWeightForSpeech,
  releasedVisemes,
  SPEECH_PULSE_FLOOR,
  SPEECH_PULSE_PITCH,
  SPEECH_VISEMES,
  speechPulseAt,
} from "./speechPulse";

const QUIET: DirectorCues = {
  shot: "bust",
  hubGesture: null,
  turnError: false,
  ready: false,
};

describe("speechPulseAt", () => {
  it("keeps the peak head bob between 0.03 and 0.15 rad", () => {
    const peak = speechPulseAt(1, 1, Math.PI / 2 / 1.9);
    expect(peak.pitch).toBeCloseTo((1 - SPEECH_PULSE_FLOOR) * SPEECH_PULSE_PITCH, 5);
    expect(Math.abs(peak.pitch)).toBeGreaterThanOrEqual(0.03);
    expect(Math.abs(peak.pitch)).toBeLessThanOrEqual(0.15);
    expect(Math.abs(peak.yaw)).toBeGreaterThan(0);
    expect(Math.abs(peak.yaw)).toBeLessThanOrEqual(0.15);
    expect(peak.chest).toBeCloseTo(0.02);
    expect(peak.forearm).toBeCloseTo(0.05);
  });

  it("does not nod downward in silence", () => {
    const quiet = speechPulseAt(0, 1, 1);
    expect(quiet.pitch).toBe(0);
    expect(quiet.pitch).toBeLessThanOrEqual(0);
    expect(speechPulseAt(SPEECH_PULSE_FLOOR - 0.01, 1, 1).pitch).toBe(0);
    expect(speechPulseAt(0, 1, 1)).toEqual(speechPulseAt(0, 1, 1));
  });
});

describe("speech energy envelope", () => {
  it("repeats the 300 ms envelope for the same samples", () => {
    function run(values: number[]): number[] {
      const state = createSpeechEnergyState();
      const speak: number[] = [];
      const times = [0, 300, 900];
      for (let i = 0; i < times.length; i += 1) {
        speak.push(advanceSpeechEnergy(state, values[i] ?? 0, times[i] ?? 0));
      }
      return speak;
    }

    const first = run([0, 0.5, 1]);
    const second = run([0, 0.5, 1]);
    expect(first).toEqual(second);
    expect(first[0]).toBe(0);
    expect(first[1]).toBeCloseTo(0.5 * (1 - Math.exp(-1)), 5);
  });
});

describe("explain weight", () => {
  it("stays inside 0.2 and 0.6 as speakW moves", () => {
    expect(explainWeightForSpeech(0)).toBeCloseTo(0.2);
    expect(explainWeightForSpeech(0.5)).toBeCloseTo(0.4);
    expect(explainWeightForSpeech(1)).toBeCloseTo(0.6);
    expect(explainWeightForSpeech(-1)).toBeCloseTo(0.2);
    expect(explainWeightForSpeech(2)).toBeCloseTo(0.6);

    const director = createDirector("speaking", 0);
    directGestures(director, 0, QUIET, true, 0);
    expect(director.queue.arm?.name).toBe("explain");
    expect(director.queue.arm?.weight).toBeCloseTo(0.2);
    directGestures(director, 400, QUIET, true, 1);
    expect(director.queue.arm?.weight).toBeCloseTo(0.6);
    expect(director.queue.arm?.weight).toBeGreaterThanOrEqual(0.2);
    expect(director.queue.arm?.weight).toBeLessThanOrEqual(0.6);
  });
});

describe("viseme release on the v2 path", () => {
  const open = { aa: 0.8, ih: 0.4, ou: 0.2, ee: 0.6, oh: 0.1 };

  it("is unchanged at the start and zero at 120 ms", () => {
    expect(releasedVisemes(open, 0)).toEqual(open);
    const closed = releasedVisemes(open, 120);
    for (const name of SPEECH_VISEMES) {
      expect(closed[name]).toBe(0);
    }
    expect(releasedVisemes(open, 60).aa).toBeCloseTo(0.4);
  });

  it("writes the five visemes to 0 at 120 ms", () => {
    const values = new Map<string, number>();
    const vrm = {
      expressionManager: {
        setValue(name: string, value: number) {
          values.set(name, value);
        },
      },
    } as unknown as VRM;

    applyVisemeRelease(vrm, open, 120);
    expect(values.size).toBe(5);
    for (const value of values.values()) {
      expect(value).toBe(0);
    }
    expect(values.get(VRMExpressionPresetName.Aa)).toBe(0);
    expect(values.get(VRMExpressionPresetName.Oh)).toBe(0);
  });
});
