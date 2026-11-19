import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import type { Object3D } from "three";

/**
 * Radians from the file T-pose toward an A-pose (arms down, slightly out).
 * Hang-at-sides would be ~π/2; this stays a bit open so it reads at bust framing.
 * VRM normalized upper-arm Z is inverted vs a naive "left +, right −" guess
 * (that version raised the arms; live default.vrm needs the opposite signs).
 */
export const REST_ARM = {
  upperZ: 1.05,
  lowerX: 0.28,
} as const;

interface BoneSlot {
  bone: Object3D | null;
  baseX: number;
  baseY: number;
  baseZ: number;
}

export interface RestPoseState {
  leftUpper: BoneSlot;
  rightUpper: BoneSlot;
  leftLower: BoneSlot;
  rightLower: BoneSlot;
  warnedMissing: boolean;
}

function emptySlot(): BoneSlot {
  return { bone: null, baseX: 0, baseY: 0, baseZ: 0 };
}

export function createRestPoseState(): RestPoseState {
  return {
    leftUpper: emptySlot(),
    rightUpper: emptySlot(),
    leftLower: emptySlot(),
    rightLower: emptySlot(),
    warnedMissing: false,
  };
}

function bindBone(vrm: VRM, slot: BoneSlot, name: VRMHumanBoneName): void {
  if (slot.bone) {
    return;
  }
  const node = vrm.humanoid?.getNormalizedBoneNode(name) ?? null;
  if (!node) {
    return;
  }
  slot.bone = node;
  slot.baseX = node.rotation.x;
  slot.baseY = node.rotation.y;
  slot.baseZ = node.rotation.z;
}

function applyUpper(slot: BoneSlot, zSign: 1 | -1): void {
  if (!slot.bone) {
    return;
  }
  slot.bone.rotation.x = slot.baseX;
  slot.bone.rotation.y = slot.baseY;
  slot.bone.rotation.z = slot.baseZ + REST_ARM.upperZ * zSign;
}

function applyLower(slot: BoneSlot): void {
  if (!slot.bone) {
    return;
  }
  slot.bone.rotation.x = slot.baseX + REST_ARM.lowerX;
  slot.bone.rotation.y = slot.baseY;
  slot.bone.rotation.z = slot.baseZ;
}

/**
 * Rewrites arm bones from the file T-pose to a standing rest every frame.
 * Gesture offsets add on top. Does not write Chest (breath owns it).
 */
export function updateRestPose(vrm: VRM, state: RestPoseState): void {
  bindBone(vrm, state.leftUpper, VRMHumanBoneName.LeftUpperArm);
  bindBone(vrm, state.rightUpper, VRMHumanBoneName.RightUpperArm);
  bindBone(vrm, state.leftLower, VRMHumanBoneName.LeftLowerArm);
  bindBone(vrm, state.rightLower, VRMHumanBoneName.RightLowerArm);

  if (!state.leftUpper.bone && !state.rightUpper.bone) {
    if (!state.warnedMissing) {
      state.warnedMissing = true;
      console.info("[avatar] upper arm bones missing; leaving file T-pose");
    }
    return;
  }

  applyUpper(state.leftUpper, -1);
  applyUpper(state.rightUpper, 1);
  applyLower(state.leftLower);
  applyLower(state.rightLower);
}
