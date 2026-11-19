import {
  addPostureOffset,
  createPostureState,
  undoPosture,
  updatePosture,
  type PostureState,
} from "./elegantPosture";
import type { IntentCue } from "./intentCue";
import { motionSmile } from "./gestureQueue";
import { useEffect, useRef, useState } from "react";
import type { VRM } from "@pixiv/three-vrm";
import { VRMExpressionPresetName, VRMUtils } from "@pixiv/three-vrm";
import type { VRMLipSync } from "three-vrm-lip-sync";
import { Clock, type Camera } from "three";
import { getSharedAudioContext, resumeSharedAudioContext } from "../../lib/audioContext";
import { getPlaybackAnalysisStream } from "../../lib/playbackTap";
import {
  beginExpressionCue,
  createExpressionMotionState,
  updateExpressionMotion,
  type AvatarExpressionCue,
  type ExpressionMotionState,
} from "./applyExpression";
import {
  beginGestureCue,
  createGestureMotionState,
  isGestureMotionActive,
  updateGestureMotion,
  type AvatarGestureCue,
  type GestureMotionState,
} from "./applyGesture";
import {
  beginClipGesture,
  createClipMotionState,
  disposeClipMotion,
  isClipPlaying,
  loadGestureClips,
  updateClipMotion,
  type ClipMotionState,
} from "./applyGestureClip";
import {
  applyCameraFraming,
  createCameraShotMotion,
  DEFAULT_CAMERA_SHOT,
  framingFromVrm,
  retargetCameraShot,
  updateCameraShotMotion,
  type CameraShot,
  type CameraShotMotion,
} from "./cameraShot";
import { applyRestArms, measureArmRig } from "./arms";
import { clampDelta } from "./clampDelta";
import { applyQueuedArms, resetAppliedArms, type GestureQueue } from "./gestureQueue";
import { avatarMotionV2 } from "./avatarMotionFlag";
import { createAvatarScene, type AvatarScene } from "./createAvatarScene";
import {
  activeLayers,
  activeState,
  createDirector,
  directGestures,
  LISTEN_LEAN,
  listenLeanAt,
  requestState,
  type Director,
  type MotionPreview,
} from "./director";
import { applySceneBackdrop, type BackdropAlignment } from "./sceneBackdrop";
import {
  createIdleLifeState,
  updateIdleLife,
  type IdleLifeState,
} from "./idleLife";
import {
  createIdleMotionState,
  updateIdleMotion,
  type BlinkV2Options,
  type IdleMotionState,
} from "./idleMotion";
import {
  createLookAtState,
  setLookTarget,
  updateLookAt,
  type LookAtState,
} from "./lookAt";
import {
  advanceSpeechEnergy,
  applySpeechPulse,
  applyVisemeRelease,
  createSpeechEnergyState,
  speechPulseAt,
  VISEME_RELEASE_MS,
  type SpeechEnergyState,
} from "./speechPulse";
import { createAvatarLipSync, readLipSyncVolume } from "./lipSync";
import { disposeVrm, loadVrm, placeAndFrameVrm } from "./loadVrm";
import {
  createPoseMotionState,
  setPoseTarget,
  updatePoseMotion,
  type PoseMotionState,
} from "./poseForState";
import { resolveGesturePlayback } from "./resolveGesturePlayback";
import {
  createToolSpriteLayer,
  disposeToolSprites,
  primaryToolGesture,
  shouldPlayToolChime,
  syncToolIcons,
  takeToolChimes,
  toolLookObject,
  updateToolSprites,
  playToolChime,
  type ActiveTool,
  type ToolChimeDirection,
  type ToolIconGesture,
  type ToolIconSlot,
  type ToolSpriteLayer,
} from "./toolIcon";
import {
  createRestPoseState,
  updateRestPose,
  type RestPoseState,
} from "./restPose";

export type AvatarStageStatus = "loading" | "ready" | "error";

export type AvatarVisualState = "idle" | "listening" | "thinking" | "speaking";

export interface AvatarStageProps {
  modelUrl: string | null;
  /** True while TTS playback is active (voiceStatus === "playing"). */
  speaking?: boolean;
  /** Resolved presentation state for pose (G4). */
  visualState?: AvatarVisualState;
  /** Latest emotion cue (already resolved for character hold). */
  expression?: AvatarExpressionCue | null;
  /** Latest gesture cue from the protocol; null clears motion. */
  gesture?: AvatarGestureCue | null;
  /** Optional wrapper class; presentation modes live outside features/avatar. */
  className?: string;
  /** Same-origin 2D backdrop URL; null keeps the studio gradient. */
  backgroundUrl?: string | null;
  /** Floor / standing spot in the photo; presentation lives outside this module. */
  backgroundAlign?: BackdropAlignment;
  /** Discrete camera framing. Presentation modes live outside this module. */
  shot?: CameraShot;
  /** Turn failed. The director enqueues `shrug` only while motion v2 is on. */
  turnError?: boolean;
  /** Tools in flight. Sprites exist only while motion v2 is on. */
  activeTools?: readonly ActiveTool[];
  /** Bank preview. Production leaves it unset. */
  motionPreview?: MotionPreview | null;
  /** User is typing in the composer: she straightens and looks at them. */
  userTyping?: boolean;
  /** Latest message intent (client heuristic, see chat/messageIntent). */
  intent?: IntentCue | null;
  /** Bench only: extra idle age to preview boredom. */
  devIdleAgeMs?: number;
}

function gestureKey(gesture: AvatarGestureCue): string {
  return `${gesture.playId ?? ""}:${gesture.name}:${gesture.intensity}:${gesture.durationMs}`;
}

function playStageToolChime(
  preferred: AudioContext | null,
  direction: ToolChimeDirection,
): void {
  try {
    const ctx = preferred ?? getSharedAudioContext();
    if (ctx.state === "suspended") {
      void ctx.resume().then(() => playToolChime(ctx, direction)).catch(() => undefined);
      return;
    }
    playToolChime(ctx, direction);
  } catch {
    /* The context can stay blocked until a user gesture. */
  }
}

const DEFAULT_WRAPPER_CLASS =
  "relative h-full min-h-44 w-full overflow-hidden rounded-xl bg-[var(--ink)]";

/** Spring settle before the canvas fades in. 75 steps of 1/60 s is 1.25 s. */
const LOAD_PRE_ROLL_STEPS = 75;
/** Short settle when the tab becomes visible again. The canvas stays shown. */
const RETURN_PRE_ROLL_STEPS = 30;
const SPRING_STEP_SEC = 1 / 60;
const CANVAS_FADE_MS = 200;

function settleSpringBones(vrm: VRM, steps: number): void {
  for (let step = 0; step < steps; step += 1) {
    vrm.update(SPRING_STEP_SEC);
  }
}

interface VeraInspector {
  published: string;
  requested: string;
  arm: string | null;
  phase: string | null;
}

interface LegacyFrame {
  vrm: VRM;
  idleState: IdleMotionState;
  delta: number;
  now: number;
  clipState: ClipMotionState;
  expressionState: ExpressionMotionState;
  gestureState: GestureMotionState;
  poseState: PoseMotionState;
  restPoseState: RestPoseState;
  postureState: PostureState;
  idleLife: IdleLifeState;
  visualState: AvatarVisualState;
  shot: CameraShot;
  camera: Camera;
  lipSync: VRMLipSync | null;
  hubGesture: AvatarGestureCue | null;
  turnError: boolean;
  ready: boolean;
  toolGesture: ToolIconGesture | null;
  motionPreview?: MotionPreview | null;
  typing?: boolean;
  intent?: IntentCue | null;
  idleAgeMs?: number;
  /** Speech chest for the breath owner. Absent on the legacy path. */
  speechChest?: number;
  blinkV2?: BlinkV2Options;
  beforeLipSync?: () => void;
  afterLipSync?: () => void;
}

/** Today's loop, moved unchanged when `solveArms` is false. Same call order, same arguments. */
function frameLegacy(frame: LegacyFrame, solveArms = false, queue?: GestureQueue): void {
  const currentVrm = frame.vrm;
  const idleState = frame.idleState;
  const delta = frame.delta;
  const now = frame.now;
  const clipActive = isClipPlaying(frame.clipState);
  undoPosture(frame.postureState);
  updateIdleMotion(
    currentVrm,
    idleState,
    { nowMs: () => now },
    {
      omitBreath: clipActive,
      speechChest: frame.speechChest,
      blinkV2: frame.blinkV2,
    },
  );
  updateExpressionMotion(currentVrm, frame.expressionState, now);
  const gestureInFlight =
    clipActive || isGestureMotionActive(frame.gestureState, now);
  if (!gestureInFlight) {
    setPoseTarget(frame.poseState, frame.visualState);
  }
  updatePoseMotion(currentVrm, frame.poseState, delta);
  if (clipActive) {
    updateClipMotion(frame.clipState, delta);
  } else if (solveArms && queue) {
    applyQueuedArms(currentVrm, queue, now, frame.shot);
    updateIdleLife(currentVrm, frame.idleLife, now, {
      visualState: frame.visualState,
      shot: frame.shot,
      camera: frame.camera,
      suppressed: gestureInFlight || frame.visualState === "speaking",
    });
  } else if (solveArms) {
    applyRestArms(currentVrm, now / 1000, frame.shot);
    updateIdleLife(currentVrm, frame.idleLife, now, {
      visualState: frame.visualState,
      shot: frame.shot,
      camera: frame.camera,
      suppressed: gestureInFlight || frame.visualState === "speaking",
    });
    updateGestureMotion(currentVrm, frame.gestureState, now);
  } else {
    updateRestPose(currentVrm, frame.restPoseState);
    updateIdleLife(currentVrm, frame.idleLife, now, {
      visualState: frame.visualState,
      shot: frame.shot,
      camera: frame.camera,
      suppressed: gestureInFlight || frame.visualState === "speaking",
    });
    updateGestureMotion(currentVrm, frame.gestureState, now);
  }
  frame.beforeLipSync?.();
  updatePosture(currentVrm, frame.postureState, now);
  frame.lipSync?.update();
  frame.afterLipSync?.();
  currentVrm.update(delta);
}

/**
 * Director path. With motion v2 the posture layer owns the arms (`restDef`).
 * `idleLife` still runs for the head and does not write arm bones.
 * Head still uses poseForState; expressions stay on applyExpression.
 */
function happyWeight(vrm: VRM): number {
  const value = vrm.expressionManager?.getValue(VRMExpressionPresetName.Happy);
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return 0;
  }
  if (value < 0) {
    return 0;
  }
  if (value > 1) {
    return 1;
  }
  return value;
}

/** Idle director output → posture (weight shift, straighten, listening lean). */
function applyIdleLife(frame: LegacyFrame, director: Director): void {
  frame.postureState.weight = director.life.weight;
  frame.postureState.straighten = director.life.straighten;
  const lean = listenLeanAt(director, frame.now);
  if (lean > 0) {
    addPostureOffset("spine", LISTEN_LEAN.spine * lean, 0, 0);
    addPostureOffset("chest", LISTEN_LEAN.chest * lean, 0, 0);
    addPostureOffset("head", LISTEN_LEAN.head * lean, 0, 0);
  }
}

/** Adds a smile on top of the face blend without lowering it. */
function addSmile(vrm: VRM, amount: number): void {
  const manager = vrm.expressionManager;
  if (!manager || amount <= 0.001) {
    return;
  }
  const current = manager.getValue(VRMExpressionPresetName.Happy) ?? 0;
  manager.setValue(VRMExpressionPresetName.Happy, Math.max(current, Math.min(0.7, amount)));
}

function frameV2(
  frame: LegacyFrame,
  director: Director,
  look: LookAtState,
  speaking: boolean,
  visemeReleaseStartMs: number | null,
  speechEnergy: SpeechEnergyState,
): void {
  const before = director.published;
  requestState(director, frame.visualState, frame.now);
  const volume = readLipSyncVolume(frame.lipSync);
  const energy = advanceSpeechEnergy(speechEnergy, volume, frame.now);
  directGestures(
    director,
    frame.now,
    {
      shot: frame.shot,
      hubGesture: frame.hubGesture,
      turnError: frame.turnError,
      ready: frame.ready,
      toolGesture: frame.toolGesture,
      motionPreview: frame.motionPreview,
      typing: frame.typing,
      intent: frame.intent,
      idleAgeMs: frame.idleAgeMs,
    },
    true,
    energy,
  );
  applyIdleLife(frame, director);
  const published = activeState(director, frame.now);
  const pulse = speechPulseAt(energy, energy, frame.now / 1000);
  const layers = activeLayers(director, frame.now, frame.shot);
  const solveArms = (layers[0]?.armWeight ?? 0) > 0;
  frameLegacy(
    {
      ...frame,
      visualState: published,
      speechChest: pulse.chest,
      blinkV2: {
        stateChanged: published !== before,
        happy: happyWeight(frame.vrm),
        listening: published === "listening",
      },
      beforeLipSync: () => {
        applySpeechPulse(frame.vrm, pulse);
        look.gazeOffset = published === "idle" ? director.life.gaze : null;
        updateLookAt(frame.vrm, look, published, frame.camera, frame.now);
        addSmile(frame.vrm, Math.max(director.life.smile, motionSmile()));
      },
      afterLipSync: () => {
        const lipSync = frame.lipSync;
        if (!lipSync || visemeReleaseStartMs === null || speaking) {
          return;
        }
        const elapsed = frame.now - visemeReleaseStartMs;
        applyVisemeRelease(frame.vrm, lipSync.engine.weights, elapsed);
      },
    },
    solveArms,
    director.queue,
  );
}

export function AvatarStage({
  modelUrl,
  speaking = false,
  visualState = "idle",
  expression = null,
  gesture = null,
  className = DEFAULT_WRAPPER_CLASS,
  backgroundUrl = null,
  backgroundAlign,
  shot = DEFAULT_CAMERA_SHOT,
  turnError = false,
  activeTools = [],
  motionPreview = null,
  userTyping = false,
  intent = null,
  devIdleAgeMs = 0,
}: AvatarStageProps) {
  const typingRef = useRef(userTyping);
  typingRef.current = userTyping;
  const intentRef = useRef(intent);
  intentRef.current = intent;
  const idleAgeRef = useRef(devIdleAgeMs);
  idleAgeRef.current = devIdleAgeMs;
  const containerRef = useRef<HTMLDivElement>(null);
  const sceneHandleRef = useRef<AvatarScene | null>(null);
  const [sceneEpoch, setSceneEpoch] = useState(0);
  const [status, setStatus] = useState<AvatarStageStatus>("loading");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const lipSyncRef = useRef<VRMLipSync | null>(null);
  const vrmRef = useRef<VRM | null>(null);
  const speakingRef = useRef(speaking);
  speakingRef.current = speaking;
  const visualStateRef = useRef(visualState);
  visualStateRef.current = visualState;
  const expressionRef = useRef(expression);
  expressionRef.current = expression;
  const gestureRef = useRef(gesture);
  gestureRef.current = gesture;
  const expressionStateRef = useRef<ExpressionMotionState>(createExpressionMotionState());
  const gestureStateRef = useRef<GestureMotionState>(createGestureMotionState());
  const clipStateRef = useRef<ClipMotionState>(createClipMotionState());
  const loadedClipsRef = useRef<ReadonlySet<string>>(new Set());
  const poseStateRef = useRef<PoseMotionState>(createPoseMotionState());
  const restPoseStateRef = useRef<RestPoseState>(createRestPoseState());
  const postureStateRef = useRef<PostureState>(createPostureState());
  const idleLifeRef = useRef<IdleLifeState>(createIdleLifeState());
  const directorRef = useRef<Director>(createDirector(visualState));
  const lookAtRef = useRef<LookAtState>(createLookAtState());
  const speechEnergyRef = useRef<SpeechEnergyState>(createSpeechEnergyState());
  const visemeReleaseStartRef = useRef<number | null>(null);
  const visemeWasSpeakingRef = useRef(false);
  const lastExpressionKeyRef = useRef<string | null>(null);
  const lastGestureKeyRef = useRef<string | null>(null);
  const shotRef = useRef(shot);
  shotRef.current = shot;
  const turnErrorRef = useRef(turnError);
  turnErrorRef.current = turnError;
  const motionPreviewRef = useRef(motionPreview);
  motionPreviewRef.current = motionPreview;
  const activeToolsRef = useRef(activeTools);
  activeToolsRef.current = activeTools;
  const toolSlotsRef = useRef<ToolIconSlot[]>([]);
  const toolLayerRef = useRef<ToolSpriteLayer>(createToolSpriteLayer());
  const readyRef = useRef(false);
  const cameraMotionRef = useRef<CameraShotMotion | null>(null);
  const appliedShotRef = useRef<CameraShot | null>(null);

  useEffect(() => {
    if (!expression) {
      return;
    }

    const key = `${expression.expression}:${expression.weight}:${expression.durationMs}`;
    if (lastExpressionKeyRef.current === key) {
      return;
    }
    lastExpressionKeyRef.current = key;
    beginExpressionCue(expressionStateRef.current, expression, performance.now());
  }, [expression]);

  useEffect(() => {
    const vrm = vrmRef.current;
    const clipState = clipStateRef.current;
    const loaded = loadedClipsRef.current;

    if (avatarMotionV2) {
      if (!gesture) {
        lastGestureKeyRef.current = null;
      }
      beginClipGesture(clipState, vrm, null);
      beginGestureCue(gestureStateRef.current, null, performance.now());
      return;
    }

    if (!gesture) {
      lastGestureKeyRef.current = null;
      beginClipGesture(clipState, vrm, null);
      beginGestureCue(gestureStateRef.current, null, performance.now());
      return;
    }

    const key = gestureKey(gesture);
    if (lastGestureKeyRef.current === key) {
      return;
    }
    lastGestureKeyRef.current = key;

    const mode = resolveGesturePlayback(gesture.name, loaded);
    if (mode === "clip" && vrm) {
      beginGestureCue(gestureStateRef.current, null, performance.now());
      beginClipGesture(clipState, vrm, gesture.name);
      return;
    }

    beginClipGesture(clipState, vrm, null);
    beginGestureCue(gestureStateRef.current, gesture, performance.now());
  }, [gesture]);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }

    if (!modelUrl) {
      setStatus("loading");
      setErrorMessage(null);
      return;
    }

    let disposed = false;
    let sceneHandle: AvatarScene | null = null;
    let currentVrm: VRM | null = null;
    let idleState: IdleMotionState | null = null;
    let rafId = 0;
    const clock = new Clock();
    const abort = new AbortController();
    expressionStateRef.current = createExpressionMotionState();
    gestureStateRef.current = createGestureMotionState();
    disposeClipMotion(clipStateRef.current);
    clipStateRef.current = createClipMotionState();
    loadedClipsRef.current = new Set();
    poseStateRef.current = createPoseMotionState();
    restPoseStateRef.current = createRestPoseState();
    postureStateRef.current = createPostureState();
    idleLifeRef.current = createIdleLifeState();
    directorRef.current = createDirector(visualStateRef.current, performance.now());
    lookAtRef.current = createLookAtState();
    speechEnergyRef.current = createSpeechEnergyState();
    visemeReleaseStartRef.current = null;
    visemeWasSpeakingRef.current = false;
    lastExpressionKeyRef.current = null;
    lastGestureKeyRef.current = null;
    setPoseTarget(poseStateRef.current, visualStateRef.current);

    try {
      sceneHandle = createAvatarScene(container);
      sceneHandle.canvas.style.opacity = "0";
      sceneHandleRef.current = sceneHandle;
      setSceneEpoch((epoch) => epoch + 1);
    } catch (error) {
      setStatus("error");
      setErrorMessage(
        error instanceof Error ? error.message : "No se pudo crear la escena WebGL.",
      );
      return;
    }

    setStatus("loading");
    setErrorMessage(null);

    const tick = () => {
      if (disposed || !sceneHandle) {
        return;
      }
      if (typeof document !== "undefined" && document.hidden) {
        rafId = 0;
        return;
      }
      const delta = clampDelta(clock.getDelta());
      if (currentVrm && idleState) {
        const now = performance.now();
        const speakingNow = speakingRef.current;
        let visemeReleaseStart: number | null = null;
        const motionV2: boolean = avatarMotionV2;
        if (motionV2) {
          if (speakingNow) {
            visemeWasSpeakingRef.current = true;
            visemeReleaseStartRef.current = null;
          } else if (visemeWasSpeakingRef.current) {
            visemeReleaseStartRef.current = now;
            visemeWasSpeakingRef.current = false;
          }
          visemeReleaseStart = visemeReleaseStartRef.current;
          if (
            visemeReleaseStart !== null &&
            now - visemeReleaseStart > VISEME_RELEASE_MS
          ) {
            visemeReleaseStartRef.current = null;
          }
        }
        const frame: LegacyFrame = {
          vrm: currentVrm,
          idleState,
          delta,
          now,
          clipState: clipStateRef.current,
          expressionState: expressionStateRef.current,
          gestureState: gestureStateRef.current,
          poseState: poseStateRef.current,
          restPoseState: restPoseStateRef.current,
          postureState: postureStateRef.current,
          idleLife: idleLifeRef.current,
          visualState: visualStateRef.current,
          shot: shotRef.current,
          camera: sceneHandle.camera,
          lipSync: lipSyncRef.current,
          hubGesture: gestureRef.current,
          turnError: turnErrorRef.current,
          ready: readyRef.current,
          toolGesture: null,
          motionPreview: motionPreviewRef.current,
          typing: typingRef.current,
          intent: intentRef.current,
          idleAgeMs: idleAgeRef.current,
        };
        if (motionV2 && currentVrm && sceneHandle) {
          const queue = directorRef.current.queue;
          const gapSec = (now - queue.lastTickMs) / 1000;
          queue.lastTickMs = now - clampDelta(gapSec) * 1000;
          const hidden = typeof document !== "undefined" && document.hidden;
          const chimeState = speakingNow ? "speaking" : visualStateRef.current;
          toolSlotsRef.current = syncToolIcons(
            toolSlotsRef.current,
            activeToolsRef.current,
            shotRef.current,
            now,
          );
          const chimes = takeToolChimes(
            toolSlotsRef.current,
            shouldPlayToolChime(chimeState, hidden),
          );
          for (const direction of chimes) {
            playStageToolChime(lipSyncRef.current?.audioContext ?? null, direction);
          }
          setLookTarget(
            lookAtRef.current,
            toolLookObject(toolLayerRef.current, toolSlotsRef.current, now),
          );
          frame.toolGesture = primaryToolGesture(toolSlotsRef.current);
          frameV2(
            frame,
            directorRef.current,
            lookAtRef.current,
            speakingNow,
            visemeReleaseStart,
            speechEnergyRef.current,
          );
          // After the pose, so the icon sits on this frame's palm, not the last one.
          updateToolSprites(
            toolLayerRef.current,
            sceneHandle.scene,
            currentVrm,
            toolSlotsRef.current,
            now,
          );
        } else {
          if (toolLayerRef.current.sprites.size > 0 || toolSlotsRef.current.length > 0) {
            disposeToolSprites(toolLayerRef.current, sceneHandle?.scene ?? null);
            toolSlotsRef.current = [];
            setLookTarget(lookAtRef.current, null);
          }
          frameLegacy(frame);
        }
      }
      if (cameraMotionRef.current) {
        updateCameraShotMotion(
          sceneHandle.camera,
          cameraMotionRef.current,
          delta,
        );
      }
      sceneHandle.renderer.render(sceneHandle.scene, sceneHandle.camera);
      rafId = requestAnimationFrame(tick);
    };
    const onVisibilityChange = () => {
      if (disposed) {
        return;
      }
      if (document.hidden) {
        cancelAnimationFrame(rafId);
        rafId = 0;
        return;
      }
      if (currentVrm) {
        settleSpringBones(currentVrm, RETURN_PRE_ROLL_STEPS);
      }
      if (!disposed && rafId === 0) {
        rafId = requestAnimationFrame(tick);
      }
    };
    document.addEventListener("visibilitychange", onVisibilityChange);
    rafId = requestAnimationFrame(tick);

    void (async () => {
      try {
        const { vrm } = await loadVrm(modelUrl, abort.signal);
        if (disposed || !sceneHandle) {
          VRMUtils.deepDispose(vrm.scene);
          return;
        }
        placeAndFrameVrm(vrm, sceneHandle.scene, sceneHandle.camera);
        const framing = framingFromVrm(vrm, shotRef.current);
        applyCameraFraming(sceneHandle.camera, framing);
        cameraMotionRef.current = createCameraShotMotion(framing);
        appliedShotRef.current = shotRef.current;
        currentVrm = vrm;
        vrmRef.current = vrm;
        resetAppliedArms();
        const motionV2: boolean = avatarMotionV2;
        if (motionV2) {
          measureArmRig(vrm);
        }
        idleState = createIdleMotionState();
        idleLifeRef.current = createIdleLifeState(performance.now());

        const loaded = await loadGestureClips(
          vrm,
          clipStateRef.current,
          abort.signal,
        );
        if (disposed) {
          disposeClipMotion(clipStateRef.current);
          return;
        }
        loadedClipsRef.current = loaded;

        if (disposed || !sceneHandle) {
          return;
        }
        settleSpringBones(vrm, LOAD_PRE_ROLL_STEPS);
        sceneHandle.canvas.style.transition = `opacity ${CANVAS_FADE_MS}ms linear`;
        sceneHandle.canvas.style.opacity = "1";

        readyRef.current = true;
        setStatus("ready");

        const pendingExpression = expressionRef.current;
        if (pendingExpression) {
          const key = `${pendingExpression.expression}:${pendingExpression.weight}:${pendingExpression.durationMs}`;
          lastExpressionKeyRef.current = key;
          beginExpressionCue(
            expressionStateRef.current,
            pendingExpression,
            performance.now(),
          );
        }

        const pendingGesture = gestureRef.current;
        if (pendingGesture && !motionV2) {
          const key = gestureKey(pendingGesture);
          lastGestureKeyRef.current = key;
          const mode = resolveGesturePlayback(pendingGesture.name, loaded);
          if (mode === "clip") {
            beginGestureCue(gestureStateRef.current, null, performance.now());
            beginClipGesture(clipStateRef.current, vrm, pendingGesture.name);
          } else {
            beginGestureCue(
              gestureStateRef.current,
              pendingGesture,
              performance.now(),
            );
          }
        }

        if (speakingRef.current) {
          await attachLipSync(vrm);
        }
      } catch (error) {
        if (abort.signal.aborted || disposed) {
          return;
        }
        const message =
          error instanceof Error ? error.message : "No se pudo cargar el VRM.";
        const isMissing =
          message.includes("404") ||
          /failed to load|load failed|Not Found/i.test(message);
        setStatus("error");
        setErrorMessage(
          isMissing
            ? "No se encontró el archivo VRM. Colocá el modelo en public/avatars/ (ver README)."
            : message,
        );
      }
    })();

    async function attachLipSync(vrm: VRM): Promise<void> {
      if (disposed) {
        return;
      }
      try {
        let lipSync = lipSyncRef.current;
        if (!lipSync) {
          const ctx = await resumeSharedAudioContext();
          if (disposed) {
            return;
          }
          lipSync = await createAvatarLipSync(vrm, ctx);
          if (disposed) {
            lipSync.dispose();
            return;
          }
          lipSyncRef.current = lipSync;
        }
        await lipSync.useStream(getPlaybackAnalysisStream(lipSync.audioContext));
      } catch {
        /* AudioWorklet / context may fail without a prior gesture; retry on speaking. */
      }
    }

    return () => {
      disposed = true;
      readyRef.current = false;
      document.removeEventListener("visibilitychange", onVisibilityChange);
      disposeToolSprites(toolLayerRef.current, sceneHandle?.scene ?? null);
      toolSlotsRef.current = [];
      setLookTarget(lookAtRef.current, null);
      abort.abort();
      cancelAnimationFrame(rafId);
      lipSyncRef.current?.dispose();
      lipSyncRef.current = null;
      vrmRef.current = null;
      cameraMotionRef.current = null;
      appliedShotRef.current = null;
      disposeClipMotion(clipStateRef.current);
      loadedClipsRef.current = new Set();
      if (currentVrm && sceneHandle) {
        disposeVrm(currentVrm, sceneHandle.scene);
        currentVrm = null;
      }
      sceneHandleRef.current = null;
      sceneHandle?.dispose();
      sceneHandle = null;
    };
  }, [modelUrl]);

  useEffect(() => {
    const vrm = vrmRef.current;
    const scene = sceneHandleRef.current;
    const motion = cameraMotionRef.current;
    if (!vrm || !scene || !motion || status !== "ready") {
      return;
    }
    if (appliedShotRef.current === shot) {
      return;
    }
    appliedShotRef.current = shot;
    retargetCameraShot(motion, framingFromVrm(vrm, shot), false);
  }, [shot, status]);

  useEffect(() => {
    const scene = sceneHandleRef.current;
    if (!scene) {
      return;
    }
    const abort = new AbortController();
    void applySceneBackdrop(scene, backgroundUrl, abort.signal, backgroundAlign);
    return () => abort.abort();
  }, [backgroundUrl, backgroundAlign, sceneEpoch]);

  useEffect(() => {
    if (!import.meta.env.DEV) {
      return;
    }
    const host = window as Window & { __vera?: VeraInspector };
    Object.defineProperty(host, "__vera", {
      configurable: true,
      enumerable: true,
      get(): VeraInspector {
        const director = directorRef.current;
        return {
          published: director.published,
          requested: director.requested,
          arm: director.queue.arm?.name ?? null,
          phase: director.queue.arm?.phase ?? null,
        };
      },
    });
    return () => {
      delete host.__vera;
    };
  }, []);

  useEffect(() => {
    const lipSync = lipSyncRef.current;
    const vrm = vrmRef.current;

    if (!speaking) {
      lipSync?.stop();
      return;
    }

    if (!vrm) {
      return;
    }

    let cancelled = false;

    void (async () => {
      try {
        let instance = lipSyncRef.current;
        if (!instance) {
          const ctx = await resumeSharedAudioContext();
          if (cancelled) {
            return;
          }
          instance = await createAvatarLipSync(vrm, ctx);
          if (cancelled) {
            instance.dispose();
            return;
          }
          lipSyncRef.current = instance;
        }
        await instance.useStream(getPlaybackAnalysisStream(instance.audioContext));
      } catch {
        /* same as attach path */
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [speaking]);

  return (
    <div className={className}>
      <div ref={containerRef} className="absolute inset-0" />
      {status === "loading" && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center bg-black/25 px-3 text-center text-sm text-white">
          {modelUrl ? "Cargando avatar…" : "Esperando personalidad…"}
        </div>
      )}
      {status === "error" && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center bg-black/55 px-4 text-center text-sm text-red-100">
          {errorMessage ?? "Error al cargar el avatar."}
        </div>
      )}
    </div>
  );
}
