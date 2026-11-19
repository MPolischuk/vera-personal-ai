/** Wire envelope and stage-1a/1b message types (protocol v1). */

export const PROTOCOL_VERSION = 1 as const;

export const MessageTypes = {
  UserMessage: "user.message",
  UserInterrupt: "user.interrupt",
  UserConfirmationResponse: "user.confirmation_response",
  UserAudioEnd: "user.audio_end",
  NotificationAck: "user.notification_ack",
  NotificationAction: "user.notification_action",
  TurnStarted: "assistant.turn_started",
  Token: "assistant.token",
  TurnCompleted: "assistant.turn_completed",
  ConfirmationRequest: "assistant.confirmation_request",
  Transcription: "assistant.transcription",
  Error: "assistant.error",
  Notification: "assistant.notification",
  ToolInvoked: "tool.invoked",
  ToolCompleted: "tool.completed",
  AvatarState: "avatar.state",
  AvatarExpression: "avatar.expression",
  AvatarGesture: "avatar.gesture",
} as const;

export type MessageType = (typeof MessageTypes)[keyof typeof MessageTypes] | string;

export interface Envelope<TPayload = unknown> {
  v: number;
  type: MessageType;
  correlationId: string;
  timestamp: string;
  payload: TPayload;
}

export interface UserMessagePayload {
  conversationId: string;
  text: string;
}

export interface UserInterruptPayload {
  playedChunks?: number;
}

export interface UserConfirmationResponsePayload {
  confirmationId: string;
  approved: boolean;
}

export interface TurnStartedPayload {
  turnId: string;
  conversationId: string;
}

export interface TokenPayload {
  text: string;
}

export interface TurnCompletedPayload {
  messageId: string | null;
  outcome: string;
  durationMs: number;
}

export interface ConfirmationRequestPayload {
  confirmationId: string;
  toolName: string;
  prompt: string;
  expiresAt: string;
}

export interface TranscriptionPayload {
  text: string;
  isFinal: boolean;
}

export interface ErrorPayload {
  code: string;
  message: string;
}

export interface NotificationActionPayload {
  actionId: string;
  label: string;
}

export interface NotificationPayload {
  notificationId: string;
  deliveryId: string;
  kind: string;
  title: string;
  body: string;
  actions: NotificationActionPayload[];
  conversationId?: string | null;
  createdAt: string;
}

export interface ToolInvokedPayload {
  toolName: string;
}

export interface ToolCompletedPayload {
  toolName: string;
  outcome: string;
  succeeded: boolean;
}

export type AvatarVisualState = "idle" | "listening" | "thinking" | "speaking";

export interface AvatarStatePayload {
  state: AvatarVisualState;
}

export interface AvatarExpressionPayload {
  expression: string;
  weight: number;
  durationMs: number;
}

export interface AvatarGesturePayload {
  gesture: string;
  intensity: number;
  durationMs: number;
}

/** Header of ReceiveAudio (JSON camelCase from the hub). */
export interface AudioChunkHeader {
  v: number;
  type: string;
  correlationId: string | null;
  timestamp: string;
  sequence: number;
}

export function createEnvelope<TPayload>(
  type: MessageType,
  payload: TPayload,
  correlationId: string = crypto.randomUUID(),
): Envelope<TPayload> {
  return {
    v: PROTOCOL_VERSION,
    type,
    correlationId,
    timestamp: new Date().toISOString(),
    payload,
  };
}
