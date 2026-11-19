# Bake-off STT rioplatense

Mide WER, WER de entidades y latencia al texto final. No lo referencia `Assistant.Api`. No crea un endpoint. No imprime claves.

La muestra de oro no vive en este repo: WAV mono, 16 kHz, s16le, `01.wav` … `08.wav`, dichos a ritmo conversacional y sin silabear. El texto esperado queda en `expected.txt` al lado.

## Frases

1. Che, fijate si mañana llueve en Buenos Aires.
2. Acordame llamar a Vera a las cinco.
3. No, dejá, después lo vemos.
4. ¿Me buscás el mail de Alicia?
5. Pará, eso no era lo que te pedí.
6. Poné un recordatorio para el lunes a la mañana.
7. ¿Cómo venía el tema de Coolify?
8. Dale, cerrá la ventana del chat.

Términos de la pasada con contexto: Buenos Aires, Vera, Alicia, Coolify.

## Grabar

Desde la raíz del repo, con un micrófono:

```powershell
dotnet run --project tools/stt-bakeoff -- record --sample-dir <sample-dir>
```

Usa el micrófono predeterminado de Windows (el de Configuración → Sonido → Entrada) y muestra la lista de entradas. Enter empieza la toma, Enter la corta. Mientras hablás tiene que moverse el medidor. Si el nivel es bajo, podés subirlo en Windows o conservar la toma con `k`. Si el predeterminado no es el micrófono, repetí con `--device` y un pedazo del nombre, por ejemplo `--device Headset`. No uses el PCM de una nota de Telegram.

## Medir

Las claves salen del entorno o de `--env-file` (por ejemplo el `.env` local, que no se commitea). Si la variable de entorno ya está definida, gana sobre el archivo.

| Variable | Vendor |
|---|---|
| `STT_API_KEY` o `DEEPGRAM_API_KEY` | Deepgram nova-3, idioma `es-419` |
| `MISTRAL_API_KEY` | Voxtral Realtime (`voxtral-mini-transcribe-realtime-2602`) y Voxtral Mini Transcribe V2 (`voxtral-mini-2602`) |
| `OPENROUTER_API_KEY` o `LLM_API_KEY` | Scribe v2 (`elevenlabs/scribe-v2`) |
| `OPENAI_API_KEY` | `gpt-4o-transcribe`, sólo si la clave está |

Sin `MISTRAL_API_KEY` esas filas quedan `sin clave`. Nemotron queda `sin GPU`: no hay cliente en este script y no se alquila una máquina para cerrar. Una fila vacía no se rellena con un WER inventado.

```powershell
dotnet run --project tools/stt-bakeoff -- run --sample-dir <sample-dir> --env-file .env
```

El resultado queda en `bakeoff-results.json` y `bakeoff-table.md` dentro de la carpeta de la muestra, fuera del repo. El WER de la tabla es el del corpus (ediciones / palabras de la referencia), en minúsculas y sin puntuación. Un texto vacío cuenta como 1. La latencia es la media, en milisegundos, desde que terminó de enviarse el WAV hasta el texto final.

Gana el menor WER de entidades (con contexto si el API lo acepta; si no, sin contexto) y, a igualdad, el menor WER. Nemotron no compite: es dato y pide GPU. Si nadie le gana a Deepgram, la decisión es «sigue Deepgram» y no hay adaptador nuevo.

```powershell
dotnet run --project tools/stt-bakeoff -- self-test
```
