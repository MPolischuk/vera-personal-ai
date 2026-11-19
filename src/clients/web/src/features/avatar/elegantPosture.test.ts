import { describe, expect, it } from "vitest";
import { Object3D, Quaternion } from "three";
import type { VRM } from "@pixiv/three-vrm";
import { POSTURE, createPostureState, undoPosture, updatePosture } from "./elegantPosture";

function fakeVrm(): { vrm: VRM; bones: Record<string, Object3D> } {
  const bones: Record<string, Object3D> = {};
  const root = new Object3D();
  for (const name of Object.keys(POSTURE)) {
    bones[name] = new Object3D();
    root.add(bones[name]);
  }
  // Minimal leg chain so bind() can capture plant targets.
  if (bones.hips && bones.leftUpperLeg && bones.leftLowerLeg && bones.leftFoot) {
    bones.hips.add(bones.leftUpperLeg);
    bones.leftUpperLeg.add(bones.leftLowerLeg);
    bones.leftLowerLeg.add(bones.leftFoot);
    bones.hips.add(bones.rightUpperLeg);
    bones.rightUpperLeg.add(bones.rightLowerLeg);
    bones.rightLowerLeg.add(bones.rightFoot);
    bones.leftUpperLeg.position.set(0.09, -0.05, 0);
    bones.leftLowerLeg.position.set(0, -0.42, 0);
    bones.leftFoot.position.set(0, -0.4, 0.05);
    bones.rightUpperLeg.position.set(-0.09, -0.05, 0);
    bones.rightLowerLeg.position.set(0, -0.42, 0);
    bones.rightFoot.position.set(0, -0.4, 0.05);
  }
  const vrm = {
    scene: root,
    humanoid: { getNormalizedBoneNode: (n: string) => bones[n] ?? null },
  } as unknown as VRM;
  return { vrm, bones };
}

describe("elegant posture", () => {
  it("does not accumulate on bones nobody rewrites", () => {
    const { vrm, bones } = fakeVrm();
    const state = createPostureState();
    updatePosture(vrm, state, 0);
    const first = bones.leftUpperLeg.quaternion.clone();
    for (let i = 0; i < 50; i += 1) {
      undoPosture(state);
      bones.chest.rotation.x = 0.01; // partial writer (breath)
      updatePosture(vrm, state, 0);
    }
    expect(bones.leftUpperLeg.quaternion.angleTo(first)).toBeLessThan(1e-6);
    expect(first.angleTo(new Quaternion())).toBeGreaterThan(0.1);
    expect(bones.chest.rotation.y).toBeCloseTo(POSTURE.chest[1], 2);
  });

  it("layers on top of a pose another writer set this frame", () => {
    const { vrm, bones } = fakeVrm();
    const state = createPostureState();
    undoPosture(state);
    bones.head.rotation.set(0.3, 0, 0);
    updatePosture(vrm, state, 0);
    expect(bones.head.rotation.x).toBeCloseTo(0.35);
  });

  it("mirrors the shoulders and keeps the hips sway tiny", () => {
    expect(POSTURE.rightShoulder[1]).toBe(-POSTURE.leftShoulder[1]);
    expect(POSTURE.rightShoulder[2]).toBe(-POSTURE.leftShoulder[2]);
    const { vrm, bones } = fakeVrm();
    const state = createPostureState();
    updatePosture(vrm, state, 0);
    const a = bones.hips.quaternion.clone();
    undoPosture(state);
    updatePosture(vrm, state, 2300);
    expect(bones.hips.quaternion.angleTo(a)).toBeLessThan(0.03);
  });
});
