/** Cancellable gesture envelope. Quintic attack, release 1.35× slower (D3, D4). */

export type EnvelopePhase = "attack" | "hold" | "release" | "off";

/**
 * Uncapped attack. A 1.35× release of this length stays under 0.08 per frame at 60 Hz.
 * Short gestures cap the attack at duration / 2.2 so the weight still reaches a peak.
 */
const NOMINAL_ATTACK_MS = 400;
const RELEASE_SLOWDOWN = 1.35;
const SHORT_GESTURE_DIVISOR = 2.2;

export interface EnvelopeSlot {
  phase: EnvelopePhase;
  startedAtMs: number;
  phaseStartedAtMs: number;
  durationMs: number;
  attackMs: number;
  releaseMs: number;
  /** Weight captured when release began. */
  releaseFrom: number;
}

function clamp01(u: number): number {
  if (u <= 0) {
    return 0;
  }
  if (u >= 1) {
    return 1;
  }
  return u;
}

/** Smoothstep quintic. */
function quint(u: number): number {
  const t = clamp01(u);
  return t * t * t * (t * (t * 6 - 15) + 10);
}

/** Attack with anticipation (−5 %) and overshoot (+4.5 %). */
export function attackWeight(u: number): number {
  let w = quint(u);
  if (u < 1) {
    w +=
      (u < 0.34
        ? -0.05 * Math.sin(Math.PI * Math.min(1, u * 3)) * (1 - quint(u))
        : 0) +
      0.045 * Math.sin(Math.PI * u) * u;
  }
  return w;
}

/** Release from the current weight w0, with a slight undershoot (−3 %). */
export function releaseWeight(u: number, w0: number): number {
  const base = w0 * (1 - quint(u));
  return base - 0.03 * Math.sin(Math.PI * (1 - u)) * u * w0;
}

export function createEnvelopeSlot(nowMs: number, durationMs: number): EnvelopeSlot {
  const duration = durationMs > 0 ? durationMs : 0;
  const attackMs =
    duration === 0
      ? 0
      : Math.min(NOMINAL_ATTACK_MS, duration / SHORT_GESTURE_DIVISOR);
  return {
    phase: duration === 0 ? "off" : "attack",
    startedAtMs: nowMs,
    phaseStartedAtMs: nowMs,
    durationMs: duration,
    attackMs,
    releaseMs: attackMs * RELEASE_SLOWDOWN,
    releaseFrom: 0,
  };
}

function scheduledWeight(slot: EnvelopeSlot, nowMs: number): number {
  if (slot.attackMs <= 0) {
    return attackWeight(1);
  }
  const elapsed = nowMs - slot.startedAtMs;
  if (elapsed <= 0) {
    return attackWeight(0);
  }
  if (elapsed >= slot.attackMs) {
    return attackWeight(1);
  }
  return attackWeight(elapsed / slot.attackMs);
}

function releaseStartsAt(slot: EnvelopeSlot): number {
  return (
    slot.startedAtMs + Math.max(slot.attackMs, slot.durationMs - slot.releaseMs)
  );
}

function enterRelease(slot: EnvelopeSlot, atMs: number, fromWeight: number): void {
  slot.phase = "release";
  slot.phaseStartedAtMs = atMs;
  slot.releaseFrom = fromWeight;
}

function readRelease(slot: EnvelopeSlot, nowMs: number): number {
  if (slot.releaseMs <= 0) {
    slot.phase = "off";
    return 0;
  }
  const u = (nowMs - slot.phaseStartedAtMs) / slot.releaseMs;
  if (u >= 1) {
    slot.phase = "off";
    return 0;
  }
  if (u <= 0) {
    return slot.releaseFrom;
  }
  return releaseWeight(u, slot.releaseFrom);
}

/** Weight at `nowMs`. Moves attack → hold → release → off. */
export function envelopeWeight(slot: EnvelopeSlot, nowMs: number): number {
  if (slot.phase === "off") {
    return 0;
  }
  if (slot.phase === "release") {
    return readRelease(slot, nowMs);
  }
  const releaseAt = releaseStartsAt(slot);
  if (nowMs >= releaseAt) {
    enterRelease(slot, releaseAt, scheduledWeight(slot, releaseAt));
    return readRelease(slot, nowMs);
  }
  slot.phase = nowMs >= slot.startedAtMs + slot.attackMs ? "hold" : "attack";
  return scheduledWeight(slot, nowMs);
}

/** Jump to release from the weight at `nowMs`. No step at the cut. */
export function cancel(slot: EnvelopeSlot, nowMs: number): void {
  if (slot.phase === "release" || slot.phase === "off") {
    return;
  }
  const releaseAt = releaseStartsAt(slot);
  if (nowMs >= releaseAt) {
    enterRelease(slot, releaseAt, scheduledWeight(slot, releaseAt));
    readRelease(slot, nowMs);
    return;
  }
  enterRelease(slot, nowMs, scheduledWeight(slot, nowMs));
}
