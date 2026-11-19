/**
 * Client-side heuristic that tags chat text with an intent for the avatar
 * director. No backend/protocol change: it reads only message text and role.
 * Spanish (rioplatense) first, with common English fallbacks.
 */
import type { IntentCue, SpeechIntent, UserIntent } from "../avatar/intentCue";

const fold = (text: string) =>
  text.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, "");

const USER_RULES: ReadonlyArray<[UserIntent, RegExp]> = [
  ["thanks", /\b(gracias|graci|mil gracias|thanks|thank you|thx|te agradezco)\b/],
  ["sad", /\b(triste|mal dia|me siento mal|deprimid\w*|problema|no anda|se rompio|fall[oó]|perd[ií]|murio|ayuda|me duele|angustia\w*)\b|:\(|😢|😭/],
  ["funny", /\b(ja(ja)+|je(je)+|lol|jaj\w*|xd|me mor[ií]|que gracioso)\b|😂|🤣/],
  ["surprise", /\b(no te puedo creer|en serio\?|increible|wow|guau|que\?!|no me digas|posta\?)\b|😮|😱|!\?|\?!/],
  ["goodNews", /\b(aprob[eé]|consegu[ií]|gan[eé]|me dieron|lo logr[eé]|buenas noticias|genial|excelente|salio bien|me ascendieron|lo hice)\b|🎉|🥳/],
  ["greeting", /^\s*(hola|holi|buenas|buen dia|buenos dias|buenas (tardes|noches)|hey|hi|hello|que tal|como va)\b/],
];

export function classifyUserText(text: string): UserIntent {
  const t = fold(text);
  for (const [intent, re] of USER_RULES) if (re.test(t)) return intent;
  return "message";
}

export function classifySpeechText(text: string): { kind: SpeechIntent; closing: boolean } {
  const t = fold(text);
  const listLines = text.split("\n").filter((l) => /^\s*(\d+[.)]|[-*•])\s+/.test(l)).length;
  let kind: SpeechIntent = "none";
  if (listLines >= 2 || /\b(primero|segundo|tercero)\b/.test(t)) kind = "list";
  else if (/\b(podes|podrias|o bien|opcion(es)?|alternativa\w*|por un lado|either|you could)\b/.test(t)) kind = "options";
  else if (/\b(no (lo )?se|no estoy segur\w*|ni idea|no tengo (forma|manera) de saber|i don'?t know|not sure)\b/.test(t)) kind = "unknown";
  else if (/\b(ahi|alla|a un costado|del otro lado|por otro lado|al lado|over there)\b/.test(t)) kind = "side";
  const tail = fold(text.trim().split(/\n/).pop() ?? "");
  const closing = /(avisame|cualquier cosa|espero que (te )?sirva|exitos|suerte|que te vaya bien|lo que necesites|let me know|hope this helps)/.test(tail);
  return { kind, closing };
}

export interface IntentMessage {
  id: string;
  role: string;
  content: string;
}

/** Cue for the newest finished message, keyed by its id so it plays once. */
export function messageIntentCue(message: IntentMessage | undefined): IntentCue | null {
  if (!message || !message.content.trim()) return null;
  if (message.role === "User") {
    return { playId: `u:${message.id}`, source: "user", kind: classifyUserText(message.content) };
  }
  const { kind, closing } = classifySpeechText(message.content);
  return { playId: `a:${message.id}`, source: "assistant", kind, closing };
}
