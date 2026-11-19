import type { VRM } from "@pixiv/three-vrm";
import { VRMLoaderPlugin, VRMUtils } from "@pixiv/three-vrm";
import type { PerspectiveCamera, Scene } from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { applyCameraFraming, framingFromVrm } from "./cameraShot";

export interface LoadVrmResult {
  vrm: VRM;
}

/**
 * Loads a VRM (0.x / 1.0) from a same-origin URL. Supports AbortSignal for URL changes.
 */
export async function loadVrm(
  url: string,
  signal?: AbortSignal,
): Promise<LoadVrmResult> {
  if (signal?.aborted) {
    throw new DOMException("Aborted", "AbortError");
  }

  const loader = new GLTFLoader();
  loader.register((parser) => new VRMLoaderPlugin(parser));

  const gltf = await loader.loadAsync(url);
  if (signal?.aborted) {
    VRMUtils.deepDispose(gltf.scene);
    throw new DOMException("Aborted", "AbortError");
  }

  const vrm = gltf.userData.vrm as VRM | undefined;
  if (!vrm) {
    VRMUtils.deepDispose(gltf.scene);
    throw new Error("The file is not a valid VRM.");
  }

  VRMUtils.removeUnnecessaryVertices(gltf.scene);
  VRMUtils.combineSkeletons(gltf.scene);
  VRMUtils.combineMorphs(vrm);
  VRMUtils.rotateVRM0(vrm);

  vrm.scene.traverse((obj) => {
    obj.frustumCulled = false;
  });

  return { vrm };
}

/** Places the VRM at the origin and frames the camera on the head / upper chest. */
export function placeAndFrameVrm(
  vrm: VRM,
  scene: Scene,
  camera: PerspectiveCamera,
): void {
  vrm.scene.position.set(0, 0, 0);
  vrm.scene.rotation.set(0, 0, 0);
  scene.add(vrm.scene);
  vrm.scene.updateMatrixWorld(true);

  applyCameraFraming(camera, framingFromVrm(vrm, "bust"));

  if (vrm.lookAt) {
    vrm.lookAt.target = camera;
  }
}

export function disposeVrm(vrm: VRM, scene: Scene): void {
  scene.remove(vrm.scene);
  VRMUtils.deepDispose(vrm.scene);
}
