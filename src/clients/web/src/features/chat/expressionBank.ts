import {
  AVATAR_EXPRESSION_WHITELIST,
  type AvatarExpressionCue,
  type AvatarExpressionName,
} from "../avatar/applyExpression";
import {
  AVATAR_GESTURE_WHITELIST,
  CHARACTER_GESTURE_DURATION_MS,
  CHARACTER_GESTURE_INTENSITY,
  type AvatarGestureCue,
  type AvatarGestureName,
} from "../avatar/applyGesture";

/** UI catalog = live whitelists (D3). Do not hardcode a parallel list in JSX. */
export const BANK_EXPRESSION_IDS: readonly AvatarExpressionName[] =
  AVATAR_EXPRESSION_WHITELIST;

export const BANK_GESTURE_IDS: readonly AvatarGestureName[] = AVATAR_GESTURE_WHITELIST;

/** Snap-and-hold expression for the bank (D6). durationMs 0 ≠ character hold 400. */
export function bankExpressionCue(name: AvatarExpressionName): AvatarExpressionCue {
  return {
    expression: name,
    weight: name === "neutral" ? 0 : 1,
    durationMs: 0,
  };
}

/** Gesture click for the bank (D7). Caller supplies a fresh playId each click. */
export function bankGestureCue(
  name: AvatarGestureName,
  playId: string,
): AvatarGestureCue {
  return {
    name,
    intensity: CHARACTER_GESTURE_INTENSITY,
    durationMs: CHARACTER_GESTURE_DURATION_MS,
    playId,
  };
}

const EXPRESSION_LABELS: Record<AvatarExpressionName, string> = {
  happy: "Alegría",
  angry: "Enojo",
  sad: "Tristeza",
  relaxed: "Calma",
  surprised: "Sorpresa",
  neutral: "Neutro",
};

export function bankExpressionLabel(name: AvatarExpressionName): string {
  return EXPRESSION_LABELS[name];
}

export type BankMotionKind = "rest" | "hub" | "pose" | "motion";

export interface BankMotionItem {
  id: string;
  label: string;
  kind: BankMotionKind;
}

/** Preview list. Grows here; the panel just renders it. */
export const BANK_MOTIONS: readonly BankMotionItem[] = [
  { id: "rest", label: "Reposo", kind: "rest" },
  { id: "explain", label: "Explicar", kind: "pose" },
  { id: "think", label: "Pensar", kind: "pose" },
  { id: "hold", label: "Sostener", kind: "pose" },
  { id: "hands", label: "Manos", kind: "pose" },
  { id: "point", label: "Señalar", kind: "pose" },
  { id: "nod", label: "Asentir", kind: "hub" },
  { id: "shake", label: "Negar", kind: "hub" },
  { id: "wave", label: "Saludar", kind: "hub" },
  { id: "shrug", label: "Encogerse", kind: "hub" },
  { id: "bow", label: "Reverencia", kind: "motion" },
  { id: "handChest", label: "Mano al pecho", kind: "motion" },
  { id: "hairTuck", label: "Pelo tras la oreja", kind: "motion" },
  { id: "apron", label: "Alisar delantal", kind: "motion" },
  { id: "clasp", label: "Manos juntas", kind: "motion" },
  { id: "rubFingers", label: "Frotar dedos", kind: "motion" },
  { id: "wristband", label: "Ajustar puño", kind: "motion" },
  { id: "sigh", label: "Suspiro", kind: "motion" },
  { id: "neckStretch", label: "Estirar cuello", kind: "motion" },
  { id: "yawn", label: "Bostezo", kind: "motion" },
  { id: "clap", label: "Aplauso", kind: "motion" },
  { id: "happyFist", label: "Puño feliz", kind: "motion" },
  { id: "sadClasp", label: "Triste", kind: "motion" },
  { id: "laugh", label: "Risa", kind: "motion" },
  { id: "surpriseStep", label: "Sorpresa", kind: "motion" },
  { id: "count", label: "Contar", kind: "motion" },
  { id: "openPalms", label: "Palmas abiertas", kind: "motion" },
  { id: "sidePoint", label: "Señalar al costado", kind: "motion" },
  { id: "openHand", label: "Cierre mano abierta", kind: "motion" },
];
