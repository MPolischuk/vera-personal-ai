import { sendAudio, sendEnvelope, setAudioHandler } from "../../api/hub";
import {
  createEnvelope,
  MessageTypes,
  type AudioChunkHeader,
} from "../../api/protocol";
import { getSharedAudioContext, resumeSharedAudioContext } from "../../lib/audioContext";
import { startMicCapture, stopMicCapture, type MicFrame } from "./micCapture";
import { createPlaybackQueue, type PlaybackQueue } from "./playbackQueue";

export type VoiceStatus =
  | "off"
  | "listening"
  | "capturing"
  | "awaiting_turn"
  | "playing";

export interface VoiceStoreSlice {
  voiceStatus: VoiceStatus;
  micEnabled: boolean;
  turnActive: boolean;
  partialTranscript: string;
  activeTurnId: string | null;
  lastTurnId: string | null;
  confirmationPending: boolean;
  setVoiceStatus: (status: VoiceStatus) => void;
  setMicEnabled: (enabled: boolean) => void;
  setPartialTranscript: (text: string) => void;
  ensureConversationForVoice: () => Promise<string>;
  onPlaybackEmpty: () => void;
  onSpeechUnrecognized: () => void;
}

type GetStore = () => VoiceStoreSlice;

let getStore: GetStore | null = null;
let playback: PlaybackQueue | null = null;
let sendingFrames = false;
let conversationIdForSession: string | null = null;
let frameChain: Promise<void> = Promise.resolve();
const pendingPcm: Uint8Array[] = [];
/** 200 ms batches so SignalR is not one invoke per 20 ms frame. */
const PCM_BATCH_FRAMES = 10;

function enqueueMicFrame(frame: MicFrame): void {
  frameChain = frameChain
    .then(() => onMicFrame(frame))
    .catch((error) => {
      console.warn("Voice frame handling failed", error);
    });
}

async function drainMicFrames(): Promise<void> {
  await frameChain;
}

async function flushPcmBatch(): Promise<void> {
  if (pendingPcm.length === 0) {
    return;
  }
  let total = 0;
  for (const frame of pendingPcm) {
    total += frame.byteLength;
  }
  const pcm = new Uint8Array(total);
  let offset = 0;
  for (const frame of pendingPcm) {
    pcm.set(frame, offset);
    offset += frame.byteLength;
  }
  pendingPcm.length = 0;
  await sendPcm(pcm);
}

function queue(): PlaybackQueue {
  if (!playback) {
    playback = createPlaybackQueue(() => getSharedAudioContext(), {
      onStart: () => {
        getStore?.().setVoiceStatus("playing");
      },
      onEmpty: () => {
        getStore?.().onPlaybackEmpty();
      },
    });
  }
  return playback;
}

async function onMicFrame(frame: MicFrame): Promise<void> {
  const store = getStore?.();
  if (!store || store.confirmationPending) {
    sendingFrames = false;
    return;
  }

  if (store.voiceStatus === "capturing" && sendingFrames) {
    pendingPcm.push(frame.pcm);
    if (pendingPcm.length >= PCM_BATCH_FRAMES) {
      await flushPcmBatch();
    }
  }
}

async function sendPcm(pcm: Uint8Array): Promise<void> {
  const store = getStore?.();
  if (!store) {
    return;
  }

  if (!conversationIdForSession) {
    conversationIdForSession = await store.ensureConversationForVoice();
  }

  await sendAudio(conversationIdForSession, pcm);
}

async function endUtterance(): Promise<void> {
  const store = getStore?.();
  if (!store) {
    return;
  }

  // Stop capturing new frames first, then flush what is already queued so
  // Enviar does not CloseStream before the spoken PCM reaches the hub.
  await stopMicCapture();
  await drainMicFrames();
  await flushPcmBatch();

  sendingFrames = false;
  store.setMicEnabled(false);
  store.setVoiceStatus("awaiting_turn");
  await sendEnvelope(createEnvelope(MessageTypes.UserAudioEnd, {}));
}

/**
 * Stop local playback, await interrupt with turnId + playedChunks, then allow SendAudio.
 */
export async function performBargeIn(): Promise<number> {
  const store = getStore?.();
  const playedChunks = queue().stop();
  const turnId = store?.activeTurnId ?? store?.lastTurnId ?? null;

  if (turnId) {
    await sendEnvelope(
      createEnvelope(
        MessageTypes.UserInterrupt,
        { playedChunks },
        turnId,
      ),
    );
  }

  return playedChunks;
}

/** Interrupt button: stop + interrupt without opening capture. */
export async function interruptPlaybackAndTurn(): Promise<void> {
  const store = getStore?.();
  const hadPlayback = !queue().isEmpty() || store?.voiceStatus === "playing";
  const turnId = store?.activeTurnId ?? store?.lastTurnId ?? null;

  let playedChunks = 0;
  if (hadPlayback || turnId) {
    playedChunks = queue().stop();
  }

  if (!turnId) {
    return;
  }

  await sendEnvelope(
    createEnvelope(
      MessageTypes.UserInterrupt,
      hadPlayback ? { playedChunks } : {},
      turnId,
    ),
  );

  sendingFrames = false;
  store?.setMicEnabled(false);
  if (store && store.voiceStatus === "capturing") {
    store.setVoiceStatus("off");
    await stopMicCapture();
  }
}

function onReceiveAudio(header: AudioChunkHeader, pcm: Uint8Array): void {
  void resumeSharedAudioContext().then(() => {
    queue().enqueue(header.sequence, pcm, header.correlationId);
    getStore?.().setVoiceStatus("playing");
  });
}

export function bindVoiceController(get: GetStore): void {
  getStore = get;
  setAudioHandler(onReceiveAudio);
}

/** Tap-to-talk: start sending PCM until the user taps Enviar. */
export async function startRecording(): Promise<void> {
  const store = getStore?.();
  if (!store) {
    throw new Error("Voice controller is not bound.");
  }

  if (store.confirmationPending || store.voiceStatus === "capturing") {
    return;
  }

  if (typeof window !== "undefined" && !window.isSecureContext) {
    throw new Error(
      "El micrófono requiere un contexto seguro: abrí la app en localhost o con HTTPS (npm run dev:https).",
    );
  }

  if (
    store.voiceStatus === "playing" ||
    store.turnActive ||
    !queue().isEmpty()
  ) {
    await interruptPlaybackAndTurn();
  }

  await resumeSharedAudioContext();
  conversationIdForSession = await store.ensureConversationForVoice();
  pendingPcm.length = 0;
  sendingFrames = true;
  store.setMicEnabled(true);
  store.setPartialTranscript("");
  store.setVoiceStatus("capturing");
  await startMicCapture((frame) => {
    enqueueMicFrame(frame);
  });
}

/** Tap-to-send: close the STT stream so the server starts the turn. */
export async function stopRecordingAndSend(): Promise<void> {
  const store = getStore?.();
  if (!store || store.voiceStatus !== "capturing") {
    return;
  }
  await endUtterance();
}

export async function disableMicrophone(): Promise<void> {
  const store = getStore?.();
  const wasCapturing = store?.voiceStatus === "capturing";
  sendingFrames = false;
  conversationIdForSession = null;
  if (wasCapturing) {
    await sendEnvelope(createEnvelope(MessageTypes.UserAudioEnd, {}));
  }
  await stopMicCapture();
  store?.setMicEnabled(false);
  store?.setPartialTranscript("");
  store?.setVoiceStatus("off");
}

export async function toggleRecording(): Promise<void> {
  const store = getStore?.();
  if (!store) {
    return;
  }
  if (store.voiceStatus === "capturing") {
    await stopRecordingAndSend();
  } else {
    await startRecording();
  }
}

export function notifyPlaybackEmpty(): void {
  const store = getStore?.();
  if (!store) {
    return;
  }
  if (store.voiceStatus === "capturing") {
    return;
  }
  store.setVoiceStatus("off");
  sendingFrames = false;
}

export function getPlaybackPlayedChunks(): number {
  return queue().getPlayedChunks();
}

export function isPlaybackEmpty(): boolean {
  return queue().isEmpty();
}

export function resetPlaybackQueue(): void {
  queue().reset();
}

export function handleSpeechUnrecognized(): void {
  sendingFrames = false;
  const store = getStore?.();
  store?.setMicEnabled(false);
  if (store && store.voiceStatus !== "playing") {
    store.setVoiceStatus("off");
    store.setPartialTranscript("");
  }
}
