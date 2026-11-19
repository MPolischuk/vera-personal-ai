import type { BackdropAlignment } from "../avatar/sceneBackdrop";

export const WINDOW_ALIGNMENT = {
  floorV: 0.12,
  standU: 0.55,
  imageHeightMeters: 4.0,
} as const;

export const WINDOW_VARIANT_IDS = [
  "window-day-clear",
  "window-day-cloudy",
  "window-day-rain",
  "window-dusk-clear",
  "window-dusk-cloudy",
  "window-dusk-rain",
  "window-night-clear",
  "window-night-cloudy",
  "window-night-rain",
] as const;

export type SceneBackgroundId = (typeof WINDOW_VARIANT_IDS)[number];

export const SCENE_BACKGROUNDS = WINDOW_VARIANT_IDS.map((id) => ({
  id,
  url: `/avatars/backgrounds/${id}.png`,
  label: "Ventana" as const,
  ...WINDOW_ALIGNMENT,
}));

export const DEFAULT_SCENE_BACKGROUND_ID: SceneBackgroundId =
  "window-night-clear";

export function sceneBackgroundUrl(id: SceneBackgroundId): string {
  const match = SCENE_BACKGROUNDS.find((item) => item.id === id);
  return match?.url ?? SCENE_BACKGROUNDS[0].url;
}

export function sceneBackgroundAlignment(
  id: SceneBackgroundId,
): BackdropAlignment {
  const match = SCENE_BACKGROUNDS.find((item) => item.id === id);
  return match ?? SCENE_BACKGROUNDS[0];
}
