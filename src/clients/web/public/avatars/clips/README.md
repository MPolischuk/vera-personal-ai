# Clips Mixamo (GLB)

Animaciones de cuerpo para gestos del protocolo (`avatar.gesture`). El runtime carga
`/avatars/clips/{id}.glb` si existe; si no, cae al procedural de PT-25.

## Origen

| Archivo | Origen | Notas |
|---|---|---|
| _(ninguno)_ | — | `wave` usa el procedural `WAVE_POSE`; `wave.glb` (Mixamo) se quitó del árbol. Sigue en el historial git: purgarlo es una decisión aparte. |

No commitees un pack de terceros sin licencia clara. Las animaciones de Mixamo se usan
bajo los [Mixamo Terms of Use](https://www.mixamo.com/#terms) del operador; no se
redistribuye un catálogo entero.

## Cómo añadir / reemplazar un clip

1. En Mixamo: animación in-place, **FBX Binary**, **Without Skin**, 30 FPS.
2. Convertí a GLB offline (Blender, FBX2glTF, o script local con Three.js FBXLoader + GLTFExporter).
3. Nombrá el archivo igual que el id del gesto (`wave.glb`, `shrug.glb`).
4. El catálogo en `src/features/avatar/clipCatalog.ts` debe apuntar a esa URL.

No hace falta npm nuevo en el cliente: el runtime usa solo `GLTFLoader`.
