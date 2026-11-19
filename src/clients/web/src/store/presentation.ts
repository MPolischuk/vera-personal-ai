import {
  DEFAULT_CAMERA_SHOT,
  stepCameraShot,
  type CameraShot,
  type CameraShotDirection,
} from "../features/avatar/cameraShot";

/**
 * Presentation mode for the chat surface (column, full-viewport stage, expression bank).
 */
export type PresentationMode = "chat" | "stage" | "bank";

export type { CameraShot, CameraShotDirection };

export interface PresentationState {
  presentationMode: PresentationMode;
  stageChatVisible: boolean;
  stageCameraShot: CameraShot;
}

export const initialPresentationState: PresentationState = {
  presentationMode: "chat",
  stageChatVisible: true,
  stageCameraShot: DEFAULT_CAMERA_SHOT,
};

export function pickPresentation(state: PresentationState): PresentationState {
  return {
    presentationMode: state.presentationMode,
    stageChatVisible: state.stageChatVisible,
    stageCameraShot: state.stageCameraShot,
  };
}

/** Full-viewport shell shared by stage and bank (D1). */
export function isViewportMode(mode: PresentationMode): boolean {
  return mode === "stage" || mode === "bank";
}

export function enterStage(_state: PresentationState): PresentationState {
  return {
    presentationMode: "stage",
    stageChatVisible: true,
    stageCameraShot: DEFAULT_CAMERA_SHOT,
  };
}

export function enterBank(_state: PresentationState): PresentationState {
  return {
    presentationMode: "bank",
    stageChatVisible: false,
    stageCameraShot: DEFAULT_CAMERA_SHOT,
  };
}

export function exitStage(_state: PresentationState): PresentationState {
  return {
    presentationMode: "chat",
    stageChatVisible: true,
    stageCameraShot: DEFAULT_CAMERA_SHOT,
  };
}

export function toggleStageChat(state: PresentationState): PresentationState {
  if (state.presentationMode !== "stage") {
    return state;
  }
  return {
    ...state,
    stageChatVisible: !state.stageChatVisible,
  };
}

/** Discrete camera dolly in stage and in the bank. The chat column stays on bust. */
export function stepStageCameraShot(
  state: PresentationState,
  direction: CameraShotDirection,
): PresentationState {
  if (state.presentationMode !== "stage" && state.presentationMode !== "bank") {
    return state;
  }
  return {
    ...state,
    stageCameraShot: stepCameraShot(state.stageCameraShot, direction),
  };
}

/** Reset stage when leaving Chat (D5 / D11). */
export function onAppViewChange(
  state: PresentationState,
  view: "chat" | "settings" | "usage" | "memory" | "extensions" | "reminders" | "activity" | "briefing" | "diary",
): PresentationState {
  if (view !== "chat") {
    return {
      presentationMode: "chat",
      stageChatVisible: true,
      stageCameraShot: DEFAULT_CAMERA_SHOT,
    };
  }
  return state;
}

/**
 * When a confirmation arrives: force stage overlay open, or leave bank for the
 * column so the card is visible (D10).
 */
export function onPendingConfirmation(
  state: PresentationState,
  pending: boolean,
): PresentationState {
  if (!pending) {
    return state;
  }
  if (state.presentationMode === "bank") {
    return {
      presentationMode: "chat",
      stageChatVisible: true,
      stageCameraShot: DEFAULT_CAMERA_SHOT,
    };
  }
  if (state.presentationMode === "stage" && !state.stageChatVisible) {
    return { ...state, stageChatVisible: true };
  }
  return state;
}
