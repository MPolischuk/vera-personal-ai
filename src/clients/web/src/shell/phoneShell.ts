import {
  enterStage,
  exitStage,
  type PresentationState,
} from "../store/presentation";

export type PhoneShellView = "chat" | "memory" | "settings" | "usage" | "extensions" | "reminders" | "activity" | "briefing" | "diary";

export interface PhoneShellEntry {
  view: PhoneShellView;
  presentation: PresentationState;
}

export interface PhoneShellEntryResult {
  view: "chat";
  presentation: PresentationState;
}

/** Same cut as Tailwind `md` (768px): the shell is everything below it. */
export function isPhoneShellWidth(px: number): boolean {
  return px <= 767;
}

/** Vera on the phone: stage with the transcript overlay closed. */
export function enterShellStage(state: PresentationState): PresentationState {
  return { ...enterStage(state), stageChatVisible: false };
}

/**
 * Runs once when the viewport crosses into the phone shell.
 * Memory, settings, and usage become chat without the avatar.
 * An open stage stays Vera and keeps its camera shot.
 * Column chat and the bank become Vera with the overlay closed.
 */
export function applyPhoneShellEntry(input: PhoneShellEntry): PhoneShellEntryResult {
  if (input.view !== "chat") {
    return { view: "chat", presentation: exitStage(input.presentation) };
  }

  if (input.presentation.presentationMode === "stage") {
    return {
      view: "chat",
      presentation: { ...input.presentation, stageChatVisible: false },
    };
  }

  return { view: "chat", presentation: enterShellStage(input.presentation) };
}

/** A confirmation on the phone leaves Vera or the bank for the transcript. */
export function coercePhoneShellConfirmation(
  state: PresentationState,
): PresentationState {
  if (state.presentationMode === "chat") {
    return state;
  }
  return exitStage(state);
}
