import type { BackdropAlignment } from "../avatar/sceneBackdrop";
import { WINDOW_ALIGNMENT } from "./sceneBackgrounds";
import {
  solarSunriseSunset,
  timeBand,
  windowBackgroundUrl,
  type SkyCondition,
} from "./windowScene";

/**
 * Optional static plate. Off by default: the nine window plates follow hour and weather.
 * Set enabled to true to pin the office photo instead.
 *
 * The office plate is taller than the room itself: the desk, plant and picture
 * keep a 2.65 m room, and the extra wall and floor keep the full-body camera
 * inside the photo. The window plates stay at 4 m.
 */
export const DEMO_SCENE = {
  enabled: false,
  url: "/avatars/backgrounds/office-dotnetconf.png",
  alignment: {
    floorV: 0.2718,
    standU: 0.4978,
    imageHeightMeters: 3.6069,
  },
} as const;

export function selectSceneBackground(
  at: Date,
  condition: SkyCondition,
  demoEnabled: boolean = DEMO_SCENE.enabled,
): { url: string; alignment: BackdropAlignment } {
  if (demoEnabled) {
    return { url: DEMO_SCENE.url, alignment: DEMO_SCENE.alignment };
  }
  const { sunrise, sunset } = solarSunriseSunset(at);
  return {
    url: windowBackgroundUrl(timeBand(at, sunrise, sunset), condition),
    alignment: WINDOW_ALIGNMENT,
  };
}
