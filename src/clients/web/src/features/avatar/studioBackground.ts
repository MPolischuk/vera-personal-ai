import { CanvasTexture, SRGBColorSpace } from "three";

/** Cool mid-tone at the top of the studio gradient (not the PT-16 solid). */
export const STUDIO_BACKGROUND_TOP = 0x3d536c;

/** Near-black floor stop of the studio gradient. */
export const STUDIO_BACKGROUND_BOTTOM = 0x0a0e14;

function toCssHex(color: number): string {
  return `#${color.toString(16).padStart(6, "0")}`;
}

/**
 * Vertical studio backdrop for `scene.background`.
 * Mapping stays default (2D plane) — not equirect / HDRI.
 */
export function createStudioBackgroundTexture(): CanvasTexture {
  const width = 8;
  const height = 256;
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;

  const ctx = canvas.getContext("2d");
  if (!ctx) {
    throw new Error("2D canvas context is unavailable for studio background.");
  }

  const gradient = ctx.createLinearGradient(0, 0, 0, height);
  gradient.addColorStop(0, toCssHex(STUDIO_BACKGROUND_TOP));
  gradient.addColorStop(1, toCssHex(STUDIO_BACKGROUND_BOTTOM));
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, width, height);

  const texture = new CanvasTexture(canvas);
  texture.colorSpace = SRGBColorSpace;
  texture.generateMipmaps = false;
  texture.needsUpdate = true;
  return texture;
}
