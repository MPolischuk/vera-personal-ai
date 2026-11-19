import type { VRM } from "@pixiv/three-vrm";
import { VRMExpressionPresetName, VRMHumanBoneName } from "@pixiv/three-vrm";

/** Head pitch/yaw from this layer stay inside this (radians). */
export const SPEECH_PULSE_LIMIT_RAD = 0.15;

/** Viseme weights reach 0 at this time after speech stops. */
export const VISEME_RELEASE_MS = 120;

/** Envelope time constant applied to lip-sync loudness before the pulse. */
export const SPEECH_ENERGY_TAU_MS = 300;

export interface SpeechEnergyState {
  weight: number;
  lastMs: number | null;
}

export function createSpeechEnergyState(): SpeechEnergyState {
  return { weight: 0, lastMs: null };
}

/**
 * One-pole envelope of lip-sync loudness, τ = 300 ms.
 * The first sample stays at 0. Same inputs and timestamps repeat the same weight.
 */
export function advanceSpeechEnergy(
  state: SpeechEnergyState,
  energy: number,
  nowMs: number,
): number {
  const sample = clamp01(energy);
  if (state.lastMs === null) {
    state.lastMs = nowMs;
    state.weight = 0;
    return 0;
  }
  const dt = Math.max(0, nowMs - state.lastMs);
  const alpha = 1 - Math.exp(-dt / SPEECH_ENERGY_TAU_MS);
  state.weight += (sample - state.weight) * alpha;
  state.lastMs = nowMs;
  return state.weight;
}

export const SPEECH_VISEMES = ["aa", "ih", "ou", "ee", "oh"] as const;

export type SpeechViseme = (typeof SPEECH_VISEMES)[number];

export type SpeechVisemeWeights = Record<SpeechViseme, number>;

/**
 * Normalized log loudness below this does not pitch the head.
 * The current TTS voice sits near 0.86 while it is voiced and falls through 0.30 on the release.
 */
export const SPEECH_PULSE_FLOOR = 0.4;

/** Pitch, in radians, for each unit of loudness above the floor, times the speak weight. */
export const SPEECH_PULSE_PITCH = 0.12;

export interface SpeechPulse {
  /** Head pitch. 0 while energy is under the floor, so silence does not nod down. */
  pitch: number;
  yaw: number;
  /** Added by the chest owner on top of breath. Not written here. */
  chest: number;
  forearm: number;
}

function clamp01(value: number): number {
  if (!Number.isFinite(value) || value <= 0) {
    return 0;
  }
  if (value >= 1) {
    return 1;
  }
  return value;
}

function clampAbs(value: number, limit: number): number {
  if (value > limit) {
    return limit;
  }
  if (value < -limit) {
    return -limit;
  }
  return value;
}

/**
 * Speech bob from loudness already passed through the 300 ms envelope.
 * Pitch is `(energy - floor) * pitchFactor * speakW`, and 0 below the floor.
 * Yaw is `sin(t * 1.9) * 0.04 * energy * speakW`.
 */
export function speechPulseAt(energy: number, speakW: number, tSeconds: number): SpeechPulse {
  const e = clamp01(energy);
  const w = clamp01(speakW);
  const pitch = e < SPEECH_PULSE_FLOOR ? 0 : (e - SPEECH_PULSE_FLOOR) * SPEECH_PULSE_PITCH * w;
  const yaw = Math.sin(tSeconds * 1.9) * 0.04 * e * w;
  return {
    pitch: clampAbs(pitch, SPEECH_PULSE_LIMIT_RAD),
    yaw: clampAbs(yaw, SPEECH_PULSE_LIMIT_RAD),
    chest: e * 0.02,
    forearm: 0.05 * e * w,
  };
}

/** `explain` weight while motion v2 is on. Stays inside 0.2..0.6. */
export function explainWeightForSpeech(speakW: number): number {
  const weight = 0.2 + 0.4 * clamp01(speakW);
  if (weight < 0.2) {
    return 0.2;
  }
  if (weight > 0.6) {
    return 0.6;
  }
  return weight;
}

/** Scales the weights the lip-sync just produced. At 120 ms every viseme is 0. */
export function releasedVisemes(
  weights: SpeechVisemeWeights,
  elapsedMs: number,
): SpeechVisemeWeights {
  const scale = elapsedMs >= VISEME_RELEASE_MS ? 0 : 1 - Math.max(0, elapsedMs) / VISEME_RELEASE_MS;
  return {
    aa: weights.aa * scale,
    ih: weights.ih * scale,
    ou: weights.ou * scale,
    ee: weights.ee * scale,
    oh: weights.oh * scale,
  };
}

const VISEME_PRESET: Record<SpeechViseme, VRMExpressionPresetName> = {
  aa: VRMExpressionPresetName.Aa,
  ih: VRMExpressionPresetName.Ih,
  ou: VRMExpressionPresetName.Ou,
  ee: VRMExpressionPresetName.Ee,
  oh: VRMExpressionPresetName.Oh,
};

/** Writes the released visemes. Motion v2 only; the legacy loop does not call this. */
export function applyVisemeRelease(
  vrm: VRM,
  weights: SpeechVisemeWeights,
  elapsedMs: number,
): void {
  const manager = vrm.expressionManager;
  if (!manager) {
    return;
  }
  const released = releasedVisemes(weights, elapsedMs);
  for (const name of SPEECH_VISEMES) {
    manager.setValue(VISEME_PRESET[name], released[name]);
  }
}

/**
 * Adds the head and forearm pulse. The chest term is applied by idle motion,
 * the only chest writer.
 */
export function applySpeechPulse(vrm: VRM, pulse: SpeechPulse): void {
  const humanoid = vrm.humanoid;
  if (!humanoid) {
    return;
  }
  const head = humanoid.getNormalizedBoneNode(VRMHumanBoneName.Head);
  if (head) {
    head.rotation.x += pulse.pitch;
    head.rotation.y += pulse.yaw;
  }
  const left = humanoid.getNormalizedBoneNode(VRMHumanBoneName.LeftLowerArm);
  const right = humanoid.getNormalizedBoneNode(VRMHumanBoneName.RightLowerArm);
  if (left) {
    left.rotation.x += pulse.forearm;
  }
  if (right) {
    right.rotation.x += pulse.forearm;
  }
}
