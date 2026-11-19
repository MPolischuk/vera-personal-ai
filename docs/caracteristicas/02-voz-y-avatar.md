# Voz y avatar

## Implementado

El turno de voz entra por el hub. El STT por defecto es Deepgram `nova-3` (`https://api.deepgram.com`). El TTS es ElevenLabs, modelo `eleven_flash_v2_5`, con el `voice id` en el entorno. El segmentador corta el primer tramo apenas hay texto y los siguientes alrededor de 48–220 caracteres, para no esperar el párrafo entero antes del audio.

El fin de turno del cliente es por energía: silencio de unos 800 ms después de haber detectado habla. Hay un medidor aparte, en `tools/smart-turn`, que compara ese corte con Smart Turn v3.2 sobre WAV que no viven en el repo.

El avatar es un VRM (`avatars/vera-office.vrm`) con Three.js: mirada, pose en reposo, gestos, sincronía labial y un pulso mientras habla. El fondo de día a día sigue la hora solar y el clima. La placa de oficina queda detrás de un flag apagado.

Los términos que el STT prioriza, en el ejemplo de configuración, son solo `Vera`.

## Decisión

Deepgram sigue como proveedor de producción. El comentario de `compose.yaml` deja el motivo: en la pasada medida, Scribe v2 mejoró el error de palabras y el texto final tardó cerca de 1,5 s, así que no entra si la meta es oír la respuesta antes. Cambiar de proveedor es una variable de entorno (`STT_PROVIDER`, `STT_MODEL`, `STT_ENDPOINT`), sin un adaptador nuevo en el proceso.

Smart Turn no se enlazó al cliente. El script de medición es la evidencia; el detector que corre es el de energía.

El audio de diagnóstico (`ASSISTANT_STT_DUMP_DIR`) escribe WAV en disco y viene vacío, o sea apagado.

## Alternativas

- Scribe v2 u otro modelo de OpenRouter, con la misma clave de LLM.
- Smart Turn en el navegador (ONNX) para cerrar antes en las pausas cortas.
- TTS local, sin una clave de ElevenLabs.
- Un solo plano de fondo, sin clima ni hora.

## A evaluar

- La muestra de oro del bake-off no está en el repo. Repetir la tabla pide esos WAV y claves en el entorno, no en el código.
- Flash de ElevenLabs suele pedir plan pago. El `.env.example` nombra `eleven_multilingual_v2` y `eleven_turbo_v2_5` como reemplazo.
- El dump de PCM no debe quedar prendido en un host compartido: es la voz del turno.

## Pendiente

- Verificación de hablante. No hay un proyecto en `src/` que compare la voz con un voiceprint.
- Fin de turno semántico en el cliente, si una medición nueva le gana a la energía sin sumar un runtime pesado.
