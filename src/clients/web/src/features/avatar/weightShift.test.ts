import { describe, expect, it } from "vitest";
import type { VRM } from "@pixiv/three-vrm";
import { Object3D, Vector3 } from "three";
import {
  FOOT_PLANT_MAX_M,
  POSTURE,
  createPostureState,
  footHorizontalDrift,
  undoPosture,
  updatePosture,
  WEIGHT_SHIFT,
} from "./elegantPosture";

/** Build a minimal hips→legs→feet hierarchy so IK and plant targets work. */
function fakeLegVrm(): { vrm: VRM; feet: { L: Object3D; R: Object3D } } {
  const nodes = new Map<string, Object3D>();
  const add = (name: string, parent: Object3D, x: number, y: number, z: number) => {
    const o = new Object3D();
    o.name = name;
    o.position.set(x, y, z);
    parent.add(o);
    nodes.set(name, o);
    return o;
  };
  const root = new Object3D();
  const hips = add("hips", root, 0, 1.0, 0);
  add("spine", hips, 0, 0.12, 0);
  add("chest", nodes.get("spine")!, 0, 0.12, 0);
  add("upperChest", nodes.get("chest")!, 0, 0.1, 0);
  add("neck", nodes.get("upperChest")!, 0, 0.08, 0);
  add("head", nodes.get("neck")!, 0, 0.1, 0);
  add("leftShoulder", nodes.get("upperChest")!, 0.08, 0.05, 0);
  add("rightShoulder", nodes.get("upperChest")!, -0.08, 0.05, 0);
  const lUpper = add("leftUpperLeg", hips, 0.09, -0.05, 0);
  const lLower = add("leftLowerLeg", lUpper, 0, -0.42, 0);
  const lFoot = add("leftFoot", lLower, 0, -0.4, 0.05);
  const rUpper = add("rightUpperLeg", hips, -0.09, -0.05, 0);
  const rLower = add("rightLowerLeg", rUpper, 0, -0.42, 0);
  const rFoot = add("rightFoot", rLower, 0, -0.4, 0.05);
  root.updateMatrixWorld(true);
  const vrm = {
    scene: root,
    humanoid: { getNormalizedBoneNode: (n: string) => nodes.get(n) ?? null },
  } as unknown as VRM;
  return { vrm, feet: { L: lFoot, R: rFoot } };
}

describe("weight shift foot plant", () => {
  it("keeps each foot within FOOT_PLANT_MAX_M across a full left↔right flip", () => {
    const { vrm, feet } = fakeLegVrm();
    const state = createPostureState();
    state.weight = 1;
    updatePosture(vrm, state, 0);
    (vrm.scene as Object3D).updateMatrixWorld(true);
    const originL = feet.L.getWorldPosition(new Vector3());
    const originR = feet.R.getWorldPosition(new Vector3());

    let maxL = 0;
    let maxR = 0;
    for (const w of [-1, -0.5, 0, 0.5, 1]) {
      undoPosture(state);
      state.weight = w;
      updatePosture(vrm, state, 0);
      (vrm.scene as Object3D).updateMatrixWorld(true);
      maxL = Math.max(maxL, footHorizontalDrift(originL, feet.L.getWorldPosition(new Vector3())));
      maxR = Math.max(maxR, footHorizontalDrift(originR, feet.R.getWorldPosition(new Vector3())));
    }
    expect(maxL, `left foot drift ${maxL.toFixed(4)} m`).toBeLessThanOrEqual(FOOT_PLANT_MAX_M);
    expect(maxR, `right foot drift ${maxR.toFixed(4)} m`).toBeLessThanOrEqual(FOOT_PLANT_MAX_M);
  });

  it("shifts the hips laterally toward the stance leg", () => {
    expect(WEIGHT_SHIFT.hipLateralM).toBeGreaterThan(0.015);
    expect(POSTURE.leftLowerLeg[0]).toBeGreaterThan(POSTURE.rightLowerLeg[0]);
  });
});
