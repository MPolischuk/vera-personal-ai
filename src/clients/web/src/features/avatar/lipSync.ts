import type { VRM } from "@pixiv/three-vrm";
import { VRMLipSync } from "three-vrm-lip-sync";

/** Mouth openness multiplier (lib default 1). Tuned for TTS playback. */
export const LIP_SYNC_GAIN = 1.25;

/** Articulation smoothing in seconds (lib default 0.05). */
export const LIP_SYNC_SMOOTHNESS = 0.05;

/** Creates VRMLipSync on the shared AudioContext. Analysis only — no playUrl. */
export async function createAvatarLipSync(
  vrm: VRM,
  audioContext: AudioContext,
): Promise<VRMLipSync> {
  return VRMLipSync.create(vrm, {
    audioContext,
    gain: LIP_SYNC_GAIN,
    smoothness: LIP_SYNC_SMOOTHNESS,
    autoRelease: true,
  });
}

/** Lip-sync loudness as a finite number. Silence when the engine is absent. */
export function readLipSyncVolume(lipSync: VRMLipSync | null): number {
  const volume = lipSync?.engine.volume;
  if (typeof volume !== "number" || !Number.isFinite(volume)) {
    return 0;
  }
  return volume;
}
