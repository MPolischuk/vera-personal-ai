import { describe, expect, it } from "vitest";
import {
  exitStage,
  initialPresentationState,
  type PresentationState,
} from "../store/presentation";
import {
  applyPhoneShellEntry,
  coercePhoneShellConfirmation,
  enterShellStage,
  isPhoneShellWidth,
} from "./phoneShell";

const chat: PresentationState = initialPresentationState;

const stageWaist: PresentationState = {
  presentationMode: "stage",
  stageChatVisible: true,
  stageCameraShot: "waist",
};

const stageFull: PresentationState = {
  presentationMode: "stage",
  stageChatVisible: false,
  stageCameraShot: "full",
};

const bank: PresentationState = {
  presentationMode: "bank",
  stageChatVisible: false,
  stageCameraShot: "bust",
};

const veraClosed: PresentationState = {
  presentationMode: "stage",
  stageChatVisible: false,
  stageCameraShot: "bust",
};

describe("isPhoneShellWidth", () => {
  it("is the phone shell at 767px and below, and desktop at 768px", () => {
    expect(isPhoneShellWidth(767)).toBe(true);
    expect(isPhoneShellWidth(0)).toBe(true);
    expect(isPhoneShellWidth(768)).toBe(false);
  });
});

describe("enterShellStage", () => {
  it("opens Vera with the transcript overlay closed", () => {
    expect(enterShellStage(chat)).toEqual(veraClosed);
    expect(enterShellStage(bank)).toEqual(veraClosed);
  });
});

describe("applyPhoneShellEntry", () => {
  it("sends memory, settings, usage, and extensions to chat without the avatar", () => {
    for (const view of ["memory", "settings", "usage", "extensions", "reminders", "activity", "briefing", "diary"] as const) {
      expect(
        applyPhoneShellEntry({ view, presentation: stageWaist }),
      ).toEqual({
        view: "chat",
        presentation: exitStage(stageWaist),
      });
    }
  });

  it("keeps Vera and the camera shot when the stage is already open", () => {
    expect(
      applyPhoneShellEntry({ view: "chat", presentation: stageWaist }),
    ).toEqual({
      view: "chat",
      presentation: { ...stageWaist, stageChatVisible: false },
    });
    expect(
      applyPhoneShellEntry({ view: "chat", presentation: stageFull }),
    ).toEqual({
      view: "chat",
      presentation: stageFull,
    });
  });

  it("opens Vera with the overlay closed from column chat or the bank", () => {
    expect(applyPhoneShellEntry({ view: "chat", presentation: chat })).toEqual({
      view: "chat",
      presentation: veraClosed,
    });
    expect(applyPhoneShellEntry({ view: "chat", presentation: bank })).toEqual({
      view: "chat",
      presentation: veraClosed,
    });
  });
});

describe("coercePhoneShellConfirmation", () => {
  it("leaves stage and bank for chat, and leaves chat unchanged", () => {
    expect(coercePhoneShellConfirmation(stageWaist)).toEqual(exitStage(stageWaist));
    expect(coercePhoneShellConfirmation(bank)).toEqual(exitStage(bank));
    expect(coercePhoneShellConfirmation(chat)).toEqual(chat);
  });
});
