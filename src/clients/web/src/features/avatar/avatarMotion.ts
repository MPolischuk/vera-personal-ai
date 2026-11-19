/** Client motion constants (S16, S17). Not personality.yaml. */

/** Multiplies every arm-gesture layer weight. */
export const GESTURE_SCALE = 0.85;

/** Global gap between the end of one arm gesture and the start of the next. */
export const ARM_GESTURE_COOLDOWN_MS = 1500;

/** Same arm one-shot cannot start again before this (listening nod is exempt). */
export const SAME_ARM_ONESHOT_MS = 4000;

/** Cancelled gestures enter this release. At 60 Hz, |Δw| stays under 0.08. */
export const GESTURE_RELEASE_MS = 180;

/** Per frame at 60 Hz, on a cancellation. */
export const MAX_WEIGHT_STEP = 0.08;

export const FRAME_MS = 1000 / 60;

/** `speaking` holds `explain` at this weight until energy exists (D4). */
export const EXPLAIN_WEIGHT = 0.4;

/** `thinking` must last this long before `think` (D5). */
export const THINK_DELAY_MS = 1500;

/** Listening nod, low intensity, at most one every 2 s (D6). */
export const LISTENING_NOD_INTENSITY = 0.35;

export const LISTENING_NOD_MIN_MS = 2000;

/** Inside the 2–4 s window from the gesture notes. */
export const LISTENING_NOD_PERIOD_MS = 3000;

/** Welcome, once per ready (D8). */
export const HANDS_DURATION_MS = 1200;

/** Existing shrug length, inside the 1.0–1.4 s note. */
export const SHRUG_DURATION_MS = 1200;

/** Face energy. Accent peaks scale by this divided by 0.6 (S16, D1). */
export const EXPRESSIVENESS = 0.6;

/** Resting preset. Not written as `neutral` (D2). */
export const BASE_MOOD = "relaxed" as const;

/** Relaxed base while no accent covers the face (D2). */
export const BASE_RELAXED_WEIGHT = {
  idle: 0.15,
  listening: 0.2,
  thinking: 0.4,
  speaking: 0.2,
} as const;

/** Accent attack from the base (D1). */
export const ACCENT_RISE_MS = 200;

/** Short sentence. Not aligned to TTS (S10). */
export const ACCENT_HOLD_MS = 1500;

/** Return to the base (D1). */
export const ACCENT_FALL_MS = 500;

/** Opposite smoothstep when one accent replaces another (D3). */
export const EXPRESSION_CROSS_MS = 600;

/** `surprised` is back under the body threshold before this (D1). */
export const SURPRISED_MAX_MS = 1000;

/** Hub weight cap for every accent except `surprised`. */
export const ACCENT_WEIGHT_CAP = 0.7;

/** `surprised` may keep the hub peak. */
export const SURPRISED_WEIGHT_CAP = 0.8;

/** `angry` accent stays at or under this (D4). Raised from 0.4 to 0.7: vera-maid's angry morph barely read. */
export const ANGRY_ACCENT_CAP = 0.7;

/**
 * Per-preset gain on the hub weight before the cap. vera-maid's sad, angry and
 * relaxed morphs are subtle, so they need more weight than happy to read.
 */
export const ACCENT_GAIN = { angry: 1.25, sad: 1.2, relaxed: 1.3 } as const;

/** Per-preset caps above the shared hub cap (still below uncanny 1.0). */
export const ACCENT_PRESET_CAP = { sad: 0.85, relaxed: 0.9 } as const;

/** Body modifiers apply only above this. */
export const EMOTION_BODY_THRESHOLD = 0.3;

/** Arm-layer multiplier while `happy` is above the threshold (D4). */
export const HAPPY_GESTURE_SCALE = 1.15;

/** Arm-layer multiplier while `sad` is above the threshold (D4). */
export const SAD_GESTURE_SCALE = 0.7;

/** Head pitch added while `happy` is above the threshold (radians, D4). */
export const HAPPY_HEAD_PITCH = 0.05;

/** Head pitch added while `surprised` is above the threshold (radians, D4). */
export const SURPRISED_HEAD_PITCH = -0.08;
