import { describe, expect, it } from "vitest";
import {
  advanceIdleLife,
  createIdleLifeState,
  idleLifeArmGain,
  idleLifeArmOffsetAt,
  idleLifeHeadOffsetAt,
  idleLifeShoulderOffsetAt,
  IDLE_LIFE_PEAK,
} from "./idleLife";

/** G4 listening yaw / thinking pitch — literals, not private OFFSETS imports (D9). */
const G4_LISTENING_YAW = 0.32;
const G4_THINKING_PITCH = -0.32;

const ZERO_ARMS = {
  leftUpperZ: 0,
  rightUpperZ: 0,
  leftLowerX: 0,
  rightLowerX: 0,
};
const ZERO_SHOULDERS = { leftZ: 0, rightZ: 0 };

describe("idleLife envelopes", () => {
  it("returns zero head offsets at progress 0 and 1", () => {
    for (const action of ["glance", "headTilt"] as const) {
      expect(idleLifeHeadOffsetAt(action, 0)).toEqual({ pitch: 0, yaw: 0, roll: 0 });
      expect(idleLifeHeadOffsetAt(action, 1)).toEqual({ pitch: 0, yaw: 0, roll: 0 });
    }
  });

  it("never writes arm or shoulder extras for wait actions", () => {
    for (const action of ["glance", "headTilt"] as const) {
      expect(idleLifeArmOffsetAt(action, 0.25)).toEqual(ZERO_ARMS);
      expect(idleLifeShoulderOffsetAt(action, 0.25)).toEqual(ZERO_SHOULDERS);
    }
  });

  it("keeps glance yaw under listening G4", () => {
    const mid = idleLifeHeadOffsetAt("glance", 0.25, 1);
    expect(Math.abs(mid.yaw)).toBeCloseTo(IDLE_LIFE_PEAK.glanceYaw);
    expect(Math.abs(mid.yaw)).toBeLessThan(G4_LISTENING_YAW);
    expect(Math.abs(mid.yaw)).toBeLessThanOrEqual(0.12);
  });

  it("keeps headTilt roll under listening G4", () => {
    const mid = idleLifeHeadOffsetAt("headTilt", 0.25);
    expect(Math.abs(mid.roll)).toBeCloseTo(IDLE_LIFE_PEAK.headTiltRoll);
    expect(Math.abs(mid.roll)).toBeLessThan(Math.abs(G4_THINKING_PITCH));
  });
});

describe("idleLifeArmGain", () => {
  it("scales arms up for wider shots", () => {
    expect(idleLifeArmGain("waist")).toBeGreaterThan(idleLifeArmGain("bust"));
    expect(idleLifeArmGain("full")).toBeGreaterThan(idleLifeArmGain("waist"));
    expect(idleLifeArmGain("bust")).toBe(1);
  });
});

describe("advanceIdleLife", () => {
  it("zeros extras when suppressed even mid-action", () => {
    const state = createIdleLifeState(0);
    state.active = "glance";
    state.startMs = 0;
    state.durationMs = 1400;
    state.actionSign = 1;

    const frame = advanceIdleLife(state, 700, {
      visualState: "idle",
      shot: "bust",
      suppressed: true,
    });

    expect(frame.head).toEqual({ pitch: 0, yaw: 0, roll: 0 });
    expect(frame.arms).toEqual(ZERO_ARMS);
    expect(frame.shoulders).toEqual(ZERO_SHOULDERS);
    expect(state.active).toBeNull();
  });

  it("does not start discrete actions while thinking (sway only)", () => {
    const state = createIdleLifeState(0);
    state.nextActionAtMs = 0;

    const frame = advanceIdleLife(state, 100, {
      visualState: "thinking",
      shot: "bust",
      suppressed: false,
    });

    expect(state.active).toBeNull();
    expect(Math.abs(frame.head.yaw)).toBeLessThanOrEqual(IDLE_LIFE_PEAK.swayYaw + 1e-9);
    expect(frame.arms).toEqual(ZERO_ARMS);
  });

  it("runs continuous sway under listening", () => {
    const state = createIdleLifeState(0);
    state.nextActionAtMs = 60_000;

    const frame = advanceIdleLife(state, 1_300, {
      visualState: "listening",
      shot: "bust",
      suppressed: false,
    });

    expect(Math.abs(frame.head.yaw)).toBeGreaterThan(0);
    expect(Math.abs(frame.head.yaw)).toBeLessThanOrEqual(IDLE_LIFE_PEAK.swayYaw + 1e-9);
  });

  it("never writes arm or shoulder extras during a minute of idle", () => {
    const state = createIdleLifeState(0);
    let sawHeadAction = false;

    for (let t = 0; t <= 60_000; t += 100) {
      const frame = advanceIdleLife(state, t, {
        visualState: "idle",
        shot: "full",
        suppressed: false,
      });
      expect(frame.arms).toEqual(ZERO_ARMS);
      expect(frame.shoulders).toEqual(ZERO_SHOULDERS);
      if (
        Math.abs(frame.head.yaw) > IDLE_LIFE_PEAK.swayYaw + 1e-6 ||
        Math.abs(frame.head.roll) > IDLE_LIFE_PEAK.swayRoll + 1e-6
      ) {
        sawHeadAction = true;
      }
    }

    expect(sawHeadAction).toBe(true);
  });
});
