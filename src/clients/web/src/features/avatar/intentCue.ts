/**
 * Message intent the director maps to body reactions. Comes from a client-side
 * heuristic over the chat text (see chat/messageIntent.ts); the backend
 * protocol is unchanged.
 */
export type UserIntent =
  | "message"
  | "greeting"
  | "thanks"
  | "goodNews"
  | "sad"
  | "funny"
  | "surprise";

export type SpeechIntent = "none" | "list" | "options" | "side" | "unknown";

export interface IntentCue {
  playId: string;
  source: "user" | "assistant";
  /** User reaction or the assistant's speech gesture. */
  kind: UserIntent | SpeechIntent;
  /** Assistant reply ends on a closing line: nod + open hand when speech ends. */
  closing?: boolean;
}

/** Which motion gesture each intent plays. */
export const USER_REACTION: Readonly<Record<UserIntent, string | null>> = {
  message: null,
  greeting: "bow",
  thanks: "handChest",
  goodNews: "clap",
  sad: "sadClasp",
  funny: "laugh",
  surprise: "surpriseStep",
};

export const SPEECH_GESTURE: Readonly<Record<SpeechIntent, string | null>> = {
  none: null,
  list: "count",
  options: "openPalms",
  side: "sidePoint",
  unknown: "shrug",
};
