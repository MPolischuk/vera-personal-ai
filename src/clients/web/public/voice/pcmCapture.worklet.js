/**
 * AudioWorkletProcessor: float input → 16 kHz s16le frames + RMS.
 * Keep in sync with src/features/voice/pcmCapture.worklet.ts
 */
const TARGET_RATE = 16000;
const FRAME_SAMPLES = 320; // 20 ms at 16 kHz
/**
 * Anti-alias cutoff, just under the 8 kHz Nyquist of TARGET_RATE. Decimating
 * 48 kHz → 16 kHz without it folds 8–24 kHz back over the speech band at full
 * amplitude: Rioplatense /ʃ/ ("sh" for ll/y) and /s/ carry most of their energy
 * there, and land on the vowel formants that carry word identity.
 */
const ANTIALIAS_HZ = 7000;
/** Q values of an 8th-order Butterworth lowpass as four cascaded biquads. */
const BUTTERWORTH_Q = [0.5098, 0.60134, 0.89998, 2.56292];

/** Direct-form-1 biquad, RBJ lowpass coefficients. */
class Biquad {
  constructor(cutoffHz, rate, q) {
    const w0 = (2 * Math.PI * cutoffHz) / rate;
    const cosW0 = Math.cos(w0);
    const alpha = Math.sin(w0) / (2 * q);
    const a0 = 1 + alpha;
    this.b0 = (1 - cosW0) / 2 / a0;
    this.b1 = (1 - cosW0) / a0;
    this.b2 = this.b0;
    this.a1 = (-2 * cosW0) / a0;
    this.a2 = (1 - alpha) / a0;
    this.x1 = 0;
    this.x2 = 0;
    this.y1 = 0;
    this.y2 = 0;
  }

  process(x) {
    const y =
      this.b0 * x +
      this.b1 * this.x1 +
      this.b2 * this.x2 -
      this.a1 * this.y1 -
      this.a2 * this.y2;
    this.x2 = this.x1;
    this.x1 = x;
    this.y2 = this.y1;
    this.y1 = y;
    return y;
  }
}

class PcmCaptureProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.ratio = sampleRate / TARGET_RATE;
    this.buffer = new Float32Array(FRAME_SAMPLES * 4);
    this.bufferFill = 0;
    /** Unconsumed source samples from the previous quantum. */
    this.carry = new Float32Array(0);
    /** Fractional position within `carry` / combined input. */
    this.phase = 0;
    const cutoff = Math.min(ANTIALIAS_HZ, sampleRate * 0.45);
    this.antiAlias = BUTTERWORTH_Q.map((q) => new Biquad(cutoff, sampleRate, q));
  }

  process(inputs) {
    const input = inputs[0] && inputs[0][0];
    if (!input || input.length === 0) {
      return true;
    }

    // Band-limit at the source rate; `carry` already holds filtered samples.
    const filtered = new Float32Array(input.length);
    for (let i = 0; i < input.length; i++) {
      let s = input[i];
      for (let k = 0; k < this.antiAlias.length; k++) {
        s = this.antiAlias[k].process(s);
      }
      filtered[i] = s;
    }

    const combined = new Float32Array(this.carry.length + filtered.length);
    combined.set(this.carry);
    combined.set(filtered, this.carry.length);

    let pos = this.phase;
    // Need one sample ahead for linear interpolation.
    while (pos + 1 < combined.length) {
      const i0 = Math.floor(pos);
      const i1 = i0 + 1;
      const frac = pos - i0;
      const sample = combined[i0] * (1 - frac) + combined[i1] * frac;
      this.buffer[this.bufferFill++] = sample;
      pos += this.ratio;

      if (this.bufferFill >= FRAME_SAMPLES) {
        this.emitFrame();
        this.bufferFill = 0;
      }
    }

    const keepFrom = Math.floor(pos);
    this.phase = pos - keepFrom;
    this.carry = combined.slice(keepFrom);
    return true;
  }

  emitFrame() {
    const frame = this.buffer.subarray(0, FRAME_SAMPLES);
    let sumSq = 0;
    const pcm = new Int16Array(FRAME_SAMPLES);
    for (let i = 0; i < FRAME_SAMPLES; i++) {
      let s = frame[i];
      if (!Number.isFinite(s)) {
        s = 0;
      }
      sumSq += s * s;
      // getUserMedia autoGainControl already sets the level; clamping is only a
      // guard against out-of-range input, not a makeup stage.
      s = Math.max(-1, Math.min(1, s));
      pcm[i] = s < 0 ? Math.round(s * 0x8000) : Math.round(s * 0x7fff);
    }
    const rms = Math.sqrt(sumSq / FRAME_SAMPLES);
    this.port.postMessage(
      { type: "frame", pcm: pcm.buffer, rms, tMs: currentTime * 1000 },
      [pcm.buffer],
    );
  }
}

registerProcessor("pcm-capture", PcmCaptureProcessor);
