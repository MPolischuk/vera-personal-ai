// DEV-ONLY: mounts the avatar alone (no backend) to diagnose bank animations.
import { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import "../index.css";
import { AvatarStage } from "../features/avatar/AvatarStage";
import type { MotionPreview } from "../features/avatar/director";
import type { CameraShot } from "../features/avatar/cameraShot";
import type { AvatarExpressionCue, AvatarExpressionName } from "../features/avatar/applyExpression";
import { bankExpressionCue } from "../features/chat/expressionBank";

import type { IntentCue } from "../features/avatar/intentCue";
import type { AvatarVisualState } from "../features/avatar/AvatarStage";
import type { ActiveTool } from "../features/avatar/toolIcon";
let toolSeq = 0;
type S = { preview: MotionPreview | null; expr: AvatarExpressionCue | null; shot: CameraShot; speaking: boolean;
  typing: boolean; intent: IntentCue | null; age: number; state: AvatarVisualState; tools: ActiveTool[] };
type P = Partial<{ motion: string; expr: AvatarExpressionName; shot: CameraShot; speaking: boolean;
  typing: boolean; age: number; state: AvatarVisualState;
  /** Tool names in flight (e.g. "set_reminder", "web_search", "x", "!fail" marks alert). [] clears. */
  tools: string[];
  intent: { source: "user" | "assistant"; kind: IntentCue["kind"]; closing?: boolean } }>;
declare global { interface Window { bench: { set: (p: P) => void } } }

function Bench() {
  const q = new URLSearchParams(location.search);
  const [s, setS] = useState<S>({ preview: null, expr: null, shot: (q.get("shot") as CameraShot) ?? "full", speaking: false,
    typing: false, intent: null, age: Number(q.get("age") ?? 0), state: "idle", tools: [] });
  useEffect(() => {
    window.bench = {
      set: (p) => setS((o) => ({
        preview: p.motion ? { name: p.motion, playId: crypto.randomUUID() } : o.preview,
        expr: p.expr ? bankExpressionCue(p.expr) : o.expr,
        shot: p.shot ?? o.shot,
        speaking: p.speaking ?? o.speaking,
        typing: p.typing ?? o.typing,
        age: p.age ?? o.age,
        state: p.state ?? o.state,
        tools: p.tools ? p.tools.map((n) => ({ id: ++toolSeq, name: n.replace(/^!/, ""), alert: n.startsWith("!") })) : o.tools,
        intent: p.intent ? { ...p.intent, playId: crypto.randomUUID() } : o.intent,
      })),
    };
  }, []);
  return <div style={{ width: "100%", height: "100%" }}>
    <AvatarStage modelUrl={q.get("model") ?? "/avatars/vera-maid.vrm"} visualState={s.state} speaking={s.speaking}
      userTyping={s.typing} activeTools={s.tools} intent={s.intent} devIdleAgeMs={s.age}
      expression={s.expr} gesture={null} shot={s.shot} motionPreview={s.preview} className="h-full w-full" />
  </div>;
}
createRoot(document.getElementById("root")!).render(<Bench />);
if (new URLSearchParams(location.search).has("probe")) {
  import("./probe").then(m => m.runProbe(new URLSearchParams(location.search).get("model") ?? "/avatars/vera-maid.vrm")).then(r => { (window as any).probe = r; console.log("PROBE", JSON.stringify(r)); });
}
