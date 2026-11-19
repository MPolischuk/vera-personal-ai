import { describe, expect, it } from "vitest";
import { chooseSide } from "./arms";
import {
  createDirector,
  directGestures,
  type DirectorCues,
} from "./director";
import {
  admitActiveTool,
  completeActiveTool,
  LOOK_LEAD_MS,
  markToolAlert,
  MAX_ACTIVE_TOOLS,
  primaryToolGesture,
  shouldPlayToolChime,
  syncToolIcons,
  takeToolChimes,
  toolGlanceActive,
  toolIconLift,
  toolIconOpacity,
  toolIconScale,
  TOOL_GLANCE_END_MS,
  TOOL_ICON_APPEAR_DELAY_MS,
  TOOL_ICON_EXIT_LIFT_M,
  TOOL_ICON_EXIT_MS,
  TOOL_ICON_RISE_M,
  toolIconFloat,
  toolIconKind,
  TOOL_ICON_BASE_SCALE,
  TOOL_ICON_ENTER_MS,
  TOOL_ICON_FLOAT_M,
  type ToolIconSlot,
} from "./toolIcon";

function quiet(shot: DirectorCues["shot"]): DirectorCues {
  return {
    shot,
    hubGesture: null,
    turnError: false,
    ready: false,
  };
}

describe("active tools", () => {
  it("keeps at most two, in arrival order", () => {
    let tools = admitActiveTool([], "web_search");
    tools = admitActiveTool(tools, "reminders");
    const blocked = admitActiveTool(tools, "calendar");
    expect(blocked).toBe(tools);
    expect(blocked).toHaveLength(MAX_ACTIVE_TOOLS);
    expect(blocked.map((tool) => tool.name)).toEqual(["web_search", "reminders"]);
  });

  it("removes the oldest entry of that name", () => {
    let tools = admitActiveTool([], "search");
    tools = admitActiveTool(tools, "search");
    const oldest = tools[0]?.id;
    const next = completeActiveTool(tools, "search");
    expect(next).toHaveLength(1);
    expect(next[0]?.id).not.toBe(oldest);
    expect(next[0]?.name).toBe("search");
  });

  it("turns a failure into the alert shape without moving the slot", () => {
    const tools = admitActiveTool([], "search");
    const marked = markToolAlert(tools, "search");
    expect(marked.id).toBe(tools[0]?.id);
    expect(marked.tools[0]?.alert).toBe(true);
    expect(toolIconKind("search", true)).toBe("alert");
  });
});

describe("tool icon kind", () => {
  it("maps search, remind, and everything else", () => {
    expect(toolIconKind("Web_Search", false)).toBe("search");
    expect(toolIconKind("reminders", false)).toBe("bell");
    expect(toolIconKind("calendar", false)).toBe("gear");
  });
});

describe("shouldPlayToolChime", () => {
  it("is silent while speaking or while the tab is hidden", () => {
    expect(shouldPlayToolChime("speaking", false)).toBe(false);
    expect(shouldPlayToolChime("idle", true)).toBe(false);
    expect(shouldPlayToolChime("thinking", true)).toBe(false);
  });

  it("plays while idle and the tab is visible", () => {
    expect(shouldPlayToolChime("idle", false)).toBe(true);
    expect(shouldPlayToolChime("listening", false)).toBe(true);
    expect(shouldPlayToolChime("thinking", false)).toBe(true);
  });
});

describe("tool icon placement", () => {
  it("locks the side chosen on the first frame", () => {
    const tools = admitActiveTool([], "search");
    const first = syncToolIcons([], tools, "bust", 0);
    const again = syncToolIcons(first, tools, "waist", 16);
    expect(again[0]?.side).toBe(first[0]?.side);
    expect(again[0]?.gesture).toBe(first[0]?.gesture);
    expect(again[0]?.anchor).toBe("palm");
    expect(first[0]?.side).toBe(chooseSide({ x: -0.32, y: 0.02, z: 0.4 }).side);
    expect(first[0]?.armAtMs).toBe(LOOK_LEAD_MS);
  });

  it("puts the second icon on the other side", () => {
    let tools = admitActiveTool([], "search");
    tools = admitActiveTool(tools, "calendar");
    const slots = syncToolIcons([], tools, "full", 0);
    expect(slots[0]?.gesture).toBe("hold");
    expect(slots[0]?.anchor).toBe("palm");
    expect(slots[1]?.side).not.toBe(slots[0]?.side);
    const later = syncToolIcons(slots, tools, "full", 32);
    expect(later.map((slot) => slot.side)).toEqual(slots.map((slot) => slot.side));
  });

  it("presents the icon on the palm in every shot", () => {
    const tools = [{ id: 1, name: "set_reminder", alert: false }];
    for (const shot of ["bust", "waist", "full"] as const) {
      const slot = syncToolIcons([], tools, shot, 0)[0];
      expect(slot?.gesture).toBe("hold");
      expect(slot?.anchor).toBe("palm");
    }
  });

  it("fades and grows in after the hand arrives, without overshoot", () => {
    expect(toolIconOpacity(0, null)).toBe(0);
    expect(toolIconOpacity(TOOL_ICON_APPEAR_DELAY_MS, null)).toBe(0);
    let last = -1;
    let maxScale = 0;
    for (let t = 0; t <= TOOL_ICON_APPEAR_DELAY_MS + TOOL_ICON_ENTER_MS; t += 25) {
      const o = toolIconOpacity(t, null);
      expect(o).toBeGreaterThanOrEqual(last);
      last = o;
      maxScale = Math.max(maxScale, toolIconScale(t, null));
    }
    expect(last).toBeCloseTo(1);
    expect(maxScale).toBeLessThanOrEqual(TOOL_ICON_BASE_SCALE + 1e-9);
    expect(toolIconScale(0, null)).toBeGreaterThan(0.5 * TOOL_ICON_BASE_SCALE);
    expect(toolIconLift(TOOL_ICON_APPEAR_DELAY_MS, null)).toBeCloseTo(-TOOL_ICON_RISE_M);
    expect(toolIconLift(5000, null)).toBeCloseTo(0);
  });

  it("fades out softly, drifting up, instead of popping", () => {
    const settled = 5000;
    expect(toolIconOpacity(settled, 0)).toBeCloseTo(1);
    const half = toolIconOpacity(settled, TOOL_ICON_EXIT_MS / 2);
    expect(half).toBeGreaterThan(0.3);
    expect(half).toBeLessThan(0.7);
    expect(toolIconOpacity(settled, TOOL_ICON_EXIT_MS)).toBeCloseTo(0);
    expect(toolIconLift(settled, TOOL_ICON_EXIT_MS)).toBeCloseTo(TOOL_ICON_EXIT_LIFT_M);
    expect(TOOL_ICON_EXIT_MS).toBeGreaterThanOrEqual(350);
  });

  it("floats gently and only once visible", () => {
    expect(Math.abs(toolIconFloat(1234, 1))).toBeLessThanOrEqual(TOOL_ICON_FLOAT_M);
    expect(TOOL_ICON_FLOAT_M).toBeLessThanOrEqual(0.01);
    expect(toolIconFloat(1234, 0)).toBeCloseTo(0);
  });

  it("glances at the icon briefly, then back to the user", () => {
    const tools = [{ id: 1, name: "set_reminder", alert: false }];
    const slot = syncToolIcons([], tools, "waist", 0)[0]!;
    expect(toolGlanceActive(slot, 200)).toBe(true);
    expect(toolGlanceActive(slot, TOOL_GLANCE_END_MS + 1)).toBe(false);
    const leaving = syncToolIcons([slot], [], "waist", 300)[0]!;
    expect(toolGlanceActive(leaving, 300)).toBe(false);
  });
});

describe("tool chime edges", () => {
  it("does not ask for a beep while speaking, and does not ask again later", () => {
    const tools = admitActiveTool([], "search");
    const slots = syncToolIcons([], tools, "bust", 0);
    expect(takeToolChimes(slots, shouldPlayToolChime("speaking", false))).toEqual([]);
    expect(takeToolChimes(slots, shouldPlayToolChime("idle", false))).toEqual([]);
  });

  it("asks once on the way in and once on the way out", () => {
    const tools = admitActiveTool([], "search");
    const slots: ToolIconSlot[] = syncToolIcons([], tools, "bust", 0);
    expect(takeToolChimes(slots, true)).toEqual(["rise"]);
    const leaving = syncToolIcons(slots, [], "bust", 400);
    expect(takeToolChimes(leaving, true)).toEqual(["fall"]);
    expect(takeToolChimes(leaving, true)).toEqual([]);
  });
});

describe("tool gesture", () => {
  it("waits 150 ms, then holds on the side that was chosen", () => {
    const director = createDirector("thinking", 0);
    const tools = admitActiveTool([], "web_search");
    const slots = syncToolIcons([], tools, "bust", 0);
    const cue = primaryToolGesture(slots);
    directGestures(director, 0, { ...quiet("bust"), toolGesture: cue }, true);
    expect(director.queue.arm?.name).not.toBe("hold");
    expect(director.queue.pending.some((item) => item.name === "hold")).toBe(false);

    directGestures(director, LOOK_LEAD_MS, { ...quiet("bust"), toolGesture: cue }, true);
    expect(director.queue.arm?.name).toBe("hold");
    expect(director.queue.arm?.hands).toBe(slots[0]?.side);
    expect(director.queue.arm?.priority).toBe(1);
  });

  it("holds on waist and full after the same lead", () => {
    const director = createDirector("thinking", 0);
    const tools = admitActiveTool([], "calendar");
    const slots = syncToolIcons([], tools, "waist", 0);
    directGestures(
      director,
      LOOK_LEAD_MS,
      { ...quiet("waist"), toolGesture: primaryToolGesture(slots) },
      true,
    );
    expect(director.queue.arm?.name).toBe("hold");
    expect(director.queue.arm?.hands).toBe(slots[0]?.side);
  });

  it("does not enqueue while motion v2 is off", () => {
    const director = createDirector("thinking", 0);
    const tools = admitActiveTool([], "search");
    const slots = syncToolIcons([], tools, "bust", 0);
    directGestures(
      director,
      LOOK_LEAD_MS,
      { ...quiet("bust"), toolGesture: primaryToolGesture(slots) },
      false,
    );
    expect(director.queue.arm).toBeNull();
    expect(director.queue.pending).toEqual([]);
  });

  it("replaces a thinking arm instead of waiting out the cooldown", () => {
    const director = createDirector("thinking", 0);
    directGestures(director, 1500, quiet("full"), true);
    expect(director.queue.arm?.name).toBe("think");
    const tools = admitActiveTool([], "search");
    const slots = syncToolIcons([], tools, "full", 1600);
    directGestures(
      director,
      1600 + LOOK_LEAD_MS,
      { ...quiet("full"), toolGesture: primaryToolGesture(slots) },
      true,
    );
    expect(director.queue.arm?.name).toBe("hold");
    expect(director.queue.arm?.hands).toBe(slots[0]?.side);
  });

  it("releases the arm when the icon leaves", () => {
    const director = createDirector("thinking", 0);
    const tools = admitActiveTool([], "search");
    const slots = syncToolIcons([], tools, "bust", 0);
    directGestures(
      director,
      LOOK_LEAD_MS,
      { ...quiet("bust"), toolGesture: primaryToolGesture(slots) },
      true,
    );
    expect(director.queue.arm?.name).toBe("hold");
    const leaving = syncToolIcons(slots, [], "bust", 400);
    directGestures(
      director,
      400,
      { ...quiet("bust"), toolGesture: primaryToolGesture(leaving) },
      true,
    );
    expect(director.queue.arm?.name === "hold" ? director.queue.arm.phase : "gone").not.toBe(
      "hold",
    );
  });
});
