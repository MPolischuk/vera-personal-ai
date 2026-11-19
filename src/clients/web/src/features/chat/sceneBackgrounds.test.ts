import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import {
  DEFAULT_SCENE_BACKGROUND_ID,
  SCENE_BACKGROUNDS,
  WINDOW_ALIGNMENT,
  WINDOW_VARIANT_IDS,
  sceneBackgroundAlignment,
  sceneBackgroundUrl,
} from "./sceneBackgrounds";

const featureDir = path.dirname(fileURLToPath(import.meta.url));
const backgroundsDir = path.resolve(
  featureDir,
  "../../../public/avatars/backgrounds",
);

describe("sceneBackgrounds catalog", () => {
  it("lists the nine window variants in order", () => {
    expect(SCENE_BACKGROUNDS).toHaveLength(9);
    expect(SCENE_BACKGROUNDS.map((item) => item.id)).toEqual([
      ...WINDOW_VARIANT_IDS,
    ]);
    expect(DEFAULT_SCENE_BACKGROUND_ID).toBe("window-night-clear");
  });

  it("resolves each variant url and ships the png", () => {
    for (const item of SCENE_BACKGROUNDS) {
      expect(item.url).toBe(`/avatars/backgrounds/${item.id}.png`);
      expect(sceneBackgroundUrl(item.id)).toBe(item.url);
      const file = path.join(backgroundsDir, `${item.id}.png`);
      expect(fs.existsSync(file)).toBe(true);
    }
  });

  it("matches WINDOW_ALIGNMENT on every entry", () => {
    for (const item of SCENE_BACKGROUNDS) {
      const align = sceneBackgroundAlignment(item.id);
      expect(align.floorV).toBe(WINDOW_ALIGNMENT.floorV);
      expect(align.standU).toBe(WINDOW_ALIGNMENT.standU);
      expect(align.imageHeightMeters).toBe(WINDOW_ALIGNMENT.imageHeightMeters);
    }
  });

  it("drops the previous plates", () => {
    for (const name of ["night-window.png", "study.png", "dusk-interior.png"]) {
      expect(fs.existsSync(path.join(backgroundsDir, name))).toBe(false);
    }
  });

  it("does not mention the stored scene id", () => {
    const files = [
      "sceneBackgrounds.ts",
      "ChatView.tsx",
      "StageChrome.tsx",
      "BankChrome.tsx",
    ];
    const storedSceneKey = "assistant." + "sceneBackgroundId";
    for (const file of files) {
      const source = fs.readFileSync(path.join(featureDir, file), "utf8");
      expect(source).not.toContain(storedSceneKey);
    }

    const moduleSource = fs.readFileSync(
      path.join(featureDir, "sceneBackgrounds.ts"),
      "utf8",
    );
    expect(moduleSource).not.toContain("localStorage");
  });
});
