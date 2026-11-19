import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import {
  STUDIO_BACKGROUND_BOTTOM,
  STUDIO_BACKGROUND_TOP,
} from "./studioBackground";

const PT16_SOLID = 0x1a2332;
const avatarDir = path.dirname(fileURLToPath(import.meta.url));

describe("studioBackground stops", () => {
  it("does not reuse the PT-16 solid background color", () => {
    expect(STUDIO_BACKGROUND_TOP).not.toBe(PT16_SOLID);
    expect(STUDIO_BACKGROUND_BOTTOM).not.toBe(PT16_SOLID);
  });

  it("uses the declared studio gradient stops", () => {
    expect(STUDIO_BACKGROUND_TOP).toBe(0x3d536c);
    expect(STUDIO_BACKGROUND_BOTTOM).toBe(0x0a0e14);
  });
});

describe("createAvatarScene background source", () => {
  it("no longer contains the PT-16 solid literal 0x1a2332", () => {
    const sceneSource = fs.readFileSync(
      path.join(avatarDir, "createAvatarScene.ts"),
      "utf8",
    );
    expect(sceneSource).not.toContain("0x1a2332");
  });

  it("does not use HDRI or equirect mapping on the backdrop", () => {
    const files = ["createAvatarScene.ts", "sceneBackdrop.ts"];
    for (const name of files) {
      const source = fs.readFileSync(path.join(avatarDir, name), "utf8");
      expect(source).not.toContain("EquirectangularReflectionMapping");
      expect(source).not.toContain("RGBELoader");
      expect(source).not.toContain("scene.environment");
    }
  });
});
