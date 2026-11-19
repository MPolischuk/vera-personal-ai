import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import {
  AnimationClip,
  Object3D,
  Quaternion,
  QuaternionKeyframeTrack,
  VectorKeyframeTrack,
  type KeyframeTrack,
} from "three";
import type { GLTF } from "three/addons/loaders/GLTFLoader.js";

/**
 * Mixamo rig bone name → VRM Humanoid bone name (pixiv humanoidAnimation map).
 */
export const MIXAMO_VRM_RIG_MAP: Readonly<Record<string, VRMHumanBoneName>> = {
  mixamorigHips: VRMHumanBoneName.Hips,
  mixamorigSpine: VRMHumanBoneName.Spine,
  mixamorigSpine1: VRMHumanBoneName.Chest,
  mixamorigSpine2: VRMHumanBoneName.UpperChest,
  mixamorigNeck: VRMHumanBoneName.Neck,
  mixamorigHead: VRMHumanBoneName.Head,
  mixamorigLeftShoulder: VRMHumanBoneName.LeftShoulder,
  mixamorigLeftArm: VRMHumanBoneName.LeftUpperArm,
  mixamorigLeftForeArm: VRMHumanBoneName.LeftLowerArm,
  mixamorigLeftHand: VRMHumanBoneName.LeftHand,
  mixamorigLeftHandThumb1: VRMHumanBoneName.LeftThumbMetacarpal,
  mixamorigLeftHandThumb2: VRMHumanBoneName.LeftThumbProximal,
  mixamorigLeftHandThumb3: VRMHumanBoneName.LeftThumbDistal,
  mixamorigLeftHandIndex1: VRMHumanBoneName.LeftIndexProximal,
  mixamorigLeftHandIndex2: VRMHumanBoneName.LeftIndexIntermediate,
  mixamorigLeftHandIndex3: VRMHumanBoneName.LeftIndexDistal,
  mixamorigLeftHandMiddle1: VRMHumanBoneName.LeftMiddleProximal,
  mixamorigLeftHandMiddle2: VRMHumanBoneName.LeftMiddleIntermediate,
  mixamorigLeftHandMiddle3: VRMHumanBoneName.LeftMiddleDistal,
  mixamorigLeftHandRing1: VRMHumanBoneName.LeftRingProximal,
  mixamorigLeftHandRing2: VRMHumanBoneName.LeftRingIntermediate,
  mixamorigLeftHandRing3: VRMHumanBoneName.LeftRingDistal,
  mixamorigLeftHandPinky1: VRMHumanBoneName.LeftLittleProximal,
  mixamorigLeftHandPinky2: VRMHumanBoneName.LeftLittleIntermediate,
  mixamorigLeftHandPinky3: VRMHumanBoneName.LeftLittleDistal,
  mixamorigRightShoulder: VRMHumanBoneName.RightShoulder,
  mixamorigRightArm: VRMHumanBoneName.RightUpperArm,
  mixamorigRightForeArm: VRMHumanBoneName.RightLowerArm,
  mixamorigRightHand: VRMHumanBoneName.RightHand,
  mixamorigRightHandPinky1: VRMHumanBoneName.RightLittleProximal,
  mixamorigRightHandPinky2: VRMHumanBoneName.RightLittleIntermediate,
  mixamorigRightHandPinky3: VRMHumanBoneName.RightLittleDistal,
  mixamorigRightHandRing1: VRMHumanBoneName.RightRingProximal,
  mixamorigRightHandRing2: VRMHumanBoneName.RightRingIntermediate,
  mixamorigRightHandRing3: VRMHumanBoneName.RightRingDistal,
  mixamorigRightHandMiddle1: VRMHumanBoneName.RightMiddleProximal,
  mixamorigRightHandMiddle2: VRMHumanBoneName.RightMiddleIntermediate,
  mixamorigRightHandMiddle3: VRMHumanBoneName.RightMiddleDistal,
  mixamorigRightHandIndex1: VRMHumanBoneName.RightIndexProximal,
  mixamorigRightHandIndex2: VRMHumanBoneName.RightIndexIntermediate,
  mixamorigRightHandIndex3: VRMHumanBoneName.RightIndexDistal,
  mixamorigRightHandThumb1: VRMHumanBoneName.RightThumbMetacarpal,
  mixamorigRightHandThumb2: VRMHumanBoneName.RightThumbProximal,
  mixamorigRightHandThumb3: VRMHumanBoneName.RightThumbDistal,
  mixamorigLeftUpLeg: VRMHumanBoneName.LeftUpperLeg,
  mixamorigLeftLeg: VRMHumanBoneName.LeftLowerLeg,
  mixamorigLeftFoot: VRMHumanBoneName.LeftFoot,
  mixamorigLeftToeBase: VRMHumanBoneName.LeftToes,
  mixamorigRightUpLeg: VRMHumanBoneName.RightUpperLeg,
  mixamorigRightLeg: VRMHumanBoneName.RightLowerLeg,
  mixamorigRightFoot: VRMHumanBoneName.RightFoot,
  mixamorigRightToeBase: VRMHumanBoneName.RightToes,
};

/** Normalize Mixamo / Blender bone names (`mixamorig:Hips` → `mixamorigHips`). */
export function normalizeMixamoBoneName(name: string): string {
  return name.replace(/:/g, "");
}

/**
 * Drops hips/root translation tracks (D4). Pure; does not mutate input tracks.
 */
export function stripHipsPositionTracks(
  tracks: readonly KeyframeTrack[],
): KeyframeTrack[] {
  return tracks.filter((track) => {
    const [nodeName, property] = track.name.split(".");
    if (property !== "position") {
      return true;
    }
    const lower = nodeName.toLowerCase();
    return !(
      lower.includes("hips") ||
      lower === "root" ||
      lower.endsWith("/hips")
    );
  });
}

function findObjectByNormalizedName(
  root: Object3D,
  normalized: string,
): Object3D | null {
  let found: Object3D | null = null;
  root.traverse((obj) => {
    if (found) {
      return;
    }
    if (normalizeMixamoBoneName(obj.name) === normalized) {
      found = obj;
    }
  });
  return found;
}

function pickSourceClip(gltf: GLTF): AnimationClip {
  if (gltf.animations.length === 0) {
    throw new Error("GLB has no animations");
  }
  return (
    AnimationClip.findByName(gltf.animations, "mixamo.com") ??
    gltf.animations[0]!
  );
}

/**
 * Retarget a Mixamo GLTF animation onto VRM normalized humanoid bones (D3/D4).
 * Strips hips.position tracks after retarget.
 */
export function retargetMixamoClip(gltf: GLTF, vrm: VRM): AnimationClip {
  const humanoid = vrm.humanoid;
  if (!humanoid) {
    throw new Error("VRM has no humanoid");
  }

  const clip = pickSourceClip(gltf);
  const tracks: KeyframeTrack[] = [];

  const restRotationInverse = new Quaternion();
  const parentRestWorldRotation = new Quaternion();
  const quatA = new Quaternion();

  const mixamoHips = findObjectByNormalizedName(gltf.scene, "mixamorigHips");
  if (!mixamoHips) {
    throw new Error("Mixamo hips bone not found in GLB scene");
  }

  const motionHipsHeight = mixamoHips.position.y;
  const restHips = humanoid.normalizedRestPose.hips?.position;
  const vrmHipsHeight = restHips?.[1] ?? 1;
  const hipsPositionScale =
    motionHipsHeight !== 0 ? vrmHipsHeight / motionHipsHeight : 1;

  const metaVersion = vrm.meta?.metaVersion;

  for (const track of clip.tracks) {
    const [mixamoRaw, propertyName] = track.name.split(".");
    if (!mixamoRaw || !propertyName) {
      continue;
    }
    const mixamoRigName = normalizeMixamoBoneName(mixamoRaw);
    const vrmBoneName = MIXAMO_VRM_RIG_MAP[mixamoRigName];
    if (!vrmBoneName) {
      continue;
    }

    const vrmNode = humanoid.getNormalizedBoneNode(vrmBoneName);
    const vrmNodeName = vrmNode?.name;
    const mixamoRigNode = findObjectByNormalizedName(gltf.scene, mixamoRigName);
    if (!vrmNodeName || !mixamoRigNode?.parent) {
      continue;
    }

    mixamoRigNode.getWorldQuaternion(restRotationInverse).invert();
    mixamoRigNode.parent.getWorldQuaternion(parentRestWorldRotation);

    if (track instanceof QuaternionKeyframeTrack) {
      const values = track.values.slice();
      for (let i = 0; i < values.length; i += 4) {
        const flat = values.slice(i, i + 4);
        quatA.fromArray(flat);
        quatA.premultiply(parentRestWorldRotation).multiply(restRotationInverse);
        quatA.toArray(flat);
        for (let j = 0; j < 4; j++) {
          values[i + j] = flat[j]!;
        }
      }
      const signed =
        metaVersion === "0"
          ? values.map((v, i) => (i % 2 === 0 ? -v : v))
          : values;
      tracks.push(
        new QuaternionKeyframeTrack(
          `${vrmNodeName}.${propertyName}`,
          track.times,
          signed,
        ),
      );
    } else if (track instanceof VectorKeyframeTrack) {
      const value = track.values.map((v, i) => {
        const flipped =
          metaVersion === "0" && i % 3 !== 1 ? -v : v;
        return flipped * hipsPositionScale;
      });
      tracks.push(
        new VectorKeyframeTrack(
          `${vrmNodeName}.${propertyName}`,
          track.times,
          value,
        ),
      );
    }
  }

  const stripped = stripHipsPositionTracks(tracks);
  return new AnimationClip("vrmAnimation", clip.duration, stripped);
}

/** Convenience for tests that only care about the strip filter. */
export function clipWithoutHipsPosition(clip: AnimationClip): AnimationClip {
  return new AnimationClip(
    clip.name,
    clip.duration,
    stripHipsPositionTracks(clip.tracks),
  );
}
