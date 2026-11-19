"""Offline Smart Turn v3.2 vs 800 ms energy on the V2-04.1 WAVs.

The model does not transcribe. Weights stay outside the repo.
"""

from __future__ import annotations

import argparse
import json
import wave
from pathlib import Path

import numpy as np
import onnxruntime as ort
from transformers import WhisperFeatureExtractor

SAMPLE_RATE = 16_000
FRAME_SAMPLES = 320  # 20 ms, same grid as pcmCapture.worklet.ts
FRAME_MS = 20
RMS_THRESHOLD = 0.015
MIN_SPEECH_MS = 180
TRIGGER_SILENCE_MS = 250
ENERGY_SILENCE_MS = 800
COMPLETE_THRESHOLD = 0.5
MODEL_SECONDS = 8
# Silence appended after each take so a trimmed WAV can still reach the cap.
TAIL_MS = 2000


def read_wav(path: Path) -> np.ndarray:
    with wave.open(str(path), "rb") as handle:
        channels = handle.getnchannels()
        width = handle.getsampwidth()
        rate = handle.getframerate()
        frames = handle.getnframes()
        if channels != 1 or width != 2 or rate != SAMPLE_RATE:
            raise ValueError(
                f"{path.name} must be mono 16 kHz s16le, got "
                f"channels={channels} width={width} rate={rate}"
            )
        pcm = np.frombuffer(handle.readframes(frames), dtype="<i2")
    audio = pcm.astype(np.float32) / 32768.0
    usable = (len(audio) // FRAME_SAMPLES) * FRAME_SAMPLES
    return audio[:usable]


def frame_rms(audio: np.ndarray) -> np.ndarray:
    frames = audio.reshape(-1, FRAME_SAMPLES)
    return np.sqrt(np.mean(frames * frames, axis=1))


def extend_with_silence(audio: np.ndarray) -> np.ndarray:
    tail = np.zeros(TAIL_MS * SAMPLE_RATE // 1000, dtype=np.float32)
    return np.concatenate([audio, tail])


def truncate_audio_to_last_n_seconds(
    audio: np.ndarray, n_seconds: int = MODEL_SECONDS
) -> np.ndarray:
    """Match pipecat-ai/smart-turn audio_utils: keep the end, pad the start."""
    max_samples = n_seconds * SAMPLE_RATE
    if len(audio) > max_samples:
        return audio[-max_samples:]
    if len(audio) < max_samples:
        pad = max_samples - len(audio)
        return np.pad(audio, (pad, 0), mode="constant")
    return audio


def build_session(model_path: Path) -> ort.InferenceSession:
    options = ort.SessionOptions()
    options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    options.inter_op_num_threads = 1
    options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    return ort.InferenceSession(str(model_path), sess_options=options)


def predict_complete(
    audio_until_now: np.ndarray,
    extractor: WhisperFeatureExtractor,
    session: ort.InferenceSession,
) -> float:
    window = truncate_audio_to_last_n_seconds(audio_until_now)
    features = extractor(
        window,
        sampling_rate=SAMPLE_RATE,
        return_tensors="np",
        padding="max_length",
        max_length=MODEL_SECONDS * SAMPLE_RATE,
        truncation=True,
        do_normalize=True,
    )
    model_input = features.input_features.squeeze(0).astype(np.float32)
    model_input = np.expand_dims(model_input, axis=0)
    outputs = session.run(None, {"input_features": model_input})
    return float(np.reshape(outputs[0], (-1,))[0])


def energy_close_ms(rms: np.ndarray) -> int:
    speech_started_at: int | None = None
    last_speech_at: int | None = None
    for index, value in enumerate(rms):
        t_ms = index * FRAME_MS
        if value >= RMS_THRESHOLD:
            if speech_started_at is None:
                speech_started_at = t_ms
            last_speech_at = t_ms
            continue
        if speech_started_at is None or last_speech_at is None:
            continue
        spoken_ms = last_speech_at - speech_started_at
        silence_for = t_ms - last_speech_at
        if spoken_ms >= MIN_SPEECH_MS and silence_for >= ENERGY_SILENCE_MS:
            return t_ms
    raise RuntimeError("Energy detector did not close; tail silence is too short.")


def smart_turn_close_ms(
    audio: np.ndarray,
    rms: np.ndarray,
    extractor: WhisperFeatureExtractor,
    session: ort.InferenceSession,
) -> tuple[int, list[dict[str, float | int]]]:
    speech_started_at: int | None = None
    last_speech_at: int | None = None
    queried_this_silence = False
    queries: list[dict[str, float | int]] = []
    for index, value in enumerate(rms):
        t_ms = index * FRAME_MS
        if value >= RMS_THRESHOLD:
            if speech_started_at is None:
                speech_started_at = t_ms
            last_speech_at = t_ms
            queried_this_silence = False
            continue
        if speech_started_at is None or last_speech_at is None:
            continue
        spoken_ms = last_speech_at - speech_started_at
        silence_for = t_ms - last_speech_at
        if spoken_ms < MIN_SPEECH_MS:
            continue
        if silence_for >= TRIGGER_SILENCE_MS and not queried_this_silence:
            queried_this_silence = True
            audio_until_now = audio[: (index + 1) * FRAME_SAMPLES]
            probability = predict_complete(audio_until_now, extractor, session)
            queries.append({"tMs": t_ms, "probability": round(probability, 4)})
            if probability > COMPLETE_THRESHOLD:
                return t_ms, queries
        if silence_for >= ENERGY_SILENCE_MS:
            return t_ms, queries
    raise RuntimeError("Smart Turn detector did not close; tail silence is too short.")


def during_pause(close_ms: int, pauses: list[list[int]]) -> bool:
    return any(start <= close_ms < end for start, end in pauses)


def median(values: list[int]) -> float:
    ordered = sorted(values)
    count = len(ordered)
    if count == 0:
        raise ValueError("median of an empty list")
    middle = count // 2
    if count % 2 == 1:
        return float(ordered[middle])
    return (ordered[middle - 1] + ordered[middle]) / 2.0


def load_marks(path: Path) -> list[dict]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    phrases = payload["phrases"]
    if len(phrases) != 8:
        raise ValueError(f"Expected 8 phrases, got {len(phrases)}")
    return phrases


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--wav-dir", type=Path, required=True)
    parser.add_argument("--marks", type=Path, required=True)
    parser.add_argument("--model", type=Path, required=True)
    args = parser.parse_args()

    if not args.model.is_file():
        raise SystemExit(f"ONNX not found: {args.model}")

    phrases = load_marks(args.marks)
    extractor = WhisperFeatureExtractor(chunk_length=MODEL_SECONDS)
    session = build_session(args.model)

    rows: list[dict] = []
    for phrase in phrases:
        phrase_id = str(phrase["id"])
        wav_path = args.wav_dir / f"{phrase_id}.wav"
        audio = extend_with_silence(read_wav(wav_path))
        rms = frame_rms(audio)
        pauses = phrase["pausesMs"]
        end_ms = int(phrase["endMs"])
        energy_ms = energy_close_ms(rms)
        smart_ms, queries = smart_turn_close_ms(audio, rms, extractor, session)
        rows.append(
            {
                "id": phrase_id,
                "energyCuts": int(during_pause(energy_ms, pauses)),
                "smartCuts": int(during_pause(smart_ms, pauses)),
                "energyHangoverMs": energy_ms - end_ms,
                "smartHangoverMs": smart_ms - end_ms,
                "energyCloseMs": energy_ms,
                "smartCloseMs": smart_ms,
                "queries": queries,
            }
        )

    energy_cuts = sum(row["energyCuts"] for row in rows)
    smart_cuts = sum(row["smartCuts"] for row in rows)
    energy_hang = median([row["energyHangoverMs"] for row in rows])
    smart_hang = median([row["smartHangoverMs"] for row in rows])

    print("| Frase | Cortes energía 800 ms | Cortes Smart Turn | Hangover energía (ms) | Hangover Smart Turn (ms) |")
    print("|---|---:|---:|---:|---:|")
    for row in rows:
        print(
            f"| {row['id']} | {row['energyCuts']} | {row['smartCuts']} | "
            f"{row['energyHangoverMs']} | {row['smartHangoverMs']} |"
        )
    print(
        f"| Total | {energy_cuts} | {smart_cuts} | {energy_hang:g} | {smart_hang:g} |"
    )
    decision = "go" if smart_cuts < energy_cuts else "no-go"
    print(f"decision: {decision}")
    for row in rows:
        print(
            f"close {row['id']} energy={row['energyCloseMs']} smart={row['smartCloseMs']} "
            f"queries={json.dumps(row['queries'])}"
        )


if __name__ == "__main__":
    main()
