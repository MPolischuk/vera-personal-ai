import type { VRM } from "@pixiv/three-vrm";
import { VRMHumanBoneName } from "@pixiv/three-vrm";
import {
  CanvasTexture,
  Sprite,
  SpriteMaterial,
  SRGBColorSpace,
  Vector3,
  type Object3D,
} from "three";
import { chooseSide, type ArmSide, type Vec3 } from "./arms";
import type { CameraShot } from "./cameraShot";

/** Store cap. A third invoke is ignored until one leaves. */
export const MAX_ACTIVE_TOOLS = 2;

/** Failed completion keeps the alert shape for this long, then the slot leaves. */
export const TOOL_ALERT_MS = 400;

/** Eyes lead the arm by this much. */
export const LOOK_LEAD_MS = 150;

/** The icon waits for the hand to arrive (arm attack is ~400 ms after the look lead). */
export const TOOL_ICON_APPEAR_DELAY_MS = 380;
export const TOOL_ICON_ENTER_MS = 450;
/** Exit is a soft fade while the hand lowers; the slot lives this long. */
export const TOOL_ICON_EXIT_MS = 420;
export const TOOL_ICON_FLOAT_M = 0.006;
/** Sprite size including the glow halo; the white disc is ~55% of it (~6 cm). */
export const TOOL_ICON_BASE_SCALE = 0.13;
/** Enter starts this far below the rest height (rises out of the palm). */
export const TOOL_ICON_RISE_M = 0.025;
/** Exit drifts up this much while fading. */
export const TOOL_ICON_EXIT_LIFT_M = 0.03;
/** Eyes go to the icon until this age, then back to the camera. */
export const TOOL_GLANCE_END_MS = 1300;

/** Disc centre above the palm centre (world up, the palm faces up). */
const PALM_LIFT_M = 0.055;
/** Palm centre sits this far from the wrist toward the middle knuckle. */
const PALM_CENTER_T = 0.55;

const ICON_PX = 128;
const FLOAT_HZ = 0.45;

export type ToolIconKind = "search" | "bell" | "gear" | "alert";

export type ToolChimeState = "idle" | "listening" | "thinking" | "speaking";

export type ToolChimeDirection = "rise" | "fall";

export interface ActiveTool {
  id: number;
  name: string;
  alert: boolean;
}

export interface ToolIconSlot {
  id: number;
  name: string;
  alert: boolean;
  kind: ToolIconKind;
  /** Chosen once, when the slot appears. */
  side: ArmSide;
  gesture: "point" | "hold";
  anchor: "air" | "palm";
  dir: Vec3;
  bornMs: number;
  armAtMs: number;
  exitStartedMs: number | null;
  enteredChime: boolean;
  exitedChime: boolean;
}

export interface ToolIconGesture {
  id: number;
  gesture: "point" | "hold";
  side: ArmSide;
  dir: Vec3;
  armAtMs: number;
}

export interface ToolSpriteLayer {
  sprites: Map<number, Sprite>;
}

const SIDE_AIM: Record<ArmSide, Vec3> = {
  R: { x: -0.32, y: 0.02, z: 0.4 },
  L: { x: 0.32, y: 0.02, z: 0.4 },
};

const textures = new Map<ToolIconKind, CanvasTexture>();
const _anchor = new Vector3();
const _knuckle = new Vector3();

export function admitActiveTool(
  tools: readonly ActiveTool[],
  name: string,
): readonly ActiveTool[] {
  const trimmed = name.trim();
  if (!trimmed || tools.length >= MAX_ACTIVE_TOOLS) {
    return tools;
  }
  const id = tools.reduce((max, tool) => Math.max(max, tool.id), 0) + 1;
  return [...tools, { id, name: trimmed, alert: false }];
}

/** Drops the oldest entry with this name. */
export function completeActiveTool(
  tools: readonly ActiveTool[],
  name: string,
): readonly ActiveTool[] {
  const index = tools.findIndex((tool) => tool.name === name);
  if (index < 0) {
    return tools;
  }
  return tools.filter((_, i) => i !== index);
}

export function markToolAlert(
  tools: readonly ActiveTool[],
  name: string,
): { tools: readonly ActiveTool[]; id: number | null; already: boolean } {
  const index = tools.findIndex((tool) => tool.name === name);
  if (index < 0) {
    return { tools, id: null, already: false };
  }
  const current = tools[index];
  if (!current || current.alert) {
    return { tools, id: current?.id ?? null, already: true };
  }
  return {
    tools: tools.map((tool, i) => (i === index ? { ...tool, alert: true } : tool)),
    id: current.id,
    already: false,
  };
}

export function dropActiveTool(tools: readonly ActiveTool[], id: number): ActiveTool[] {
  return tools.filter((tool) => tool.id !== id);
}

export function toolIconKind(name: string, alert: boolean): ToolIconKind {
  if (alert) {
    return "alert";
  }
  const lower = name.toLowerCase();
  if (lower.includes("search")) {
    return "search";
  }
  if (lower.includes("remind")) {
    return "bell";
  }
  return "gear";
}

export function shouldPlayToolChime(state: ToolChimeState, tabHidden: boolean): boolean {
  return state !== "speaking" && !tabHidden;
}

/** Marks each edge once. Silent edges stay consumed so they do not beep later. */
export function takeToolChimes(
  slots: readonly ToolIconSlot[],
  allow: boolean,
): ToolChimeDirection[] {
  const played: ToolChimeDirection[] = [];
  for (const slot of slots) {
    if (!slot.enteredChime) {
      slot.enteredChime = true;
      if (allow) {
        played.push("rise");
      }
    }
    if (slot.exitStartedMs !== null && !slot.exitedChime) {
      slot.exitedChime = true;
      if (allow) {
        played.push("fall");
      }
    }
  }
  return played;
}

export function playToolChime(
  ctx: AudioContext,
  direction: ToolChimeDirection,
  atSec = ctx.currentTime,
): void {
  const osc = ctx.createOscillator();
  const gain = ctx.createGain();
  osc.type = "sine";
  const from = direction === "rise" ? 520 : 780;
  const to = direction === "rise" ? 780 : 420;
  osc.frequency.setValueAtTime(from, atSec);
  osc.frequency.exponentialRampToValueAtTime(to, atSec + 0.12);
  gain.gain.setValueAtTime(0.0001, atSec);
  gain.gain.exponentialRampToValueAtTime(0.035, atSec + 0.02);
  gain.gain.exponentialRampToValueAtTime(0.0001, atSec + 0.14);
  osc.connect(gain);
  gain.connect(ctx.destination);
  osc.start(atSec);
  osc.stop(atSec + 0.16);
}

function clamp01(value: number): number {
  if (value <= 0) {
    return 0;
  }
  if (value >= 1) {
    return 1;
  }
  return value;
}

function easeOutCubic(t: number): number {
  const p = 1 - clamp01(t);
  return 1 - p * p * p;
}

function easeInOut(t: number): number {
  const x = clamp01(t);
  return x * x * (3 - 2 * x);
}

/** 0..1 enter progress; 0 until the hand has had time to arrive. */
export function toolIconEnter(elapsedMs: number): number {
  return easeOutCubic((elapsedMs - TOOL_ICON_APPEAR_DELAY_MS) / TOOL_ICON_ENTER_MS);
}

/** 0..1 exit progress. */
export function toolIconExit(exitElapsedMs: number | null): number {
  return exitElapsedMs === null ? 0 : easeInOut(exitElapsedMs / TOOL_ICON_EXIT_MS);
}

/** Grows from 55% to 100% without overshoot; shrinks only to 70% on exit (the fade does the rest). */
export function toolIconScale(elapsedMs: number, exitElapsedMs: number | null): number {
  const enter = toolIconEnter(elapsedMs);
  const exit = toolIconExit(exitElapsedMs);
  return TOOL_ICON_BASE_SCALE * (0.55 + 0.45 * enter) * (1 - 0.3 * exit);
}

export function toolIconOpacity(elapsedMs: number, exitElapsedMs: number | null): number {
  return toolIconEnter(elapsedMs) * (1 - toolIconExit(exitElapsedMs));
}

/** Vertical offset from the rest height: rises out of the palm, drifts up on exit. */
export function toolIconLift(elapsedMs: number, exitElapsedMs: number | null): number {
  return -TOOL_ICON_RISE_M * (1 - toolIconEnter(elapsedMs)) + TOOL_ICON_EXIT_LIFT_M * toolIconExit(exitElapsedMs);
}

/** Small float that fades in with the icon so it does not jump. */
export function toolIconFloat(nowMs: number, amount = 1): number {
  return Math.sin((nowMs / 1000) * Math.PI * 2 * FLOAT_HZ) * TOOL_ICON_FLOAT_M * amount;
}

/** Glance at the icon right after it appears, then back to the user. */
export function toolGlanceActive(slot: ToolIconSlot, nowMs: number): boolean {
  if (slot.exitStartedMs !== null) {
    return false;
  }
  const age = nowMs - slot.bornMs;
  return age >= 0 && age < TOOL_GLANCE_END_MS;
}

function pickSide(used: ReadonlySet<ArmSide>): ArmSide {
  const order: readonly ArmSide[] = ["R", "L"];
  for (const side of order) {
    if (used.has(side)) {
      continue;
    }
    const chosen = chooseSide(SIDE_AIM[side]).side;
    if (!used.has(chosen)) {
      return chosen;
    }
  }
  return chooseSide(SIDE_AIM.R).side;
}

function aimDir(side: ArmSide, anchor: "air" | "palm"): Vec3 {
  return {
    x: side === "L" ? 0.34 : -0.34,
    y: anchor === "air" ? 0.22 : 0.04,
    z: 1,
  };
}

function spawnSlot(tool: ActiveTool, shot: CameraShot, used: ReadonlySet<ArmSide>, nowMs: number): ToolIconSlot {
  // Every shot presents the icon on the palm. The camera shot no longer
  // switches to a point-at-the-air gesture (the icon fell off frame in bust).
  void shot;
  const gesture = "hold";
  const anchor = "palm";
  const side = pickSide(used);
  return {
    id: tool.id,
    name: tool.name,
    alert: tool.alert,
    kind: toolIconKind(tool.name, tool.alert),
    side,
    gesture,
    anchor,
    dir: aimDir(side, anchor),
    bornMs: nowMs,
    armAtMs: nowMs + LOOK_LEAD_MS,
    exitStartedMs: null,
    enteredChime: false,
    exitedChime: false,
  };
}

/**
 * Side, gesture, and anchor stay as they were on the first frame.
 * A removed tool exits for 300 ms. A failure flips the shape to the alert.
 */
export function syncToolIcons(
  slots: readonly ToolIconSlot[],
  tools: readonly ActiveTool[],
  shot: CameraShot,
  nowMs: number,
): ToolIconSlot[] {
  const live = new Map(tools.map((tool) => [tool.id, tool]));
  const next: ToolIconSlot[] = [];
  for (const slot of slots) {
    const tool = live.get(slot.id);
    if (!tool) {
      if (slot.exitStartedMs === null) {
        next.push({ ...slot, exitStartedMs: nowMs });
      } else if (nowMs - slot.exitStartedMs < TOOL_ICON_EXIT_MS) {
        next.push(slot);
      }
      continue;
    }
    next.push({
      ...slot,
      name: tool.name,
      alert: tool.alert,
      kind: toolIconKind(tool.name, tool.alert),
    });
  }
  const used = new Set(
    next.filter((slot) => slot.exitStartedMs === null).map((slot) => slot.side),
  );
  for (const tool of tools) {
    if (next.some((slot) => slot.id === tool.id)) {
      continue;
    }
    const slot = spawnSlot(tool, shot, used, nowMs);
    used.add(slot.side);
    next.push(slot);
  }
  return next;
}

/** The hand follows the oldest icon that is still on screen. */
export function primaryToolGesture(
  slots: readonly ToolIconSlot[],
): ToolIconGesture | null {
  const slot = slots.find((item) => item.exitStartedMs === null);
  if (!slot) {
    return null;
  }
  return {
    id: slot.id,
    gesture: slot.gesture,
    side: slot.side,
    dir: slot.dir,
    armAtMs: slot.armAtMs,
  };
}

/** Icons are drawn on a 64-unit grid centred in a 128 px canvas with a soft glow. */
function drawDisc(ctx: CanvasRenderingContext2D): void {
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.clearRect(0, 0, ICON_PX, ICON_PX);
  const c = ICON_PX / 2;
  const glow = ctx.createRadialGradient(c, c, 18, c, c, c);
  glow.addColorStop(0, "rgba(170,215,255,0.55)");
  glow.addColorStop(0.55, "rgba(150,200,255,0.22)");
  glow.addColorStop(1, "rgba(150,200,255,0)");
  ctx.fillStyle = glow;
  ctx.fillRect(0, 0, ICON_PX, ICON_PX);
  ctx.translate(c - 32, c - 32);
  ctx.fillStyle = "rgba(255,255,255,0.96)";
  ctx.beginPath();
  ctx.arc(32, 32, 30, 0, Math.PI * 2);
  ctx.fill();
  ctx.strokeStyle = "rgba(120,170,230,0.9)";
  ctx.lineWidth = 2;
  ctx.stroke();
  ctx.strokeStyle = "#1b2430";
  ctx.lineWidth = 3.5;
  ctx.lineCap = "round";
  ctx.lineJoin = "round";
}

function drawSearch(ctx: CanvasRenderingContext2D): void {
  ctx.beginPath();
  ctx.arc(27, 27, 10, 0, Math.PI * 2);
  ctx.stroke();
  ctx.beginPath();
  ctx.moveTo(34, 34);
  ctx.lineTo(46, 46);
  ctx.stroke();
}

function drawBell(ctx: CanvasRenderingContext2D): void {
  ctx.beginPath();
  ctx.moveTo(32, 14);
  ctx.quadraticCurveTo(48, 20, 46, 36);
  ctx.lineTo(18, 36);
  ctx.quadraticCurveTo(16, 20, 32, 14);
  ctx.closePath();
  ctx.stroke();
  ctx.beginPath();
  ctx.moveTo(22, 36);
  ctx.lineTo(42, 36);
  ctx.stroke();
  ctx.beginPath();
  ctx.arc(32, 42, 3.5, 0, Math.PI * 2);
  ctx.stroke();
}

function drawGear(ctx: CanvasRenderingContext2D): void {
  ctx.beginPath();
  ctx.arc(32, 32, 8, 0, Math.PI * 2);
  ctx.stroke();
  for (let tooth = 0; tooth < 8; tooth += 1) {
    const angle = (tooth / 8) * Math.PI * 2;
    ctx.beginPath();
    ctx.moveTo(32 + Math.cos(angle) * 12, 32 + Math.sin(angle) * 12);
    ctx.lineTo(32 + Math.cos(angle) * 18, 32 + Math.sin(angle) * 18);
    ctx.stroke();
  }
}

function drawAlert(ctx: CanvasRenderingContext2D): void {
  ctx.beginPath();
  ctx.moveTo(32, 12);
  ctx.lineTo(52, 48);
  ctx.lineTo(12, 48);
  ctx.closePath();
  ctx.stroke();
  ctx.beginPath();
  ctx.moveTo(32, 24);
  ctx.lineTo(32, 36);
  ctx.stroke();
  ctx.beginPath();
  ctx.arc(32, 42, 1.8, 0, Math.PI * 2);
  ctx.fillStyle = "#1b2430";
  ctx.fill();
}

export function drawToolIcon(ctx: CanvasRenderingContext2D, kind: ToolIconKind): void {
  drawDisc(ctx);
  switch (kind) {
    case "search":
      drawSearch(ctx);
      break;
    case "bell":
      drawBell(ctx);
      break;
    case "alert":
      drawAlert(ctx);
      break;
    default:
      drawGear(ctx);
      break;
  }
}

function textureFor(kind: ToolIconKind): CanvasTexture {
  const cached = textures.get(kind);
  if (cached) {
    return cached;
  }
  const canvas = document.createElement("canvas");
  canvas.width = ICON_PX;
  canvas.height = ICON_PX;
  const ctx = canvas.getContext("2d");
  if (!ctx) {
    throw new Error("2D canvas context is unavailable for a tool icon.");
  }
  drawToolIcon(ctx, kind);
  const texture = new CanvasTexture(canvas);
  texture.colorSpace = SRGBColorSpace;
  texture.generateMipmaps = false;
  texture.needsUpdate = true;
  textures.set(kind, texture);
  return texture;
}

export function createToolIconSprite(kind: ToolIconKind): Sprite {
  const material = new SpriteMaterial({
    map: textureFor(kind),
    transparent: true,
    depthWrite: false,
    opacity: 0,
  });
  const sprite = new Sprite(material);
  sprite.userData.kind = kind;
  sprite.renderOrder = 2;
  sprite.scale.setScalar(0);
  return sprite;
}

function paintToolIcon(sprite: Sprite, kind: ToolIconKind): void {
  const material = sprite.material;
  if (Array.isArray(material)) {
    return;
  }
  material.map = textureFor(kind);
  material.needsUpdate = true;
  sprite.userData.kind = kind;
}

function disposeToolSprite(sprite: Sprite): void {
  const material = sprite.material;
  if (!Array.isArray(material)) {
    material.map = null;
    material.dispose();
  }
}

export function createToolSpriteLayer(): ToolSpriteLayer {
  return { sprites: new Map() };
}

export function disposeToolSprites(layer: ToolSpriteLayer, scene: Object3D | null): void {
  for (const sprite of layer.sprites.values()) {
    scene?.remove(sprite);
    disposeToolSprite(sprite);
  }
  layer.sprites.clear();
}

export function updateToolSprites(
  layer: ToolSpriteLayer,
  scene: Object3D,
  vrm: VRM,
  slots: readonly ToolIconSlot[],
  nowMs: number,
): void {
  const live = new Set(slots.map((slot) => slot.id));
  for (const [id, sprite] of layer.sprites) {
    if (live.has(id)) {
      continue;
    }
    scene.remove(sprite);
    disposeToolSprite(sprite);
    layer.sprites.delete(id);
  }
  for (const slot of slots) {
    let sprite = layer.sprites.get(slot.id);
    if (!sprite) {
      sprite = createToolIconSprite(slot.kind);
      layer.sprites.set(slot.id, sprite);
      scene.add(sprite);
    } else if (sprite.userData.kind !== slot.kind) {
      paintToolIcon(sprite, slot.kind);
    }
    placeToolIconSprite(vrm, slot, sprite, nowMs);
  }
}

export function toolLookObject(
  layer: ToolSpriteLayer,
  slots: readonly ToolIconSlot[],
  nowMs: number,
): Object3D | null {
  const slot = slots.find((item) => item.exitStartedMs === null);
  if (!slot || !toolGlanceActive(slot, nowMs)) {
    return null;
  }
  return layer.sprites.get(slot.id) ?? null;
}

function boneAt(vrm: VRM, name: VRMHumanBoneName): Object3D | null {
  return vrm.humanoid?.getNormalizedBoneNode(name) ?? null;
}

/** World position: just above the palm centre, following the hand bone every frame. */
export function placeToolIconSprite(
  vrm: VRM,
  slot: ToolIconSlot,
  sprite: Sprite,
  nowMs: number,
): void {
  const lateral = slot.side === "L" ? 1 : -1;
  const left = slot.side === "L";
  const hand = boneAt(vrm, left ? VRMHumanBoneName.LeftHand : VRMHumanBoneName.RightHand);
  if (hand) {
    hand.getWorldPosition(_anchor);
    const knuckle = boneAt(
      vrm,
      left ? VRMHumanBoneName.LeftMiddleProximal : VRMHumanBoneName.RightMiddleProximal,
    );
    if (knuckle) {
      knuckle.getWorldPosition(_knuckle);
      _anchor.lerp(_knuckle, PALM_CENTER_T);
    }
  } else {
    const chest = boneAt(vrm, VRMHumanBoneName.Chest);
    if (!chest) {
      return;
    }
    chest.getWorldPosition(_anchor);
    _anchor.x += lateral * 0.2;
    _anchor.y -= 0.12;
    _anchor.z += 0.3;
  }
  const elapsed = nowMs - slot.bornMs;
  const exitElapsed = slot.exitStartedMs === null ? null : nowMs - slot.exitStartedMs;
  const enter = toolIconEnter(elapsed);
  _anchor.y += PALM_LIFT_M + toolIconLift(elapsed, exitElapsed) + toolIconFloat(nowMs, enter);
  sprite.position.copy(_anchor);
  sprite.scale.setScalar(toolIconScale(elapsed, exitElapsed));
  const material = sprite.material;
  if (!Array.isArray(material)) {
    material.opacity = toolIconOpacity(elapsed, exitElapsed);
  }
  sprite.visible = material && !Array.isArray(material) ? material.opacity > 0.001 : true;
}
