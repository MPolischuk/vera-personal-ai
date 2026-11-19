import { describe, expect, it } from "vitest";
import type { VRM } from "@pixiv/three-vrm";
import { VRMExpressionPresetName, VRMHumanBoneName } from "@pixiv/three-vrm";
import { Object3D } from "three";
import { activeState, createDirector, requestState } from "./director";
import {
  blinkWeightAt,
  breathOffsetAt,
  createIdleMotionState,
  updateIdleMotion,
} from "./idleMotion";
import {
  advanceSpeakingAside,
  blinkWeightWithHappy,
  createGazeClock,
  createLookAtState,
  DOUBLE_BLINK_GAP_MS,
  resolveLookAim,
  scaleLookPitch,
  scaleLookYaw,
  setLookTarget,
  smoothLook,
  splitLook,
  stretchedBlinkGap,
  THINK_SACCADE_HOLD_MS,
  thinkingSaccade,
  updateLookAt,
} from "./lookAt";

describe("look aim", () => {
  it("looks at the camera except while thinking or glancing away", () => {
    expect(resolveLookAim("idle", false, false)).toBe("camera");
    expect(resolveLookAim("listening", false, false)).toBe("camera");
    expect(resolveLookAim("speaking", false, false)).toBe("camera");
    expect(resolveLookAim("thinking", false, false)).toBe("aside");
    expect(resolveLookAim("thinking", false, false)).not.toBe("camera");
    expect(resolveLookAim("speaking", false, true)).toBe("aside");
  });

  it("uses a custom target from setLookTarget and otherwise thinks off camera", () => {
    const state = createLookAtState();
    const point = new Object3D();
    setLookTarget(state, point);
    expect(state.custom).toBe(point);
    expect(resolveLookAim("idle", state.custom !== null, false)).toBe("custom");
    setLookTarget(state, null);
    expect(resolveLookAim("thinking", state.custom !== null, false)).toBe("aside");
  });

  it("saccades thinking by 0.15 rad, up and to the side", () => {
    expect(thinkingSaccade(0).pitch).toBeCloseTo(-0.15);
    expect(Math.abs(thinkingSaccade(0).yaw)).toBeCloseTo(0.15);
    expect(thinkingSaccade(0).yaw).toBe(thinkingSaccade(THINK_SACCADE_HOLD_MS - 1).yaw);
    expect(thinkingSaccade(0).yaw).not.toBe(thinkingSaccade(THINK_SACCADE_HOLD_MS).yaw);
  });

  it("glances away once a speaking stretch passes 8 s, for 0.4–0.8 s", () => {
    const clock = createGazeClock();
    expect(advanceSpeakingAside(clock, "speaking", 100)).toBe(false);
    expect(advanceSpeakingAside(clock, "speaking", 100 + 7999)).toBe(false);
    expect(advanceSpeakingAside(clock, "speaking", 100 + 8000)).toBe(true);
    const duration = clock.asideUntilMs - clock.asideFromMs;
    expect(duration).toBeGreaterThanOrEqual(400);
    expect(duration).toBeLessThanOrEqual(800);
    expect(advanceSpeakingAside(clock, "speaking", clock.asideUntilMs)).toBe(false);
    expect(advanceSpeakingAside(clock, "listening", clock.asideUntilMs + 10)).toBe(false);
    expect(clock.speakingSinceMs).toBe(-1);
  });
});

describe("head look, not the eyes", () => {
  it("scales atan2 and splits neck 0.4 / head 0.6", () => {
    expect(scaleLookYaw(2)).toBeCloseTo(0.8);
    expect(scaleLookYaw(-2)).toBeCloseTo(-0.8);
    expect(scaleLookPitch(1)).toBeCloseTo(0.35);
    expect(scaleLookPitch(-1)).toBeCloseTo(-0.35);
    const parts = splitLook({ yaw: 1, pitch: 0.5 });
    expect(parts.neck.yaw).toBeCloseTo(0.4);
    expect(parts.head.yaw).toBeCloseTo(0.6);
    expect(parts.neck.pitch).toBeCloseTo(0.2);
    expect(parts.head.pitch).toBeCloseTo(0.3);
  });

  it("eases the head and snaps the eyes onto the thinking target", () => {
    const eased = smoothLook({ yaw: 0, pitch: 0 }, { yaw: 0.15, pitch: -0.15 }, 16);
    expect(Math.abs(eased.yaw)).toBeLessThan(0.15);

    const head = new Object3D();
    const neck = new Object3D();
    head.position.set(0, 1.5, 0);
    const camera = new Object3D();
    camera.position.set(0, 1.5, 2);
    const vrm = {
      lookAt: { target: camera as Object3D | null },
      humanoid: {
        getNormalizedBoneNode(name: string) {
          if (name === VRMHumanBoneName.Head) {
            return head;
          }
          if (name === VRMHumanBoneName.Neck) {
            return neck;
          }
          return null;
        },
      },
    } as unknown as VRM;
    const state = createLookAtState();
    updateLookAt(vrm, state, "thinking", camera, 0);
    updateLookAt(vrm, state, "thinking", camera, 16);
    expect(vrm.lookAt?.target).toBe(state.asideTarget);
    expect(vrm.lookAt?.target).not.toBe(camera);
    expect(Math.abs(head.rotation.y)).toBeLessThan(0.15);

    const marked = new Object3D();
    setLookTarget(state, marked);
    updateLookAt(vrm, state, "idle", camera, 32);
    expect(vrm.lookAt?.target).toBe(marked);
  });
});

function blinkVrm(): { vrm: VRM; values: Map<string, number> } {
  const values = new Map<string, number>();
  const vrm = {
    expressionManager: {
      getExpression: () => ({}),
      setValue(name: string, value: number) {
        values.set(name, value);
      },
      getValue: (name: string) => values.get(name) ?? 0,
    },
    humanoid: null,
  } as unknown as VRM;
  return { vrm, values };
}

describe("blink", () => {
  it("leads a blink when two published states meet in one tick", () => {
    const director = createDirector("idle", 0);
    requestState(director, "thinking", 0);
    const before = director.published;
    const published = activeState(director, 250);
    expect(before).not.toBe(published);

    const { vrm } = blinkVrm();
    const state = createIdleMotionState({ nowMs: () => 0 });
    state.nextBlinkAtMs = 10_000;
    updateIdleMotion(vrm, state, { nowMs: () => 1000 }, {
      blinkV2: { stateChanged: true, happy: 0, listening: false },
    });
    expect(state.blinkActive).toBe(true);
  });

  it("does not lead a blink that already happened in the last 200 ms", () => {
    const { vrm } = blinkVrm();
    const state = createIdleMotionState({ nowMs: () => 0 });
    state.blinkActive = false;
    state.lastBlinkStartMs = 900;
    state.nextBlinkAtMs = 10_000;
    updateIdleMotion(vrm, state, { nowMs: () => 1000 }, {
      blinkV2: { stateChanged: true, happy: 0, listening: false },
    });
    expect(state.blinkActive).toBe(false);
  });

  it("cuts the blink to a fifth when happy is 1", () => {
    const { vrm, values } = blinkVrm();
    const state = createIdleMotionState({ nowMs: () => 0 });
    state.blinkActive = true;
    state.blinkStartMs = 1000;
    state.nextBlinkAtMs = 10_000;
    updateIdleMotion(vrm, state, { nowMs: () => 1070 }, {
      blinkV2: { stateChanged: false, happy: 1, listening: false },
    });
    const neutral = blinkWeightAt(0.5);
    expect(blinkWeightWithHappy(neutral, 1)).toBeCloseTo(neutral * 0.2);
    expect(values.get(VRMExpressionPresetName.Blink)).toBeCloseTo(neutral * 0.2);
  });

  it("schedules a double on every sixth blink and stretches listening", () => {
    expect(stretchedBlinkGap(1000, true)).toBeCloseTo(1300);
    expect(stretchedBlinkGap(1000, false)).toBe(1000);

    const { vrm } = blinkVrm();
    const state = createIdleMotionState({ nowMs: () => 0 });
    state.blinkActive = true;
    state.blinkStartMs = 0;
    state.blinkCount = 5;
    updateIdleMotion(vrm, state, { nowMs: () => 140 }, {
      blinkV2: { stateChanged: false, happy: 0, listening: true },
    });
    expect(state.nextBlinkAtMs).toBe(140 + DOUBLE_BLINK_GAP_MS);
  });

  it("keeps the legacy gap when the v2 policy is absent", () => {
    const { vrm } = blinkVrm();
    const state = createIdleMotionState({ nowMs: () => 0 });
    state.blinkActive = true;
    state.blinkStartMs = 0;
    state.blinkCount = 5;
    updateIdleMotion(vrm, state, { nowMs: () => 140 });
    expect(state.blinkCount).toBe(5);
    expect(state.nextBlinkAtMs).toBeGreaterThanOrEqual(140 + 3000);
  });

  it("adds the speech chest on top of the breath", () => {
    const chest = { rotation: { x: 1, y: 0, z: 0 } };
    const vrm = {
      expressionManager: null,
      humanoid: { getNormalizedBoneNode: () => chest },
    } as unknown as VRM;
    const state = createIdleMotionState({ nowMs: () => 0 });
    updateIdleMotion(vrm, state, { nowMs: () => 0 }, { speechChest: 0.02 });
    expect(chest.rotation.x).toBeCloseTo(1 + breathOffsetAt(0) + 0.02);
  });
});
