/**
 * Static gesture id → Mixamo GLB URL. Filename = whitelist id (D2/D3).
 * Do not read personality YAML here.
 */
export const CLIP_CATALOG: Readonly<Record<string, string>> = {
  // `wave` was a Mixamo clip (not redistributable). The protocol wave now runs
  // the procedural WAVE_POSE one-shot from gestureCatalog/gestureQueue.
};

export function clipUrlForGesture(name: string): string | undefined {
  return CLIP_CATALOG[name];
}
