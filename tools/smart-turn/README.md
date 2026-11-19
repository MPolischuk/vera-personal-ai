# Smart Turn v3.2 — medición offline

Compara, sobre los WAV de V2-04.1, el detector de energía a 800 ms con Smart Turn v3.2 (ONNX de CPU). El modelo no transcribe y no ve el texto. El peso no se commitea.

## Pesos y marcas

Los dos viven fuera del repo, al lado de la muestra:

- `<sample-dir>\smart-turn-v3.2-cpu.onnx` — `pipecat-ai/smart-turn-v3`, archivo `smart-turn-v3.2-cpu.onnx` (~8 MB, int8).
- `<sample-dir>\turn-marks.json` — por frase, `endMs` y `pausesMs: [[inicio, fin], …]`.

Una pausa es un silencio continuo (RMS &lt; 0,015) de al menos 200 ms, después de que la frase empezó y antes del silencio final. En esta muestra los huecos de palabra quedan en 80–160 ms. `endMs` es el instante en que empieza ese silencio final.

## Cómo correr

Hace falta Python con `numpy`, `onnxruntime` y `transformers` (el preproceso es `WhisperFeatureExtractor`, log-mel de 80 bandas, ventana de 8 s).

```powershell
python tools/smart-turn/measure.py --wav-dir <sample-dir> --marks <sample-dir>\turn-marks.json --model <sample-dir>\smart-turn-v3.2-cpu.onnx
```

## Regla que aplica el script

Frames de 20 ms, igual que el worklet. RMS sobre el PCM en [−1, 1], umbral 0,015. El turno se arma cuando el tramo desde el primer frame de habla hasta el último llega a 180 ms (la misma compuerta que `createEnergyEndpointDetector`). A los 250 ms de silencio se consulta el modelo con el audio del turno hasta ese instante: como máximo los últimos 8 s, con ceros al inicio. Si la probabilidad de completo es mayor que 0,5, cierra. Si no, a los 800 ms cierra igual. Un corte prematuro es ese cierre con el reloj dentro de una pausa marcada. El hangover es el cierre menos `endMs`.

## Tabla (esta muestra)

| Frase | Cortes energía 800 ms | Cortes Smart Turn | Hangover energía (ms) | Hangover Smart Turn (ms) |
|---|---:|---:|---:|---:|
| 01 | 0 | 0 | 780 | 240 |
| 02 | 0 | 1 | 780 | -300 |
| 03 | 0 | 1 | 780 | -980 |
| 04 | 0 | 0 | 780 | 240 |
| 05 | 0 | 1 | 780 | -1520 |
| 06 | 0 | 0 | 780 | 240 |
| 07 | 0 | 0 | 780 | 240 |
| 08 | 0 | 0 | 1180 | 240 |
| Total | 0 | 3 | 780 | 240 |

No-go: 3 cortes no es menos que 0. El cliente no cambia.
