import { create } from "zustand";
import { connectHub, disconnectHub, sendEnvelope } from "../api/hub";
import { hasSession as hasAccessToken } from "../api/session";
import {
  createEnvelope,
  MessageTypes,
  type AvatarExpressionPayload,
  type AvatarGesturePayload,
  type AvatarStatePayload,
  type AvatarVisualState,
  type ConfirmationRequestPayload,
  type Envelope,
  type ErrorPayload,
  type TokenPayload,
  type ToolCompletedPayload,
  type ToolInvokedPayload,
  type TranscriptionPayload,
  type TurnCompletedPayload,
  type TurnStartedPayload,
} from "../api/protocol";
import {
  createConversation as apiCreateConversation,
  deleteAllConversations as apiDeleteAllConversations,
  deleteConversation as apiDeleteConversation,
  getMessages,
  getPersonality,
  getUsage,
  listConversations,
  type ConversationDto,
  type MessageDto,
  type PersonalityDto,
  type UsageDto,
} from "../api/rest";
import {
  bindVoiceController,
  handleSpeechUnrecognized,
  interruptPlaybackAndTurn,
  isPlaybackEmpty,
  notifyPlaybackEmpty,
  type VoiceStatus,
} from "../features/voice/voiceController";
import {
  applyPhoneShellEntry,
  coercePhoneShellConfirmation,
  enterShellStage,
} from "../shell/phoneShell";
import {
  onExpressionCue,
  onGestureCue,
  onTurnCompleted as onCueTurnCompleted,
  onTurnFailed,
  onTurnStarted as onCueTurnStarted,
  type CharacterExpressionHold,
} from "./avatarCueLifetime";
import {
  admitActiveTool,
  completeActiveTool,
  dropActiveTool,
  markToolAlert,
  TOOL_ALERT_MS,
  type ActiveTool,
} from "../features/avatar/toolIcon";
import {
  enterBank as enterBankPure,
  enterStage as enterStagePure,
  exitStage as exitStagePure,
  initialPresentationState,
  onAppViewChange,
  onPendingConfirmation,
  pickPresentation,
  stepStageCameraShot as stepStageCameraShotPure,
  toggleStageChat as toggleStageChatPure,
  type CameraShot,
  type CameraShotDirection,
  type PresentationMode,
} from "./presentation";

export type ConnectionStatus = "idle" | "connecting" | "connected" | "error";
export type AppView = "chat" | "settings" | "usage" | "memory" | "extensions" | "reminders" | "activity" | "briefing" | "diary";
export type { PresentationMode };
export type { VoiceStatus };
export type { AvatarVisualState };

export interface AvatarExpressionCue {
  expression: string;
  weight: number;
  durationMs: number;
}

export interface AvatarGestureCue {
  name: string;
  intensity: number;
  durationMs: number;
  playId: string;
}

export type { CharacterExpressionHold };

const AVATAR_GESTURE_WHITELIST = new Set(["nod", "shake", "wave", "shrug"]);

export interface PendingConfirmation {
  confirmationId: string;
  toolName: string;
  prompt: string;
  expiresAt: string;
  conversationId: string;
}

export interface ClientError {
  code: string;
  detail: string;
}

interface AppState {
  view: AppView;
  phoneShell: boolean;
  presentationMode: PresentationMode;
  stageChatVisible: boolean;
  stageCameraShot: CameraShot;
  connectionStatus: ConnectionStatus;
  connectionError: string | null;
  conversations: ConversationDto[];
  selectedConversationId: string | null;
  messages: MessageDto[];
  streamingText: string;
  turnActive: boolean;
  activeTurnConversationId: string | null;
  activeTurnId: string | null;
  lastTurnId: string | null;
  pendingConfirmation: PendingConfirmation | null;
  clientError: ClientError | null;
  personality: PersonalityDto | null;
  usage: UsageDto | null;
  loadingMessages: boolean;
  sending: boolean;
  voiceStatus: VoiceStatus;
  micEnabled: boolean;
  partialTranscript: string;
  avatarState: AvatarVisualState | null;
  avatarExpression: AvatarExpressionCue | null;
  characterExpression: CharacterExpressionHold | null;
  avatarGesture: AvatarGestureCue | null;
  /** At most two tools, oldest first. The stage reads this; it does not import the store. */
  activeTools: readonly ActiveTool[];

  setView: (view: AppView) => void;
  setPhoneShell: (active: boolean) => void;
  enterPhoneVera: () => void;
  showPhoneChat: () => void;
  enterStage: () => void;
  enterBank: () => void;
  exitStage: () => void;
  toggleStageChat: () => void;
  stepStageCameraShot: (direction: CameraShotDirection) => void;
  clearClientError: () => void;
  connect: () => Promise<void>;
  loadConversations: () => Promise<void>;
  selectConversation: (id: string) => Promise<void>;
  createConversation: (title?: string | null) => Promise<ConversationDto>;
  deleteConversation: (id: string) => Promise<void>;
  deleteAllConversations: () => Promise<void>;
  sendMessage: (text: string) => Promise<void>;
  interrupt: () => Promise<void>;
  respondToConfirmation: (approved: boolean) => Promise<void>;
  loadPersonality: () => Promise<void>;
  loadUsage: () => Promise<void>;
  setVoiceStatus: (status: VoiceStatus) => void;
  setMicEnabled: (enabled: boolean) => void;
  setPartialTranscript: (text: string) => void;
}

const errorCopy: Record<string, string> = {
  unsupported_message_type: "El servidor rechazó un tipo de mensaje.",
  unsupported_protocol_version: "Versión de protocolo no soportada.",
  invalid_payload: "El mensaje enviado no es válido.",
  confirmation_pending: "Hay una confirmación pendiente.",
  turn_in_progress: "Ya hay un turno en curso.",
  conversation_not_found: "No se encontró la conversación.",
  model_unavailable: "El modelo no está disponible.",
  voice_unavailable: "La voz no está disponible en el servidor.",
  speech_unrecognized: "No se entendió lo que dijiste. Probá de nuevo.",
  stt_unavailable: "El reconocimiento de voz no está disponible.",
  stt_in_progress: "Ya hay una captura de voz en curso.",
};

function spanishError(code: string): string {
  return errorCopy[code] ?? "Ocurrió un error en el asistente.";
}

function buildHandlers(get: () => AppState, set: (partial: Partial<AppState>) => void) {
  return {
    [MessageTypes.TurnStarted]: (envelope: Envelope) => {
      const payload = envelope.payload as TurnStartedPayload;
      const cues = onCueTurnStarted({
        characterExpression: get().characterExpression,
        avatarGesture: get().avatarGesture,
      });
      set({
        turnActive: true,
        activeTurnConversationId: payload.conversationId,
        activeTurnId: payload.turnId,
        lastTurnId: payload.turnId,
        streamingText:
          get().selectedConversationId === payload.conversationId
            ? ""
            : get().streamingText,
        clientError: null,
        partialTranscript: "",
        pendingConfirmation: null,
        ...cues,
      });
    },
    [MessageTypes.Token]: (envelope: Envelope) => {
      const payload = envelope.payload as TokenPayload;
      const { selectedConversationId, activeTurnConversationId, streamingText } = get();
      if (
        selectedConversationId &&
        activeTurnConversationId &&
        selectedConversationId === activeTurnConversationId
      ) {
        set({ streamingText: streamingText + (payload.text ?? "") });
      }
    },
    [MessageTypes.Transcription]: (envelope: Envelope) => {
      const payload = envelope.payload as TranscriptionPayload;
      if (payload.isFinal) {
        set({ partialTranscript: payload.text ?? "" });
      } else {
        set({ partialTranscript: payload.text ?? "" });
      }
    },
    [MessageTypes.ConfirmationRequest]: (envelope: Envelope) => {
      const payload = envelope.payload as ConfirmationRequestPayload;
      const conversationId =
        get().activeTurnConversationId ?? get().selectedConversationId ?? "";
      const presentation = get().phoneShell
        ? coercePhoneShellConfirmation(pickPresentation(get()))
        : onPendingConfirmation(pickPresentation(get()), true);
      set({
        pendingConfirmation: {
          confirmationId: payload.confirmationId,
          toolName: payload.toolName,
          prompt: payload.prompt,
          expiresAt: payload.expiresAt,
          conversationId,
        },
        ...presentation,
      });
    },
    [MessageTypes.TurnCompleted]: async (envelope: Envelope) => {
      const payload = envelope.payload as TurnCompletedPayload;
      const conversationId = get().activeTurnConversationId;
      const keepTurnIdForPlayback = !isPlaybackEmpty();
      const cues = onCueTurnCompleted(
        {
          characterExpression: get().characterExpression,
          avatarGesture: get().avatarGesture,
        },
        payload.outcome,
      );

      set({
        turnActive: false,
        activeTurnConversationId: null,
        activeTurnId: null,
        lastTurnId: keepTurnIdForPlayback ? get().lastTurnId : null,
        streamingText: "",
        sending: false,
        partialTranscript: "",
        pendingConfirmation:
          payload.outcome === "AwaitingConfirmation"
            ? get().pendingConfirmation
            : null,
        ...cues,
      });

      if (
        conversationId &&
        get().selectedConversationId === conversationId
      ) {
        try {
          const messages = await getMessages(conversationId);
          set({ messages });
        } catch {
          /* keep previous messages */
        }
      }

      await get().loadConversations();

      if (isPlaybackEmpty() && get().voiceStatus !== "capturing" && !get().pendingConfirmation) {
        set({ voiceStatus: "off" });
      }
    },
    [MessageTypes.Error]: (envelope: Envelope) => {
      const payload = envelope.payload as ErrorPayload;
      const cues = onTurnFailed({
        characterExpression: get().characterExpression,
        avatarGesture: get().avatarGesture,
      });
      set({
        clientError: {
          code: payload.code,
          detail: spanishError(payload.code),
        },
        turnActive: false,
        sending: false,
        streamingText: "",
        ...cues,
      });

      if (payload.code === "speech_unrecognized") {
        handleSpeechUnrecognized();
      }
    },
    [MessageTypes.ToolInvoked]: (envelope: Envelope) => {
      const payload = envelope.payload as ToolInvokedPayload;
      const name = payload?.toolName;
      if (typeof name !== "string") {
        return;
      }
      const next = admitActiveTool(get().activeTools, name);
      if (next !== get().activeTools) {
        set({ activeTools: next });
      }
    },
    [MessageTypes.ToolCompleted]: (envelope: Envelope) => {
      const payload = envelope.payload as ToolCompletedPayload;
      const name = payload?.toolName;
      if (typeof name !== "string" || !name) {
        return;
      }
      if (payload.succeeded === false) {
        const marked = markToolAlert(get().activeTools, name);
        if (marked.tools !== get().activeTools) {
          set({ activeTools: marked.tools });
        }
        if (marked.id !== null && !marked.already) {
          scheduleToolAlertDrop(marked.id, get, set);
        }
        return;
      }
      const previous = get().activeTools;
      const next = completeActiveTool(previous, name);
      if (next === previous) {
        return;
      }
      for (const tool of previous) {
        if (!next.some((item) => item.id === tool.id)) {
          clearToolAlertTimer(tool.id);
        }
      }
      set({ activeTools: next });
    },
    [MessageTypes.AvatarState]: (envelope: Envelope) => {
      const payload = envelope.payload as AvatarStatePayload;
      const state = payload?.state;
      if (
        state !== "idle" &&
        state !== "listening" &&
        state !== "thinking" &&
        state !== "speaking"
      ) {
        return;
      }
      set({ avatarState: state });
    },
    [MessageTypes.AvatarExpression]: (envelope: Envelope) => {
      const payload = envelope.payload as AvatarExpressionPayload;
      const expression = payload?.expression;
      const weight = payload?.weight;
      const durationMs = payload?.durationMs;
      if (
        typeof expression !== "string" ||
        !expression ||
        typeof weight !== "number" ||
        !Number.isFinite(weight) ||
        typeof durationMs !== "number" ||
        !Number.isFinite(durationMs)
      ) {
        return;
      }
      const cue = { expression, weight, durationMs };
      const cues = onExpressionCue(
        {
          characterExpression: get().characterExpression,
          avatarGesture: get().avatarGesture,
        },
        cue,
        envelope.correlationId,
      );
      set({
        avatarExpression: cue,
        ...cues,
      });
    },
    [MessageTypes.AvatarGesture]: (envelope: Envelope) => {
      const payload = envelope.payload as AvatarGesturePayload;
      const gesture = payload?.gesture;
      const intensity = payload?.intensity;
      const durationMs = payload?.durationMs;
      if (
        typeof gesture !== "string" ||
        !AVATAR_GESTURE_WHITELIST.has(gesture) ||
        typeof intensity !== "number" ||
        !Number.isFinite(intensity) ||
        typeof durationMs !== "number" ||
        !Number.isFinite(durationMs)
      ) {
        return;
      }
      const cues = onGestureCue(
        {
          characterExpression: get().characterExpression,
          avatarGesture: get().avatarGesture,
        },
        {
          name: gesture,
          intensity,
          durationMs,
          playId: envelope.correlationId,
        },
      );
      set(cues);
    },
  };
}

const toolAlertTimers = new Map<number, ReturnType<typeof setTimeout>>();

function clearToolAlertTimer(id: number): void {
  const existing = toolAlertTimers.get(id);
  if (existing === undefined) {
    return;
  }
  clearTimeout(existing);
  toolAlertTimers.delete(id);
}

function scheduleToolAlertDrop(
  id: number,
  get: () => AppState,
  set: (partial: Partial<AppState>) => void,
): void {
  clearToolAlertTimer(id);
  const handle = setTimeout(() => {
    toolAlertTimers.delete(id);
    set({ activeTools: dropActiveTool(get().activeTools, id) });
  }, TOOL_ALERT_MS);
  toolAlertTimers.set(id, handle);
}

function clearOpenConversation(set: (partial: Partial<AppState>) => void) {
  set({
    selectedConversationId: null,
    messages: [],
    streamingText: "",
    pendingConfirmation: null,
    partialTranscript: "",
    loadingMessages: false,
  });
}

export const useAppStore = create<AppState>((set, get) => {
  bindVoiceController(() => ({
    voiceStatus: get().voiceStatus,
    micEnabled: get().micEnabled,
    turnActive: get().turnActive,
    partialTranscript: get().partialTranscript,
    activeTurnId: get().activeTurnId,
    lastTurnId: get().lastTurnId,
    confirmationPending: get().pendingConfirmation !== null,
    setVoiceStatus: (status) => set({ voiceStatus: status }),
    setMicEnabled: (enabled) => set({ micEnabled: enabled }),
    setPartialTranscript: (text) => set({ partialTranscript: text }),
    ensureConversationForVoice: async () => {
      let conversationId = get().selectedConversationId;
      if (!conversationId) {
        const created = await get().createConversation(null);
        conversationId = created.id;
      }
      return conversationId;
    },
    onPlaybackEmpty: () => {
      notifyPlaybackEmpty();
      if (!get().turnActive) {
        set({ lastTurnId: null, activeTurnId: null });
      }
    },
    onSpeechUnrecognized: () => handleSpeechUnrecognized(),
  }));

  return {
    view: "chat",
    phoneShell: false,
    ...initialPresentationState,
    connectionStatus: "idle",
    connectionError: null,
    conversations: [],
    selectedConversationId: null,
    messages: [],
    streamingText: "",
    turnActive: false,
    activeTurnConversationId: null,
    activeTurnId: null,
    lastTurnId: null,
    pendingConfirmation: null,
    clientError: null,
    personality: null,
    usage: null,
    loadingMessages: false,
    sending: false,
    voiceStatus: "off",
    micEnabled: false,
    partialTranscript: "",
    avatarState: null,
    avatarExpression: null,
    characterExpression: null,
    avatarGesture: null,
    activeTools: [],

    setView: (view) => {
      const presentation = onAppViewChange(pickPresentation(get()), view);
      set({ view, ...presentation });
    },
    setPhoneShell: (active) => {
      if (get().phoneShell === active) {
        return;
      }
      if (!active) {
        set({ phoneShell: false });
        return;
      }
      const entry = applyPhoneShellEntry({
        view: get().view,
        presentation: pickPresentation(get()),
      });
      set({
        phoneShell: true,
        view: entry.view,
        ...entry.presentation,
      });
    },
    enterPhoneVera: () =>
      set({
        view: "chat",
        ...enterShellStage(pickPresentation(get())),
      }),
    showPhoneChat: () =>
      set({
        view: "chat",
        ...exitStagePure(pickPresentation(get())),
      }),
    enterStage: () => set(enterStagePure(pickPresentation(get()))),
    enterBank: () =>
      set({
        ...enterBankPure(pickPresentation(get())),
        avatarGesture: null,
      }),
    exitStage: () => set(exitStagePure(pickPresentation(get()))),
    toggleStageChat: () => {
      const { stageChatVisible, pendingConfirmation } = get();
      if (pendingConfirmation && stageChatVisible) {
        return;
      }
      set(toggleStageChatPure(pickPresentation(get())));
    },
    stepStageCameraShot: (direction) =>
      set(stepStageCameraShotPure(pickPresentation(get()), direction)),
    clearClientError: () => set({ clientError: null }),
    setVoiceStatus: (status) => set({ voiceStatus: status }),
    setMicEnabled: (enabled) => set({ micEnabled: enabled }),
    setPartialTranscript: (text) => set({ partialTranscript: text }),

    connect: async () => {
      if (!hasAccessToken()) {
        return;
      }

      set({ connectionStatus: "connecting", connectionError: null });
      try {
        await connectHub(buildHandlers(get, set));
        set({ connectionStatus: "connected" });
        await get().loadConversations();
      } catch (error) {
        set({
          connectionStatus: "error",
          connectionError:
            error instanceof Error ? error.message : "No se pudo conectar al hub.",
        });
        await disconnectHub();
      }
    },

    loadConversations: async () => {
      const conversations = await listConversations();
      set({ conversations });
    },

    selectConversation: async (id) => {
      set({
        selectedConversationId: id,
        loadingMessages: true,
        streamingText: "",
        pendingConfirmation: null,
        partialTranscript: "",
        view: "chat",
      });
      try {
        const messages = await getMessages(id);
        set({ messages, loadingMessages: false });
      } catch (error) {
        set({
          loadingMessages: false,
          clientError: {
            code: "load_failed",
            detail:
              error instanceof Error
                ? error.message
                : "No se pudo cargar el historial.",
          },
        });
      }
    },

    createConversation: async (title) => {
      const conversation = await apiCreateConversation(title);
      await get().loadConversations();
      await get().selectConversation(conversation.id);
      return conversation;
    },

    deleteConversation: async (id) => {
      const wasSelected = get().selectedConversationId === id;
      if (wasSelected && (get().turnActive || get().voiceStatus === "playing")) {
        await get().interrupt();
      }

      try {
        await apiDeleteConversation(id);
        const conversations = get().conversations.filter((c) => c.id !== id);
        set({ conversations });
        if (wasSelected) {
          clearOpenConversation(set);
        }
      } catch (error) {
        set({
          clientError: {
            code: "delete_failed",
            detail:
              error instanceof Error
                ? error.message
                : "No se pudo eliminar la conversación.",
          },
        });
      }
    },

    deleteAllConversations: async () => {
      if (get().turnActive || get().voiceStatus === "playing") {
        await get().interrupt();
      }

      try {
        await apiDeleteAllConversations();
        set({ conversations: [] });
        clearOpenConversation(set);
      } catch (error) {
        set({
          clientError: {
            code: "delete_failed",
            detail:
              error instanceof Error
                ? error.message
                : "No se pudieron eliminar las conversaciones.",
          },
        });
      }
    },

    sendMessage: async (text) => {
      const trimmed = text.trim();
      if (!trimmed) {
        return;
      }

      const { pendingConfirmation, turnActive, sending, voiceStatus } = get();
      if (
        pendingConfirmation ||
        turnActive ||
        sending ||
        voiceStatus === "capturing"
      ) {
        return;
      }

      set({ sending: true, clientError: null });
      try {
        let conversationId = get().selectedConversationId;
        if (!conversationId) {
          const created = await get().createConversation(null);
          conversationId = created.id;
        }

        await sendEnvelope(
          createEnvelope(MessageTypes.UserMessage, {
            conversationId,
            text: trimmed,
          }),
        );
      } catch (error) {
        set({
          sending: false,
          clientError: {
            code: "send_failed",
            detail:
              error instanceof Error ? error.message : "No se pudo enviar el mensaje.",
          },
        });
      }
    },

    interrupt: async () => {
      const { turnActive, voiceStatus, activeTurnId, lastTurnId } = get();
      if (!turnActive && voiceStatus !== "playing" && !activeTurnId && !lastTurnId) {
        return;
      }
      await interruptPlaybackAndTurn();
    },

    respondToConfirmation: async (approved) => {
      const pending = get().pendingConfirmation;
      if (!pending || !pending.confirmationId) {
        return;
      }
      set({
        pendingConfirmation: null,
        clientError: null,
        sending: true,
      });
      try {
        await sendEnvelope(
          createEnvelope(MessageTypes.UserConfirmationResponse, {
            confirmationId: pending.confirmationId,
            approved,
          }),
        );
      } catch (error) {
        set({
          pendingConfirmation: pending,
          sending: false,
          clientError: {
            code: "send_failed",
            detail:
              error instanceof Error
                ? error.message
                : "No se pudo enviar la confirmación.",
          },
        });
      }
    },

    loadPersonality: async () => {
      const personality = await getPersonality();
      set({ personality });
    },

    loadUsage: async () => {
      const usage = await getUsage();
      set({ usage });
    },
  };
});
