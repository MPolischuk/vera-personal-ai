/**
 * Maps a personality avatar.model path to a same-origin SPA URL.
 * Example: "avatars/default.vrm" → "/avatars/default.vrm"
 */
export function resolveAvatarModelUrl(model: string): string {
  const trimmed = model.trim().replace(/^\/+/, "");
  return `/${trimmed}`;
}
