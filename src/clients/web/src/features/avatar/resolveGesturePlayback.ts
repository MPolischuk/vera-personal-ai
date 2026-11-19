/**
 * Clip wins only when that id finished load + retarget into `loaded` (D5).
 */
export function resolveGesturePlayback(
  name: string,
  loaded: ReadonlySet<string>,
): "clip" | "procedural" {
  return loaded.has(name) ? "clip" : "procedural";
}
