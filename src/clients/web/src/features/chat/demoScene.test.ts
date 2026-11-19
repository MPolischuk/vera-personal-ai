import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { DEMO_SCENE, selectSceneBackground } from "./demoScene";
import { SCENE_BACKGROUNDS, WINDOW_ALIGNMENT } from "./sceneBackgrounds";
import { solarSunriseSunset, timeBand, windowBackgroundUrl } from "./windowScene";

const featureDir = path.dirname(fileURLToPath(import.meta.url));
const officePlate = path.resolve(
  featureDir,
  "../../../public/avatars/backgrounds/office-dotnetconf.png",
);

describe("optional office plate", () => {
  it("keeps the office plate behind an explicit flag", () => {
    expect(DEMO_SCENE.enabled).toBe(false);
    const noon = new Date("2026-11-16T15:00:00.000Z");
    const night = new Date("2026-11-16T03:00:00.000Z");
    expect(selectSceneBackground(noon, "rain", true).url).toBe(DEMO_SCENE.url);
    expect(selectSceneBackground(night, "clear", true).url).toBe(DEMO_SCENE.url);
    expect(selectSceneBackground(noon, "rain", true).alignment).toEqual(
      DEMO_SCENE.alignment,
    );
    expect(selectSceneBackground(noon, "rain").url).not.toBe(DEMO_SCENE.url);
  });

  it("restores the hour-and-weather plate when the flag is off", () => {
    const at = new Date("2026-06-21T15:00:00.000Z");
    const { sunrise, sunset } = solarSunriseSunset(at);
    for (const condition of ["clear", "cloudy", "rain"] as const) {
      const selected = selectSceneBackground(at, condition, false);
      expect(selected.url).toBe(
        windowBackgroundUrl(timeBand(at, sunrise, sunset), condition),
      );
      expect(selected.alignment).toEqual(WINDOW_ALIGNMENT);
    }
  });

  it("keeps the office plate out of the nine window variants", () => {
    expect(SCENE_BACKGROUNDS.map((item) => item.id)).not.toContain(
      "office-dotnetconf",
    );
    expect(fs.existsSync(officePlate)).toBe(true);
    const header = fs.readFileSync(officePlate);
    expect(header.readUInt32BE(16)).toBe(1980);
    expect(header.readUInt32BE(20)).toBe(980);
  });

  it("leaves the weather consultation in place behind the flag", () => {
    const source = fs.readFileSync(
      path.join(featureDir, "useWindowBackground.ts"),
      "utf8",
    );
    expect(source).toContain("DEMO_SCENE.enabled");
    expect(source).toContain("fetchWeatherCode");
    expect(source).toContain("selectSceneBackground");
  });
});
