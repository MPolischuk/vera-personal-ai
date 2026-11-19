import { describe, expect, it } from "vitest";
import {
  enterBank,
  enterStage,
  exitStage,
  initialPresentationState,
  isViewportMode,
  onAppViewChange,
  onPendingConfirmation,
  stepStageCameraShot,
  toggleStageChat,
  type PresentationState,
} from "./presentation";

const chat: PresentationState = initialPresentationState;
const stageOpen: PresentationState = {
  presentationMode: "stage",
  stageChatVisible: true,
  stageCameraShot: "bust",
};
const stageHidden: PresentationState = {
  presentationMode: "stage",
  stageChatVisible: false,
  stageCameraShot: "bust",
};
const bank: PresentationState = {
  presentationMode: "bank",
  stageChatVisible: false,
  stageCameraShot: "bust",
};

describe("presentation helpers", () => {
  it("enterStage switches to stage with overlay visible", () => {
    expect(enterStage(chat)).toEqual(stageOpen);
    expect(enterStage(stageHidden)).toEqual(stageOpen);
    expect(enterStage(bank)).toEqual(stageOpen);
  });

  it("enterBank switches to bank with chat hidden", () => {
    expect(enterBank(chat)).toEqual(bank);
    expect(enterBank(stageOpen)).toEqual(bank);
    expect(enterBank(stageHidden)).toEqual(bank);
  });

  it("exitStage returns to chat column with overlay reset", () => {
    expect(exitStage(stageOpen)).toEqual(chat);
    expect(exitStage(stageHidden)).toEqual(chat);
    expect(exitStage(bank)).toEqual(chat);
  });

  it("isViewportMode is true for stage and bank", () => {
    expect(isViewportMode("chat")).toBe(false);
    expect(isViewportMode("stage")).toBe(true);
    expect(isViewportMode("bank")).toBe(true);
  });

  it("toggleStageChat flips overlay only while in stage", () => {
    expect(toggleStageChat(stageOpen)).toEqual(stageHidden);
    expect(toggleStageChat(stageHidden)).toEqual(stageOpen);
    expect(toggleStageChat(chat)).toEqual(chat);
    expect(toggleStageChat(bank)).toEqual(bank);
  });

  it("onAppViewChange resets stage/bank when leaving chat", () => {
    expect(onAppViewChange(stageHidden, "settings")).toEqual(chat);
    expect(onAppViewChange(stageOpen, "usage")).toEqual(chat);
    expect(onAppViewChange(bank, "settings")).toEqual(chat);
    expect(onAppViewChange(stageHidden, "memory")).toEqual(chat);
    expect(onAppViewChange(stageHidden, "extensions")).toEqual(chat);
    expect(onAppViewChange(stageHidden, "chat")).toEqual(stageHidden);
    expect(onAppViewChange(bank, "chat")).toEqual(bank);
  });

  it("onPendingConfirmation forces overlay in stage and leaves bank (D10)", () => {
    expect(onPendingConfirmation(stageHidden, true)).toEqual(stageOpen);
    expect(onPendingConfirmation(stageOpen, true)).toEqual(stageOpen);
    expect(onPendingConfirmation(stageHidden, false)).toEqual(stageHidden);
    expect(onPendingConfirmation(chat, true)).toEqual(chat);
    expect(onPendingConfirmation(bank, true)).toEqual(chat);
    expect(onPendingConfirmation(bank, false)).toEqual(bank);
  });

  it("stepStageCameraShot dollies in stage and in the bank", () => {
    const waist = stepStageCameraShot(stageOpen, "out");
    expect(waist.stageCameraShot).toBe("waist");
    expect(stepStageCameraShot(waist, "out").stageCameraShot).toBe("full");
    expect(stepStageCameraShot(chat, "out")).toEqual(chat);
    expect(stepStageCameraShot(bank, "out").stageCameraShot).toBe("waist");
    expect(exitStage(waist)).toEqual(chat);
    expect(enterStage(waist)).toEqual(stageOpen);
  });

  it("onPendingConfirmation keeps the current stage shot", () => {
    const hiddenFull: PresentationState = {
      presentationMode: "stage",
      stageChatVisible: false,
      stageCameraShot: "full",
    };
    expect(onPendingConfirmation(hiddenFull, true)).toEqual({
      presentationMode: "stage",
      stageChatVisible: true,
      stageCameraShot: "full",
    });
  });
});
