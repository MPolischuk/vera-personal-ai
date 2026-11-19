import {
  DirectionalLight,
  HemisphereLight,
  Mesh,
  MeshBasicMaterial,
  PerspectiveCamera,
  PlaneGeometry,
  Scene,
  SRGBColorSpace,
  WebGLRenderer,
  type Texture,
} from "three";
import { CAMERA_VERTICAL_FOV_DEG, FULL_MAX_Z } from "./cameraShot";
import {
  applyBackdropCover,
  BACKDROP_FIT_COVER,
  BACKDROP_PLANE_Z,
  backdropPlaneOffset,
  backdropPlaneSize,
  DEFAULT_BACKDROP_ALIGNMENT,
  photoPlaneSize,
  type BackdropAlignment,
} from "./sceneBackdrop";
import { createStudioBackgroundTexture } from "./studioBackground";

export interface AvatarScene {
  scene: Scene;
  camera: PerspectiveCamera;
  renderer: WebGLRenderer;
  canvas: HTMLCanvasElement;
  resize: () => void;
  setBackgroundTexture: (texture: Texture) => void;
  restoreStudioBackground: () => void;
  dispose: () => void;
}

/**
 * Imperative Three.js scene for a VRM (no OrbitControls / HDRI).
 * Photos sit on a world-space wall so camera dollies crop the room;
 * the studio gradient stays on `scene.background` as surround / fallback.
 */
export function createAvatarScene(container: HTMLElement): AvatarScene {
  const scene = new Scene();
  let studioTexture: Texture = createStudioBackgroundTexture();
  scene.background = studioTexture;
  let viewWidth = 1;
  let viewHeight = 1;

  const camera = new PerspectiveCamera(CAMERA_VERTICAL_FOV_DEG, 1, 0.1, 24);
  camera.position.set(0, 1.35, 1.55);
  camera.lookAt(0, 1.25, 0);

  const hemi = new HemisphereLight(0xf0f4f8, 0x3a4550, 0.85);
  scene.add(hemi);

  const key = new DirectionalLight(0xffffff, 1.1);
  key.position.set(0.6, 1.8, 1.2);
  scene.add(key);

  const planeMaterial = new MeshBasicMaterial({
    depthWrite: true,
    toneMapped: false,
  });
  const backdropPlane = new Mesh(new PlaneGeometry(1, 1), planeMaterial);
  backdropPlane.name = "sceneBackdropPlane";
  backdropPlane.position.set(0, 0.9, BACKDROP_PLANE_Z);
  backdropPlane.frustumCulled = false;
  backdropPlane.visible = false;
  scene.add(backdropPlane);

  const renderer = new WebGLRenderer({ antialias: true, alpha: true });
  renderer.outputColorSpace = SRGBColorSpace;
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));

  const canvas = renderer.domElement;
  canvas.style.display = "block";
  canvas.style.width = "100%";
  canvas.style.height = "100%";
  container.appendChild(canvas);

  const layoutBackdropPlane = () => {
    const map = planeMaterial.map;
    const alignment =
      (map?.userData.alignment as BackdropAlignment | undefined) ??
      DEFAULT_BACKDROP_ALIGNMENT;
    const image = map?.image as { width?: number; height?: number } | undefined;
    const imageWidth = image?.width ?? 0;
    const imageHeight = image?.height ?? 0;
    if (
      backdropPlane.visible &&
      imageWidth > 0 &&
      imageHeight > 0 &&
      alignment.imageHeightMeters > 0
    ) {
      const { width, height } = photoPlaneSize(
        imageWidth,
        imageHeight,
        alignment.imageHeightMeters,
      );
      backdropPlane.scale.set(width, height, 1);
      return;
    }
    const { width, height } = backdropPlaneSize(
      camera.aspect,
      FULL_MAX_Z,
      BACKDROP_PLANE_Z,
      CAMERA_VERTICAL_FOV_DEG,
    );
    backdropPlane.scale.set(width, height, 1);
  };

  const fitPhotoOnPlane = () => {
    const map = planeMaterial.map;
    if (!map || !backdropPlane.visible) {
      return;
    }
    if (map.userData.fit !== BACKDROP_FIT_COVER) {
      return;
    }
    const width = Math.max(backdropPlane.scale.x, 1);
    const height = Math.max(backdropPlane.scale.y, 1);
    applyBackdropCover(map, width, height);
    const alignment =
      (map.userData.alignment as BackdropAlignment | undefined) ??
      DEFAULT_BACKDROP_ALIGNMENT;
    const pose = backdropPlaneOffset(width, height, alignment, {
      repeatX: map.repeat.x,
      repeatY: map.repeat.y,
      offsetX: map.offset.x,
      offsetY: map.offset.y,
    });
    backdropPlane.position.set(pose.x, pose.y, BACKDROP_PLANE_Z);
  };

  const clearPhoto = () => {
    backdropPlane.visible = false;
    const map = planeMaterial.map;
    if (map) {
      map.dispose();
      planeMaterial.map = null;
      planeMaterial.needsUpdate = true;
    }
  };

  const setBackgroundTexture = (texture: Texture) => {
    if (texture.userData.fit === BACKDROP_FIT_COVER) {
      const previous = planeMaterial.map;
      planeMaterial.map = texture;
      planeMaterial.needsUpdate = true;
      backdropPlane.visible = true;
      scene.background = studioTexture;
      layoutBackdropPlane();
      fitPhotoOnPlane();
      if (previous && previous !== texture) {
        previous.dispose();
      }
      return;
    }

    clearPhoto();
    const previousStudio = studioTexture;
    studioTexture = texture;
    scene.background = texture;
    if (previousStudio !== texture) {
      previousStudio.dispose();
    }
  };

  const restoreStudioBackground = () => {
    setBackgroundTexture(createStudioBackgroundTexture());
  };

  const resize = () => {
    viewWidth = Math.max(container.clientWidth, 1);
    viewHeight = Math.max(container.clientHeight, 1);
    camera.aspect = viewWidth / viewHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(viewWidth, viewHeight, false);
    layoutBackdropPlane();
    fitPhotoOnPlane();
  };

  resize();

  const observer = new ResizeObserver(() => resize());
  observer.observe(container);

  const dispose = () => {
    observer.disconnect();
    scene.background = null;
    clearPhoto();
    studioTexture.dispose();
    scene.remove(backdropPlane);
    backdropPlane.geometry.dispose();
    planeMaterial.dispose();
    scene.remove(hemi);
    scene.remove(key);
    hemi.dispose();
    key.dispose();
    renderer.dispose();
    renderer.forceContextLoss();
    if (canvas.parentElement === container) {
      container.removeChild(canvas);
    }
  };

  return {
    scene,
    camera,
    renderer,
    canvas,
    resize,
    setBackgroundTexture,
    restoreStudioBackground,
    dispose,
  };
}
