import type { AvatarVisualState } from "../api/protocol";
import {
  expressionCueForVisualState,
  type AvatarExpressionCue,
} from "../features/avatar/applyExpression";
import type { VoiceStatus } from "../features/voice/voiceController";
import {
  holdIsLive,
  type CharacterExpressionHold,
} from "./avatarCueLifetime";

export interface AvatarPresentationInput {
  voiceStatus: VoiceStatus;
  turnActive: boolean;
  avatarState: AvatarVisualState | null;
}

export interface ResolveAvatarExpressionInput {
  visualState: AvatarVisualState;
  characterHold: CharacterExpressionHold | null;
  currentTurnId: string | null;
}

/**
 * Client overlay for G4. Server cues matter for other clients; the web also
 * projects capture / playback that the server cannot see (PT-18 D2 / D3).
 */
export function resolveAvatarPresentation(input: AvatarPresentationInput): AvatarVisualState {
  if (input.voiceStatus === "playing" || input.avatarState === "speaking") {
    return "speaking";
  }

  if (input.turnActive || input.avatarState === "thinking") {
    return "thinking";
  }

  if (
    input.voiceStatus === "listening" ||
    input.voiceStatus === "capturing" ||
    input.avatarState === "listening"
  ) {
    return "listening";
  }

  return "idle";
}

/**
 * Prefers a live character hold over the PT-18 state overlay (D6).
 */
export function resolveAvatarExpression(
  input: ResolveAvatarExpressionInput,
): AvatarExpressionCue {
  if (holdIsLive(input.characterHold, input.currentTurnId)) {
    return input.characterHold.cue;
  }
  return expressionCueForVisualState(input.visualState);
}
