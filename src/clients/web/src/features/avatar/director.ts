import type { CameraShot } from "./cameraShot";
import { copyDef, DOMINANT_SIDE, postureArmWeight } from "./arms";
import { CHARACTER_GESTURE_DURATION_MS, CHARACTER_GESTURE_INTENSITY } from "./applyGesture";
import { avatarMotionV2 } from "./avatarMotionFlag";
import {
  EXPLAIN_WEIGHT,
  HANDS_DURATION_MS,
  LISTENING_NOD_INTENSITY,
  LISTENING_NOD_MIN_MS,
  LISTENING_NOD_PERIOD_MS,
  SHRUG_DURATION_MS,
  THINK_DELAY_MS,
} from "./avatarMotion";
import {
  expressionGestureModifier,
  noteVisualState,
  publishedGestureFaceNow,
  type ExpressionGestureModifier,
  type ExpressionWeights,
} from "./expressionBlend";
import {
  EXPLAIN_POSE,
  HANDS_POSE,
  HOLD_POSE,
  THINK_HEAD,
  THINK_POSE,
  pointPose,
  DEFAULT_POINT_DIR,
} from "./gestureCatalog";
import { explainWeightForSpeech } from "./speechPulse";
import {
  cancelBelow,
  createGestureQueue,
  enqueue,
  releaseChannel,
  tick,
  type GestureQueue,
  type GestureRequest,
  type QueuedGesture,
} from "./gestureQueue";
import type { ToolIconGesture } from "./toolIcon";
import { GESTURE_SCALE } from "./avatarMotion";
import {
  advanceIdleBehavior,
  createIdleBehavior,
  type IdleBehaviorOutput,
  type IdleBehaviorState,
} from "./idleBehavior";
import { bell, IDLE_FIDGETS, motionGesture } from "./motionGestures";
import {
  SPEECH_GESTURE,
  USER_REACTION,
  type IntentCue,
  type SpeechIntent,
  type UserIntent,
} from "./intentCue";

/**
 * Bone owner (one writer per bone). This module records the table only;
 * it does not write `bone.rotation`.
 *
 * | Bone / channel       | Final owner | Intention from                           |
 * |----------------------|-------------|------------------------------------------|
 * | Hips, legs           | Posture     | (none on a bust)                         |
 * | Spine, Chest         | Posture     | breath, speech pulse, turn toward target |
 * | Neck, Head           | Head stage  | state, gestures, look-at, pulse, idle    |
 * | Shoulder L/R         | Arm solver  | automatic raise, shrug                   |
 * | UpperArm…fingers L/R | Arm solver  | rest, noise, gestures, point             |
 * | Expressions          | Face stage  | emotion, state, blink, visemes           |
 */

export type DirectorState = "idle" | "listening" | "thinking" | "speaking";

export type LayerId = "posture" | "idle" | "state" | "gesture" | "attention";

export interface MotionLayer {
  id: LayerId;
  /** Arm blend. Framing gain while motion v2 is on; otherwise 0. */
  armWeight: number;
}

export interface MotionPreview {
  /** `rest` clears the preview. Otherwise a catalog pose or a hub gesture name. */
  name: string;
  playId: string;
}

export interface DirectorCues {
  shot: CameraShot;
  /** Hub cue already delivered. Null when the protocol has none. */
  hubGesture: {
    name: string;
    intensity: number;
    durationMs: number;
    playId?: string;
  } | null;
  turnError: boolean;
  /** Avatar finished loading. `hands` plays once on the rising edge. */
  ready: boolean;
  /** Oldest live tool icon. Absent when nothing is showing. */
  toolGesture?: ToolIconGesture | null;
  /** Bank preview. Production leaves it unset. */
  motionPreview?: MotionPreview | null;
  /** User is composing a message: straighten and look at them. */
  typing?: boolean;
  /** Latest message intent (client heuristic). */
  intent?: IntentCue | null;
  /** Bench only: extra idle age so boredom can be previewed. */
  idleAgeMs?: number;
}

export interface Director {
  published: DirectorState;
  requested: DirectorState;
  requestedSinceMs: number;
  /** Request observed on the previous `directGestures` call. */
  seenRequested: DirectorState;
  queue: GestureQueue;
  thinkingSinceMs: number;
  listeningSinceMs: number;
  lastNodMs: number;
  handsPlayed: boolean;
  errorLatched: boolean;
  hubKey: string | null;
  /** Tool icon whose arm gesture is already requested. */
  toolArmId: number | null;
  /** Last bank preview applied. Null when the bank is not holding a pose. */
  previewKey: string | null;
  previewHeld: boolean;
  idle: IdleBehaviorState;
  /** Last idle-life output for the stage (posture weight, gaze, smile). */
  life: IdleBehaviorOutput;
  intentKey: string | null;
  /** Assistant gesture waiting for speech to start. */
  pendingSpeech: string | null;
  speechStartMs: number;
  closingPending: boolean;
  /** Listening lean after a user message, 0..1 envelope window. */
  leanStartMs: number;
  lastTapMs: number;
}

const STATE_HYSTERESIS_MS = 250;

const MOTION_LAYERS: readonly MotionLayer[] = [
  { id: "posture", armWeight: 0 },
  { id: "idle", armWeight: 0 },
  { id: "state", armWeight: 0 },
  { id: "gesture", armWeight: 0 },
  { id: "attention", armWeight: 0 },
];

const HUB_GESTURES = new Set(["nod", "shake", "wave", "shrug"]);

/** Head pitch from the active emotion. Yields the channel to nod, shake, and think. */
const MOOD_GESTURE = "mood";

export function createDirector(
  initial: DirectorState = "idle",
  nowMs = 0,
): Director {
  return {
    published: initial,
    requested: initial,
    requestedSinceMs: nowMs,
    seenRequested: initial,
    queue: createGestureQueue(nowMs),
    thinkingSinceMs: -1,
    listeningSinceMs: -1,
    lastNodMs: -1,
    handsPlayed: false,
    errorLatched: false,
    hubKey: null,
    toolArmId: null,
    previewKey: null,
    previewHeld: false,
    idle: createIdleBehavior(nowMs),
    life: {
      fidget: null,
      headCue: null,
      attention: false,
      weight: 1,
      straighten: 0,
      gaze: { yaw: 0, pitch: 0 },
      smile: 0,
      boredom: 0,
    },
    intentKey: null,
    pendingSpeech: null,
    speechStartMs: -1,
    closingPending: false,
    leanStartMs: -1,
    lastTapMs: -1,
  };
}

/** Listening lean: forward spine pitch after a user message (rad, ms). */
export const LISTEN_LEAN = { spine: 0.05, chest: 0.025, head: 0.04, ms: 2600 } as const;
/** Slow thinking: finger tapping after this long, then every `SLOW_THINK_GAP_MS`. */
export const SLOW_THINK_MS = 8_000;
export const SLOW_THINK_GAP_MS = 16_000;
/** Assistant speech gesture starts this long after speech begins. */
export const SPEECH_GESTURE_DELAY_MS = 700;

/** Names the idle director may interrupt when attention is needed. */
const IDLE_NAMES = new Set<string>([...IDLE_FIDGETS, "yawn", "sigh", "neckStretch"]);

/** Queue request for a procedural motion gesture (full reach: weight 1/GESTURE_SCALE). */
export function motionRequest(name: string, priority: 1 | 2 = 2, scale = 1): GestureRequest | null {
  const motion = motionGesture(name);
  if (!motion) {
    return null;
  }
  return {
    name,
    priority,
    channel: motion.channel,
    hands: motion.hands,
    oneshot: true,
    weight: motion.channel === "arm" ? scale / GESTURE_SCALE : 1,
    durationMs: motion.durationMs,
    pose: null,
    head: null,
  };
}

function releaseIdleMotion(director: Director, nowMs: number): void {
  for (const slot of [director.queue.arm, director.queue.head]) {
    if (slot && IDLE_NAMES.has(slot.name)) {
      releaseChannel(director.queue, slot.channel, slot.name, nowMs);
    }
  }
  director.queue.pending = director.queue.pending.filter((item) => !IDLE_NAMES.has(item.name));
}

function playMotion(director: Director, name: string | null, nowMs: number, scale = 1): void {
  if (!name) {
    return;
  }
  if (name === "shrug") {
    enqueue(director.queue, {
      name,
      priority: 2,
      channel: "arm",
      hands: "both",
      oneshot: true,
      weight: scale,
      durationMs: SHRUG_DURATION_MS,
      intensity: 1,
      pose: null,
      head: null,
      skipCooldown: true,
    }, nowMs);
    return;
  }
  const request = motionRequest(name, 2, scale);
  if (!request) {
    return;
  }
  releaseIdleMotion(director, nowMs);
  if (request.channel === "arm") {
    // Reactions answer a message: skip the post-gesture arm cooldown.
    director.queue.armCooldownUntilMs = Math.min(director.queue.armCooldownUntilMs, nowMs);
    director.queue.oneshotStartedAt[name] = undefined;
  }
  enqueue(director.queue, { ...request, skipCooldown: true }, nowMs);
}

function nodRequest(intensity: number): GestureRequest {
  return {
    name: "nod",
    priority: 2,
    channel: "head",
    hands: "none",
    oneshot: true,
    weight: 1,
    durationMs: CHARACTER_GESTURE_DURATION_MS,
    intensity,
    pose: null,
    head: null,
  };
}

/** Phase B: message intent → reaction. User reactions fire at once; speech ones wait for TTS. */
function directIntent(director: Director, nowMs: number, cue: IntentCue | null | undefined, scale: number): void {
  if (cue && cue.playId !== director.intentKey) {
    director.intentKey = cue.playId;
    if (cue.source === "user") {
      director.leanStartMs = nowMs;
      releaseMood(director, nowMs);
      enqueue(director.queue, nodRequest(LISTENING_NOD_INTENSITY), nowMs);
      director.lastNodMs = nowMs;
      playMotion(director, USER_REACTION[cue.kind as UserIntent] ?? null, nowMs, scale);
    } else {
      director.pendingSpeech = SPEECH_GESTURE[cue.kind as SpeechIntent] ?? null;
      director.closingPending = Boolean(cue.closing);
      director.speechStartMs = -1;
    }
  }

  const speaking = director.published === "speaking";
  if (speaking) {
    if (director.speechStartMs < 0) {
      director.speechStartMs = nowMs;
    }
    if (director.pendingSpeech && nowMs - director.speechStartMs >= SPEECH_GESTURE_DELAY_MS) {
      playMotion(director, director.pendingSpeech, nowMs, scale);
      director.pendingSpeech = null;
    }
  } else if (director.speechStartMs >= 0) {
    director.speechStartMs = -1;
    director.pendingSpeech = null;
    if (director.closingPending) {
      director.closingPending = false;
      enqueue(director.queue, nodRequest(CHARACTER_GESTURE_INTENSITY), nowMs);
      playMotion(director, "openHand", nowMs, scale);
    }
  }
}

/** Spine lean while a fresh user message is being listened to. */
export function listenLeanAt(director: Director, nowMs: number): number {
  if (director.leanStartMs < 0) {
    return 0;
  }
  const t = (nowMs - director.leanStartMs) / LISTEN_LEAN.ms;
  if (t <= 0 || t >= 1) {
    return 0;
  }
  return bell(t, 0.3);
}

function directSlowThinking(director: Director, nowMs: number, state: DirectorState, scale: number): void {
  if (state !== "thinking" || director.thinkingSinceMs < 0) {
    return;
  }
  const elapsed = nowMs - director.thinkingSinceMs;
  if (elapsed < SLOW_THINK_MS) {
    return;
  }
  if (director.lastTapMs >= 0 && nowMs - director.lastTapMs < SLOW_THINK_GAP_MS) {
    return;
  }
  director.lastTapMs = nowMs;
  const arm = director.queue.arm;
  if (arm && arm.name === "think") {
    releaseChannel(director.queue, "arm", "think", nowMs);
  }
  const request = motionRequest("tapFingers", 1, scale);
  if (request) {
    director.queue.armCooldownUntilMs = Math.min(director.queue.armCooldownUntilMs, nowMs);
    enqueue(director.queue, request, nowMs);
  }
}

function directIdleLife(director: Director, nowMs: number, cues: DirectorCues, state: DirectorState, scale: number): void {
  const queue = director.queue;
  const idle = state === "idle" && director.requested === "idle" && !director.previewHeld;
  const life = advanceIdleBehavior(director.idle, nowMs, {
    idle,
    typing: Boolean(cues.typing) && state !== "speaking",
    armFree: queue.arm === null && !queue.pending.some((item) => item.channel === "arm"),
    headFree: queue.head === null || queue.head.name === MOOD_GESTURE,
    extraIdleMs: cues.idleAgeMs,
  });
  director.life = life;
  if (life.attention) {
    releaseIdleMotion(director, nowMs);
  }
  if (life.fidget) {
    const request = motionRequest(life.fidget, 1, scale);
    if (request) {
      enqueue(queue, request, nowMs);
    }
  }
  if (life.headCue) {
    const request = motionRequest(life.headCue, 1);
    if (request) {
      releaseMood(director, nowMs);
      enqueue(queue, request, nowMs);
    }
  }
}

/** Remember the hub state. Repeating the same request does not reset the hold. */
export function requestState(
  director: Director,
  state: DirectorState,
  nowMs: number,
): void {
  if (state === director.requested) {
    return;
  }
  director.requested = state;
  director.requestedSinceMs = nowMs;
}

/** Published state. A new request is applied after it has been held for 250 ms. */
export function activeState(director: Director, nowMs: number): DirectorState {
  if (director.requested !== director.published) {
    if (nowMs - director.requestedSinceMs >= STATE_HYSTERESIS_MS) {
      director.published = director.requested;
    }
  }
  return director.published;
}

/**
 * Fixed layer order.
 * The posture layer carries the rest-arm weight only while motion v2 is on.
 */
export function activeLayers(
  director: Director,
  nowMs: number,
  shot: CameraShot = "bust",
): readonly MotionLayer[] {
  activeState(director, nowMs);
  MOTION_LAYERS[0].armWeight = postureArmWeight(avatarMotionV2, shot);
  return MOTION_LAYERS;
}

function armBlocked(queue: GestureQueue): boolean {
  if (queue.arm && queue.arm.priority >= 3 && queue.arm.phase !== "release") {
    return true;
  }
  return queue.pending.some((item) => item.channel === "arm" && item.priority >= 3);
}

function explainRequest(speakW: number | undefined, scale: number): GestureRequest {
  const base = speakW === undefined ? EXPLAIN_WEIGHT : explainWeightForSpeech(speakW);
  return {
    name: "explain",
    priority: 1,
    channel: "arm",
    hands: "both",
    oneshot: false,
    weight: base * scale,
    durationMs: 0,
    pose: EXPLAIN_POSE,
    head: null,
  };
}

function syncExplainWeight(director: Director, speakW: number, scale: number): void {
  const weight = explainWeightForSpeech(speakW) * scale;
  const arm = director.queue.arm;
  if (arm && arm.name === "explain" && arm.phase !== "release") {
    arm.weight = weight;
  }
  for (const item of director.queue.pending) {
    if (item.channel === "arm" && item.name === "explain") {
      item.weight = weight;
    }
  }
}

function thinkArmRequest(scale: number): GestureRequest {
  return {
    name: "think",
    priority: 1,
    channel: "arm",
    hands: DOMINANT_SIDE,
    oneshot: false,
    weight: scale,
    durationMs: 0,
    pose: THINK_POSE,
    head: { ...THINK_HEAD },
  };
}

function thinkHeadRequest(pitch: number): GestureRequest {
  return {
    name: "think",
    priority: 1,
    channel: "head",
    hands: "none",
    oneshot: false,
    weight: 1,
    durationMs: 0,
    pose: null,
    head: {
      pitch: THINK_HEAD.pitch + pitch,
      yaw: THINK_HEAD.yaw,
      roll: THINK_HEAD.roll,
    },
  };
}

/**
 * Asks the queue for the gesture that matches the published state.
 * With the motion flag off this is a no-op: `applyGesture` stays the only path.
 */
export function directGestures(
  director: Director,
  nowMs: number,
  cues: DirectorCues,
  motionV2: boolean = avatarMotionV2,
  speakW?: number,
  face?: ExpressionWeights,
): void {
  if (!motionV2) {
    return;
  }

  noteVisualState(director.requested);
  const body = expressionGestureModifier(face ?? publishedGestureFaceNow());
  const state = activeState(director, nowMs);
  if (
    director.seenRequested === "speaking" &&
    director.requested === "listening"
  ) {
    cancelBelow(director.queue, 3, nowMs);
  }
  if (!cues.motionPreview && director.previewHeld) {
    releasePreviewArm(director, nowMs);
    director.previewHeld = false;
    director.previewKey = null;
  }
  if (!cues.motionPreview && director.requested !== "speaking") {
    releaseChannel(director.queue, "arm", "explain", nowMs);
    releaseChannel(director.queue, "arm", "explainR", nowMs);
  }

  if (director.requested === "thinking") {
    if (director.thinkingSinceMs < 0) {
      director.thinkingSinceMs = director.requestedSinceMs;
    }
  } else {
    director.thinkingSinceMs = -1;
    if (!cues.motionPreview) {
      releaseChannel(director.queue, "arm", "think", nowMs);
      releaseChannel(director.queue, "head", "think", nowMs);
    }
  }

  if (cues.turnError && !director.errorLatched) {
    director.errorLatched = true;
    enqueue(director.queue, {
      name: "shrug",
      priority: 3,
      channel: "arm",
      hands: "both",
      oneshot: true,
      weight: body.scale,
      durationMs: SHRUG_DURATION_MS,
      intensity: 1,
      pose: null,
      head: null,
    }, nowMs);
  } else if (!cues.turnError) {
    director.errorLatched = false;
  }

  if (cues.ready && !director.handsPlayed && !body.omitHands && !cues.motionPreview) {
    director.handsPlayed = true;
    enqueue(director.queue, {
      name: "hands",
      priority: 2,
      channel: "arm",
      hands: "both",
      oneshot: true,
      weight: body.scale,
      durationMs: HANDS_DURATION_MS,
      pose: HANDS_POSE,
      head: null,
    }, nowMs);
  }

  const hub = cues.hubGesture;
  const hubKey = hub
    ? `${hub.playId ?? ""}:${hub.name}:${hub.intensity}:${hub.durationMs}`
    : null;
  if (hub && hubKey && hubKey !== director.hubKey && HUB_GESTURES.has(hub.name)) {
    director.hubKey = hubKey;
    const headCue = hub.name === "nod" || hub.name === "shake";
    const omitWave = hub.name === "wave" && body.omitWave;
    if (!omitWave) {
      if (headCue) {
        releaseMood(director, nowMs);
      }
      enqueue(director.queue, {
        name: hub.name,
        priority: 2,
        channel: headCue ? "head" : "arm",
        hands: hub.name === "wave" ? "R" : headCue ? "none" : "both",
        oneshot: true,
        weight: headCue ? 1 : body.scale,
        durationMs: hub.durationMs,
        intensity: hub.intensity,
        pose: null,
        head: null,
      }, nowMs);
    }
  } else if (!hub) {
    director.hubKey = null;
  }

  if (
    director.requested === "speaking" &&
    state === "speaking" &&
    !armBlocked(director.queue)
  ) {
    enqueue(director.queue, explainRequest(speakW, body.scale), nowMs);
    if (speakW !== undefined) {
      syncExplainWeight(director, speakW, body.scale);
    }
  }

  if (
    state === "thinking" &&
    director.thinkingSinceMs >= 0 &&
    nowMs - director.thinkingSinceMs >= THINK_DELAY_MS
  ) {
    if (cues.shot === "bust") {
      releaseChannel(director.queue, "arm", "think", nowMs);
      releaseMood(director, nowMs);
      enqueue(director.queue, thinkHeadRequest(body.pitch), nowMs);
    } else {
      releaseChannel(director.queue, "head", "think", nowMs);
      enqueue(director.queue, thinkArmRequest(body.scale), nowMs);
    }
  }

  if (state === "listening") {
    if (director.listeningSinceMs < 0) {
      director.listeningSinceMs = director.requestedSinceMs;
    }
    const head = director.queue.head;
    const headBusy =
      head !== null && head.name !== MOOD_GESTURE && head.phase !== "release";
    const sinceNod = director.lastNodMs < 0
      ? nowMs - director.listeningSinceMs
      : nowMs - director.lastNodMs;
    if (!headBusy && sinceNod >= LISTENING_NOD_PERIOD_MS && sinceNod >= LISTENING_NOD_MIN_MS) {
      releaseMood(director, nowMs);
      enqueue(director.queue, {
        name: "nod",
        priority: 1,
        channel: "head",
        hands: "none",
        oneshot: true,
        weight: 1,
        durationMs: CHARACTER_GESTURE_DURATION_MS,
        intensity: LISTENING_NOD_INTENSITY,
        pose: null,
        head: null,
      }, nowMs);
      director.lastNodMs = nowMs;
    }
  } else {
    director.listeningSinceMs = -1;
  }

  directToolGesture(director, nowMs, cues.toolGesture, body.scale);
  directIntent(director, nowMs, cues.intent, body.scale);
  directSlowThinking(director, nowMs, state, body.scale);
  directIdleLife(director, nowMs, cues, state, body.scale);
  applyMotionPreview(director, cues.motionPreview, body.scale, nowMs);
  director.seenRequested = director.requested;
  applyMoodPitch(director, body, nowMs);
  tick(director.queue, nowMs);
}

function toolArmRequest(cue: ToolIconGesture, scale: number): GestureRequest {
  const aimed = cue.gesture === "point" ? pointPose(cue.dir) : null;
  return {
    name: cue.gesture,
    priority: 1,
    channel: "arm",
    hands: cue.side,
    oneshot: false,
    // Full authored reach (cancels GESTURE_SCALE), same as motion previews.
    weight: scale / GESTURE_SCALE,
    durationMs: 0,
    pose: aimed ? aimed.pose : HOLD_POSE,
    head: null,
  };
}

function retargetArm(arm: QueuedGesture, request: GestureRequest, nowMs: number): void {
  arm.name = request.name;
  arm.hands = request.hands;
  arm.oneshot = false;
  arm.weight = request.weight;
  arm.durationMs = 0;
  arm.pose = request.pose ? copyDef(request.pose) : null;
  arm.head = null;
  arm.phase = "attack";
  arm.envelope = 0;
  arm.startedAtMs = nowMs;
  arm.releaseAtMs = 0;
  arm.releaseFrom = 0;
}

function dropPendingToolArms(director: Director): void {
  director.queue.pending = director.queue.pending.filter((item) => {
    if (item.channel !== "arm") {
      return true;
    }
    return item.name !== "think" && item.name !== "point" && item.name !== "hold";
  });
}

function releasePreviewArm(director: Director, nowMs: number): void {
  const arm = director.queue.arm;
  if (arm && arm.priority <= 2) {
    releaseChannel(director.queue, "arm", arm.name, nowMs);
  }
  const head = director.queue.head;
  if (head && head.priority <= 2 && head.name !== "mood") {
    releaseChannel(director.queue, "head", head.name, nowMs);
  }
}

function previewRequest(name: string, scale: number): GestureRequest | null {
  // Full authored reach in the bank preview (weight cancels GESTURE_SCALE), same as motions.
  const full = scale / GESTURE_SCALE;
  const arm = {
    priority: 1 as const,
    channel: "arm" as const,
    oneshot: false,
    weight: full,
    durationMs: 0,
    head: null,
  };
  switch (name) {
    case "explain":
      return { ...arm, name, hands: "both", pose: EXPLAIN_POSE };
    case "think":
      return { ...arm, name, hands: DOMINANT_SIDE, pose: THINK_POSE, head: { ...THINK_HEAD } };
    case "hold":
      return { ...arm, name, hands: DOMINANT_SIDE, pose: HOLD_POSE };
    case "hands":
      return { ...arm, name, hands: "both", pose: HANDS_POSE };
    case "point":
      return {
        ...arm,
        name,
        hands: DOMINANT_SIDE,
        pose: pointPose(DEFAULT_POINT_DIR).pose,
      };
    case "nod":
    case "shake":
      return {
        name,
        priority: 2,
        channel: "head",
        hands: "none",
        oneshot: true,
        weight: 1,
        durationMs: CHARACTER_GESTURE_DURATION_MS,
        intensity: CHARACTER_GESTURE_INTENSITY,
        pose: null,
        head: null,
      };
    case "wave":
      return {
        name,
        priority: 2,
        channel: "arm",
        hands: "R",
        oneshot: true,
        weight: scale,
        durationMs: CHARACTER_GESTURE_DURATION_MS,
        intensity: CHARACTER_GESTURE_INTENSITY,
        pose: null,
        head: null,
      };
    case "shrug":
      return {
        name,
        priority: 2,
        channel: "arm",
        hands: "both",
        oneshot: true,
        weight: scale,
        durationMs: SHRUG_DURATION_MS,
        intensity: 1,
        pose: null,
        head: null,
      };
    default:
      return motionRequest(name, 2, scale);
  }
}

function playPreview(director: Director, request: GestureRequest, nowMs: number): void {
  if (request.channel === "head") {
    const arm = director.queue.arm;
    if (arm && arm.priority <= 2) {
      releaseChannel(director.queue, "arm", arm.name, nowMs);
    }
  } else {
    const head = director.queue.head;
    if (head && head.priority <= 2 && head.name !== "mood") {
      releaseChannel(director.queue, "head", head.name, nowMs);
    }
  }

  const slot = request.channel === "arm" ? director.queue.arm : director.queue.head;
  if (request.channel === "arm" && slot && slot.phase !== "pending") {
    retargetSlot(slot, request, nowMs);
    return;
  }
  if (request.channel === "head" && slot && slot.phase !== "pending") {
    retargetSlot(slot, request, nowMs);
    return;
  }
  if (request.channel === "arm") {
    director.queue.armCooldownUntilMs = nowMs;
  }
  enqueue(director.queue, request, nowMs);
}

function retargetSlot(slot: QueuedGesture, request: GestureRequest, nowMs: number): void {
  slot.name = request.name;
  slot.priority = request.priority;
  slot.channel = request.channel;
  slot.hands = request.hands;
  slot.oneshot = request.oneshot;
  slot.weight = request.weight;
  slot.durationMs = request.durationMs;
  slot.intensity = request.intensity ?? 1;
  slot.pose = request.pose ? copyDef(request.pose) : null;
  slot.head = request.head ? { ...request.head } : null;
  slot.phase = "attack";
  slot.envelope = 0;
  slot.startedAtMs = nowMs;
  slot.releaseAtMs = 0;
  slot.releaseFrom = 0;
}

function applyMotionPreview(
  director: Director,
  preview: MotionPreview | null | undefined,
  scale: number,
  nowMs: number,
): void {
  if (!preview || preview.playId === director.previewKey) {
    return;
  }
  director.previewKey = preview.playId;
  if (preview.name === "rest") {
    releasePreviewArm(director, nowMs);
    director.previewHeld = false;
    return;
  }
  const request = previewRequest(preview.name, scale);
  if (!request) {
    director.previewHeld = false;
    return;
  }
  playPreview(director, request, nowMs);
  director.previewHeld = !request.oneshot;
}

/**
 * P1, under an explicit P2 cue. The arm starts `LOOK_LEAD_MS` after the icon
 * appears. Replacing `think` avoids the arm cooldown, which would hide the icon pose.
 */
function directToolGesture(
  director: Director,
  nowMs: number,
  cue: ToolIconGesture | null | undefined,
  scale: number,
): void {
  if (!cue) {
    if (director.toolArmId === null) {
      return;
    }
    releaseChannel(director.queue, "arm", "point", nowMs);
    releaseChannel(director.queue, "arm", "hold", nowMs);
    director.toolArmId = null;
    return;
  }
  if (nowMs < cue.armAtMs) {
    return;
  }

  const request = toolArmRequest(cue, scale);
  const arm = director.queue.arm;
  const same =
    director.toolArmId === cue.id &&
    arm !== null &&
    arm.name === cue.gesture &&
    arm.phase !== "release";
  if (same) {
    return;
  }

  const replaceThink =
    arm !== null && arm.name === "think" && arm.priority === 1 && arm.phase !== "release";
  const replaceTool =
    director.toolArmId !== null &&
    director.toolArmId !== cue.id &&
    arm !== null &&
    (arm.name === "point" || arm.name === "hold") &&
    arm.phase !== "release";
  if (arm && (replaceThink || replaceTool)) {
    retargetArm(arm, request, nowMs);
    dropPendingToolArms(director);
    director.toolArmId = cue.id;
    return;
  }

  enqueue(director.queue, request, nowMs);
  director.toolArmId = cue.id;
}

function releaseMood(director: Director, nowMs: number): void {
  releaseChannel(director.queue, "head", MOOD_GESTURE, nowMs);
}

function applyMoodPitch(
  director: Director,
  body: ExpressionGestureModifier,
  nowMs: number,
): void {
  const head = director.queue.head;
  if (head?.name === "think" && head.head && head.phase !== "release") {
    head.head.pitch = THINK_HEAD.pitch + body.pitch;
  }

  const pitch = body.pitch;
  if (Math.abs(pitch) < 1e-6) {
    releaseMood(director, nowMs);
    return;
  }

  const pendingHead = director.queue.pending.some(
    (item) => item.channel === "head" && item.name !== MOOD_GESTURE,
  );
  if (pendingHead) {
    return;
  }
  if (head && head.name !== MOOD_GESTURE && head.phase !== "release") {
    return;
  }
  if (head?.name === MOOD_GESTURE) {
    if (head.phase !== "release" && head.head) {
      head.head.pitch = pitch;
    }
    return;
  }

  enqueue(director.queue, {
    name: MOOD_GESTURE,
    priority: 0,
    channel: "head",
    hands: "none",
    oneshot: false,
    weight: 1,
    durationMs: 0,
    pose: null,
    head: { pitch, yaw: 0, roll: 0 },
  }, nowMs);
}
