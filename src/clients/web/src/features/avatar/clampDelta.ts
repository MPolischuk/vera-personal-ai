/** Largest step that may reach the spring bones or the gesture director. */
const MAX_DELTA_SEC = 1 / 30;

/**
 * Caps a frame gap at 1/30 s. The leftover is discarded: a hidden tab must
 * not be integrated as several seconds of physics, nor replayed in a burst.
 */
export function clampDelta(dt: number): number {
  if (!Number.isFinite(dt) || dt <= 0) {
    return 0;
  }
  if (dt < MAX_DELTA_SEC) {
    return dt;
  }
  return MAX_DELTA_SEC;
}
