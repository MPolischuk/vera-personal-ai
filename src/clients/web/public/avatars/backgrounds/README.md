# Fondos de escena

JPEG/PNG 2D para `scene.background` (PT-29). No son HDRI ni environment maps.

Nueve tomas de la misma ventana. El id es el stem del archivo. La UI elige
`window-{franja}-{condición}` según la hora en Corrientes y el cielo. Sin
caché y con la consulta caída, muestra el despejado de esa franja.

| Archivo | Id | Notas |
|---|---|---|
| `window-day-clear.png` | `window-day-clear` | Día despejado |
| `window-day-cloudy.png` | `window-day-cloudy` | Día nublado |
| `window-day-rain.png` | `window-day-rain` | Día con lluvia |
| `window-dusk-clear.png` | `window-dusk-clear` | Atardecer despejado |
| `window-dusk-cloudy.png` | `window-dusk-cloudy` | Atardecer nublado |
| `window-dusk-rain.png` | `window-dusk-rain` | Atardecer con lluvia |
| `window-night-clear.png` | `window-night-clear` | Noche despejada |
| `window-night-cloudy.png` | `window-night-cloudy` | Noche nublada |
| `window-night-rain.png` | `window-night-rain` | Noche con lluvia |

Son assets originales del repo (no fotos de terceros). Si reemplazás uno, mantené
el nombre de archivo o actualizá `sceneBackgrounds.ts`. Preferí encuadres
oscuros para que el VRM siga legible (M2).

## Placa estática de la demo

`office-dotnetconf.png` no entra en las nueve variantes. La elige
`demoScene.ts` (`DEMO_SCENE.enabled`). Con `enabled: true` la oficina queda
fija y no consulta el clima. Con `enabled: false` vuelven las nueve ventanas,
con la misma hora y el mismo cielo de antes. La habitación sigue midiendo
2.65 m; la placa es más alta (`imageHeightMeters` 3.61) porque suma pared y
piso para que el plano de cuerpo entero no deje ver el estudio. Las ventanas
siguen en 4 m.

La pantalla y el cuadro usan arte oficial de
[dotnetConf/2026](https://github.com/dotnetConf/2026) (MIT): el wallpaper
`swag/desktop/blue-1920x1080.png` y el bot de la plantilla
`Creative/DotNetConf-2026-Template.pptx`. El cuadro de la pared derecha es el
logo de cuatro cuadrados de Microsoft, en la perspectiva de esa pared.
