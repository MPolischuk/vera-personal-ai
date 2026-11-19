import type { VRM } from "@pixiv/three-vrm";
import {
  AnimationAction,
  AnimationClip,
  AnimationMixer,
  LoopOnce,
} from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { CLIP_CATALOG } from "./clipCatalog";
import { retargetMixamoClip } from "./retargetMixamoClip";

export const CLIP_FADE_OUT_MS = 150;

export interface ClipMotionState {
  mixer: AnimationMixer | null;
  action: AnimationAction | null;
  activeId: string | null;
  clips: Map<string, AnimationClip>;
  /** Ids that failed once — log at most once per id (D5). */
  warnedMissing: Set<string>;
}

export function createClipMotionState(): ClipMotionState {
  return {
    mixer: null,
    action: null,
    activeId: null,
    clips: new Map(),
    warnedMissing: new Set(),
  };
}

export function isClipPlaying(state: ClipMotionState): boolean {
  if (state.activeId !== null) {
    return true;
  }
  const weight = state.action?.getEffectiveWeight() ?? 0;
  return weight > 0.01;
}

/**
 * Load catalog GLBs, retarget onto the VRM, fill `state.clips`.
 * 404 / retarget errors skip that id and log once.
 */
export async function loadGestureClips(
  vrm: VRM,
  state: ClipMotionState,
  signal?: AbortSignal,
  catalog: Readonly<Record<string, string>> = CLIP_CATALOG,
): Promise<ReadonlySet<string>> {
  const loader = new GLTFLoader();
  const loaded = new Set<string>();

  await Promise.all(
    Object.entries(catalog).map(async ([id, url]) => {
      if (signal?.aborted) {
        return;
      }
      try {
        const gltf = await loader.loadAsync(url);
        if (signal?.aborted) {
          return;
        }
        const clip = retargetMixamoClip(gltf, vrm);
        state.clips.set(id, clip);
        loaded.add(id);
      } catch {
        warnClipFallback(state, id);
      }
    }),
  );

  return loaded;
}

function warnClipFallback(state: ClipMotionState, id: string): void {
  if (state.warnedMissing.has(id)) {
    return;
  }
  state.warnedMissing.add(id);
  console.info(
    `[avatar] clip ${id} missing or retarget failed; using procedural`,
  );
}

/**
 * Start or stop a Mixamo clip (D6/D7). Returns whether a clip is now driving.
 * weight=1, native duration, LoopOnce. Does not use payload durationMs.
 */
export function beginClipGesture(
  state: ClipMotionState,
  vrm: VRM | null,
  id: string | null,
): boolean {
  if (!id) {
    fadeOutAndClear(state);
    return false;
  }

  if (!vrm) {
    return false;
  }

  const clip = state.clips.get(id);
  if (!clip) {
    warnClipFallback(state, id);
    fadeOutAndClear(state);
    return false;
  }

  if (!state.mixer) {
    state.mixer = new AnimationMixer(vrm.scene);
  }

  if (state.activeId === id && state.action) {
    return true;
  }

  if (state.action) {
    state.action.fadeOut(CLIP_FADE_OUT_MS / 1000);
  }

  const action = state.mixer.clipAction(clip);
  action.reset();
  action.setLoop(LoopOnce, 1);
  action.clampWhenFinished = true;
  action.setEffectiveWeight(1);
  action.timeScale = 1;
  action.fadeIn(0);
  action.play();

  state.action = action;
  state.activeId = id;
  return true;
}

function fadeOutAndClear(state: ClipMotionState): void {
  if (state.action) {
    state.action.fadeOut(CLIP_FADE_OUT_MS / 1000);
  }
  state.activeId = null;
}

/**
 * Advance the mixer when a clip is active or fading out.
 * Call after pose, before lip-sync (D6).
 */
export function updateClipMotion(
  state: ClipMotionState,
  deltaSeconds: number,
): boolean {
  if (!state.mixer) {
    return false;
  }

  const needsUpdate =
    state.activeId !== null ||
    (state.action !== null && (state.action.getEffectiveWeight() > 0.01));
  if (!needsUpdate) {
    state.action = null;
    return false;
  }

  state.mixer.update(deltaSeconds);

  if (state.activeId !== null && state.action) {
    const finished =
      !state.action.isRunning() &&
      state.action.time >= state.action.getClip().duration - 1e-4;
    if (finished) {
      state.action.stop();
      state.action = null;
      state.activeId = null;
      return false;
    }
    return true;
  }

  // Fading out after gesture cleared
  if (state.action && state.action.getEffectiveWeight() <= 0.01) {
    state.action.stop();
    state.action = null;
    return false;
  }

  return state.action !== null;
}

export function stopClipMotion(state: ClipMotionState): void {
  if (state.mixer) {
    state.mixer.stopAllAction();
  }
  state.action = null;
  state.activeId = null;
}

export function disposeClipMotion(state: ClipMotionState): void {
  stopClipMotion(state);
  state.mixer = null;
  state.clips.clear();
}
