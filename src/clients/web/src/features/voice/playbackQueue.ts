import { PCM_SAMPLE_RATE } from "../../lib/pcm";
import { getPlaybackInput } from "../../lib/playbackTap";

export interface PlaybackQueueHooks {
  onStart?: (sequence: number, playedChunks: number) => void;
  onEmpty?: () => void;
}

export interface PlaybackQueue {
  /** Enqueue a PCM s16le 16 kHz mono chunk. Resets when correlationId changes. */
  enqueue(sequence: number, pcm: Uint8Array, correlationId?: string | null): void;
  /** Stop sources, clear queue; returns playedChunks at interrupt time. */
  stop(): number;
  /** Drop everything and clear turn correlation. */
  reset(): void;
  getPlayedChunks(): number;
  isEmpty(): boolean;
  getCorrelationId(): string | null;
}

interface QueuedChunk {
  sequence: number;
  pcm: Uint8Array;
}

/**
 * FIFO playback ledger + AudioBufferSourceNode scheduler (PT-14 D6 / PT-19 D5).
 * Chunks are pre-scheduled against `nextTime` without waiting for `onended`.
 * `playedChunks = sequence + 1` when a chunk **starts**, not onended.
 */
export function createPlaybackQueue(
  getContext: () => AudioContext,
  hooks: PlaybackQueueHooks = {},
): PlaybackQueue {
  let queue: QueuedChunk[] = [];
  let nextTime = 0;
  let playedChunks = 0;
  let correlationId: string | null = null;
  const pendingSources = new Set<AudioBufferSourceNode>();

  function pcmToBuffer(ctx: AudioContext, pcm: Uint8Array): AudioBuffer {
    const sampleCount = Math.floor(pcm.byteLength / 2);
    const buffer = ctx.createBuffer(1, sampleCount, PCM_SAMPLE_RATE);
    const channel = buffer.getChannelData(0);
    const view = new DataView(pcm.buffer, pcm.byteOffset, pcm.byteLength);
    for (let i = 0; i < sampleCount; i++) {
      channel[i] = view.getInt16(i * 2, true) / 0x8000;
    }
    return buffer;
  }

  function pump(): void {
    while (queue.length > 0) {
      const item = queue.shift()!;
      const ctx = getContext();
      const buffer = pcmToBuffer(ctx, item.pcm);
      const source = ctx.createBufferSource();
      source.buffer = buffer;
      source.connect(getPlaybackInput(ctx));

      const startAt = Math.max(ctx.currentTime, nextTime);
      nextTime = startAt + buffer.duration;
      pendingSources.add(source);

      playedChunks = item.sequence + 1;
      hooks.onStart?.(item.sequence, playedChunks);

      source.onended = () => {
        pendingSources.delete(source);
        if (queue.length > 0) {
          pump();
        } else if (pendingSources.size === 0) {
          nextTime = 0;
          hooks.onEmpty?.();
        }
      };

      source.start(startAt);
    }
  }

  return {
    enqueue(sequence, pcm, nextCorrelationId) {
      if (
        nextCorrelationId != null &&
        correlationId != null &&
        nextCorrelationId !== correlationId
      ) {
        this.stop();
        playedChunks = 0;
      }
      if (nextCorrelationId != null) {
        correlationId = nextCorrelationId;
      }

      queue.push({ sequence, pcm });
      queue.sort((a, b) => a.sequence - b.sequence);
      pump();
    },

    stop() {
      for (const source of pendingSources) {
        try {
          source.onended = null;
          source.stop();
        } catch {
          /* already stopped */
        }
      }
      pendingSources.clear();
      queue = [];
      nextTime = 0;
      return playedChunks;
    },

    reset() {
      this.stop();
      playedChunks = 0;
      correlationId = null;
    },

    getPlayedChunks() {
      return playedChunks;
    },

    isEmpty() {
      return queue.length === 0 && pendingSources.size === 0;
    },

    getCorrelationId() {
      return correlationId;
    },
  };
}

/**
 * Pure ledger for Vitest — records start callbacks without AudioContext.
 * Simulates D6: playedChunks updates when a chunk starts.
 */
export function createPlaybackLedger() {
  let playedChunks = 0;
  const started: number[] = [];

  return {
    get playedChunks() {
      return playedChunks;
    },
    start(sequence: number) {
      started.push(sequence);
      playedChunks = sequence + 1;
    },
    interrupt() {
      return playedChunks;
    },
    get started() {
      return started.slice();
    },
  };
}
