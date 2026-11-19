import {
  ClampToEdgeWrapping,
  LinearFilter,
  SRGBColorSpace,
  Texture,
  TextureLoader,
} from "three";
import { CAMERA_VERTICAL_FOV_DEG } from "./cameraShot";

export interface SceneBackdropTarget {
  setBackgroundTexture: (texture: Texture) => void;
  restoreStudioBackground: () => void;
}

export interface BackdropAlignment {
  /** Image V of the floor contact. 0 = bottom of the photo, 1 = top. */
  floorV: number;
  /** Image U that should sit behind the avatar. 0 = left, 1 = right. */
  standU: number;
  /**
   * Real-world height in meters of the full photo. Furniture is scaled to this,
   * not to the camera frustum (a ~1.6 m avatar next to a ~1 m chair).
   */
  imageHeightMeters: number;
}

export const DEFAULT_BACKDROP_ALIGNMENT: BackdropAlignment = {
  floorV: 0.12,
  standU: 0.5,
  imageHeightMeters: 3.9,
};

export const BACKDROP_FIT_COVER = "cover";

/** World Z of the photo wall (avatar stands at 0, camera looks from +Z). */
export const BACKDROP_PLANE_Z = -1.8;

const BACKDROP_PLANE_MARGIN = 1.22;

/**
 * Size a world-space photo wall so the farthest camera shot still fills it.
 * Closer shots then crop the same wall — the photo dollies with the avatar.
 */
export function backdropPlaneSize(
  aspect: number,
  cameraZ: number,
  planeZ: number = BACKDROP_PLANE_Z,
  fovDeg: number = CAMERA_VERTICAL_FOV_DEG,
  margin: number = BACKDROP_PLANE_MARGIN,
): { width: number; height: number } {
  const distance = Math.max(cameraZ - planeZ, 0.1);
  const height =
    2 * distance * Math.tan((fovDeg * Math.PI) / 360) * margin;
  return { width: height * Math.max(aspect, 0.05), height };
}

/**
 * Photo wall sized to a real room, using the image aspect (not the viewport).
 * A typical interior frame is ~2.5–2.8 m tall so chairs sit below head height.
 */
export function photoPlaneSize(
  imageWidth: number,
  imageHeight: number,
  imageHeightMeters: number,
): { width: number; height: number } {
  const aspect = imageWidth / Math.max(imageHeight, 1);
  const height = Math.max(imageHeightMeters, 0.5);
  return { width: height * Math.max(aspect, 0.05), height };
}

export function coverRepeatOffset(
  viewAspect: number,
  imageAspect: number,
  verticalAnchor: "center" | "floor" = "floor",
): { repeatX: number; repeatY: number; offsetX: number; offsetY: number } {
  if (!(viewAspect > 0) || !(imageAspect > 0)) {
    return { repeatX: 1, repeatY: 1, offsetX: 0, offsetY: 0 };
  }

  if (viewAspect > imageAspect) {
    const repeatY = imageAspect / viewAspect;
    return {
      repeatX: 1,
      repeatY,
      offsetX: 0,
      offsetY: verticalAnchor === "floor" ? 0 : (1 - repeatY) / 2,
    };
  }

  const repeatX = viewAspect / imageAspect;
  return { repeatX, repeatY: 1, offsetX: (1 - repeatX) / 2, offsetY: 0 };
}

/**
 * Places the photo wall so `floorV`/`standU` land at the origin (avatar feet).
 */
export function backdropPlaneOffset(
  planeWidth: number,
  planeHeight: number,
  alignment: BackdropAlignment,
  cover: { repeatX: number; repeatY: number; offsetX: number; offsetY: number },
): { x: number; y: number } {
  const u =
    (alignment.standU - cover.offsetX) / Math.max(cover.repeatX, 1e-6);
  const v =
    (alignment.floorV - cover.offsetY) / Math.max(cover.repeatY, 1e-6);
  return {
    x: (0.5 - u) * planeWidth,
    y: (0.5 - v) * planeHeight,
  };
}

export function applyBackdropCover(
  texture: Texture,
  viewWidth: number,
  viewHeight: number,
): void {
  const image = texture.image as { width?: number; height?: number } | undefined;
  const imageWidth = image?.width ?? 0;
  const imageHeight = image?.height ?? 0;
  const { repeatX, repeatY, offsetX, offsetY } = coverRepeatOffset(
    viewWidth / Math.max(viewHeight, 1),
    imageWidth / Math.max(imageHeight, 1),
  );
  texture.wrapS = ClampToEdgeWrapping;
  texture.wrapT = ClampToEdgeWrapping;
  texture.repeat.set(repeatX, repeatY);
  texture.offset.set(offsetX, offsetY);
  texture.needsUpdate = true;
}

export function loadBackdropTexture(
  url: string,
  signal?: AbortSignal,
): Promise<Texture> {
  const loader = new TextureLoader();
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException("Aborted", "AbortError"));
      return;
    }

    const onAbort = () => {
      reject(new DOMException("Aborted", "AbortError"));
    };
    signal?.addEventListener("abort", onAbort, { once: true });

    loader.load(
      url,
      (texture) => {
        signal?.removeEventListener("abort", onAbort);
        if (signal?.aborted) {
          texture.dispose();
          reject(new DOMException("Aborted", "AbortError"));
          return;
        }
        texture.colorSpace = SRGBColorSpace;
        texture.generateMipmaps = false;
        texture.minFilter = LinearFilter;
        texture.magFilter = LinearFilter;
        texture.userData.fit = BACKDROP_FIT_COVER;
        resolve(texture);
      },
      undefined,
      () => {
        signal?.removeEventListener("abort", onAbort);
        reject(new Error(`Failed to load scene backdrop: ${url}`));
      },
    );
  });
}

/**
 * Photo on the scene wall when `url` loads; studio gradient stays if the load
 * fails or is aborted. Mapping stays 2D (not HDRI / environment map).
 */
export async function applySceneBackdrop(
  scene: SceneBackdropTarget,
  url: string | null,
  signal?: AbortSignal,
  alignment?: BackdropAlignment,
): Promise<void> {
  if (!url) {
    scene.restoreStudioBackground();
    return;
  }

  try {
    const texture = await loadBackdropTexture(url, signal);
    if (signal?.aborted) {
      texture.dispose();
      return;
    }
    texture.userData.alignment = alignment ?? DEFAULT_BACKDROP_ALIGNMENT;
    scene.setBackgroundTexture(texture);
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      return;
    }
  }
}
