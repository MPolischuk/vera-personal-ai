import { getSharedAudioContext, resumeSharedAudioContext } from "../../lib/audioContext";

export interface MicFrame {
  pcm: Uint8Array;
  rms: number;
  tMs: number;
}

export type MicFrameHandler = (frame: MicFrame) => void;

const WORKLET_URL = "/voice/pcmCapture.worklet.js";
const PROCESSOR_NAME = "pcm-capture";

let mediaStream: MediaStream | null = null;
let workletNode: AudioWorkletNode | null = null;
let sourceNode: MediaStreamAudioSourceNode | null = null;
let silentGain: GainNode | null = null;
let frameHandler: MicFrameHandler | null = null;
let workletLoaded = false;

async function ensureWorklet(ctx: AudioContext): Promise<void> {
  if (workletLoaded) {
    return;
  }
  await ctx.audioWorklet.addModule(WORKLET_URL);
  workletLoaded = true;
}

/**
 * Start microphone capture. Requires a user gesture (getUserMedia + resume).
 * Does not request the mic on page load (PT-14 D8).
 */
export async function startMicCapture(onFrame: MicFrameHandler): Promise<void> {
  if (mediaStream) {
    frameHandler = onFrame;
    return;
  }

  if (typeof window !== "undefined" && !window.isSecureContext) {
    throw new Error(
      "El micrófono requiere un contexto seguro (localhost o HTTPS).",
    );
  }

  frameHandler = onFrame;
  const ctx = await resumeSharedAudioContext();
  await ensureWorklet(ctx);

  mediaStream = await navigator.mediaDevices.getUserMedia({
    audio: {
      echoCancellation: true,
      // Browser NS eats unstressed Spanish syllables; Deepgram handles noise.
      noiseSuppression: false,
      autoGainControl: true,
      channelCount: 1,
    },
  });

  sourceNode = ctx.createMediaStreamSource(mediaStream);
  workletNode = new AudioWorkletNode(ctx, PROCESSOR_NAME);
  workletNode.port.onmessage = (event: MessageEvent) => {
    const data = event.data as {
      type: string;
      pcm: ArrayBuffer;
      rms: number;
      tMs: number;
    };
    if (data?.type !== "frame" || !frameHandler) {
      return;
    }
    frameHandler({
      pcm: new Uint8Array(data.pcm),
      rms: data.rms,
      tMs: data.tMs,
    });
  };

  sourceNode.connect(workletNode);
  // Keep the graph alive without feeding the mic to the speakers.
  silentGain = ctx.createGain();
  silentGain.gain.value = 0;
  workletNode.connect(silentGain);
  silentGain.connect(ctx.destination);
}

export async function stopMicCapture(): Promise<void> {
  frameHandler = null;

  if (workletNode) {
    try {
      workletNode.port.onmessage = null;
      workletNode.disconnect();
    } catch {
      /* ignore */
    }
    workletNode = null;
  }

  if (silentGain) {
    try {
      silentGain.disconnect();
    } catch {
      /* ignore */
    }
    silentGain = null;
  }

  if (sourceNode) {
    try {
      sourceNode.disconnect();
    } catch {
      /* ignore */
    }
    sourceNode = null;
  }

  if (mediaStream) {
    for (const track of mediaStream.getTracks()) {
      track.stop();
    }
    mediaStream = null;
  }
}

export function isMicCaptureActive(): boolean {
  return mediaStream !== null;
}

/** Expose shared context for callers that only need resume (tests / playback). */
export { getSharedAudioContext, resumeSharedAudioContext };
