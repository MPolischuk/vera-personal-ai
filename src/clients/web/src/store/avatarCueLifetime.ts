import {
  isAvatarExpressionName,
  type AvatarExpressionCue,
} from "../features/avatar/applyExpression";

/** Matches AvatarCues.CharacterExpressionDurationMs (PT-25 D3). Ingress seal, not hold life. */
export const CHARACTER_EXPRESSION_DURATION_MS = 400;

export interface CharacterExpressionHold {
  cue: AvatarExpressionCue;
  turnId: string | null;
}

export interface AvatarCueGesture {
  name: string;
  intensity: number;
  durationMs: number;
  playId: string;
}

export interface AvatarCueState {
  characterExpression: CharacterExpressionHold | null;
  avatarGesture: AvatarCueGesture | null;
}

/** Ingress discriminator kept from PT-25 D3: the 400 ms seal plus a non-neutral preset. */
export function isCharacterExpressionCue(cue: AvatarExpressionCue): boolean {
  if (cue.durationMs !== CHARACTER_EXPRESSION_DURATION_MS) {
    return false;
  }
  if (!isAvatarExpressionName(cue.expression) || cue.expression === "neutral") {
    return false;
  }
  return true;
}

/** The hold belongs to a turn; it dies with that turn's identity (D6). */
export function holdIsLive(
  hold: CharacterExpressionHold | null,
  currentTurnId: string | null,
): hold is CharacterExpressionHold {
  if (hold === null) {
    return false;
  }
  return hold.turnId === currentTurnId;
}

export function onTurnStarted(state: AvatarCueState): AvatarCueState {
  return { ...state, characterExpression: null, avatarGesture: null };
}

export function onTurnCompleted(
  state: AvatarCueState,
  outcome: string,
): AvatarCueState {
  if (outcome === "Interrupted") {
    return { ...state, avatarGesture: null };
  }
  return state;
}

export function onTurnFailed(state: AvatarCueState): AvatarCueState {
  return { ...state, avatarGesture: null };
}

export function onExpressionCue(
  state: AvatarCueState,
  cue: AvatarExpressionCue,
  turnId: string | null,
): AvatarCueState {
  if (!isCharacterExpressionCue(cue)) {
    return state;
  }
  return { ...state, characterExpression: { cue, turnId } };
}

export function onGestureCue(
  state: AvatarCueState,
  cue: AvatarCueGesture,
): AvatarCueState {
  return { ...state, avatarGesture: cue };
}
