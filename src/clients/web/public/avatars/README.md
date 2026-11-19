# Avatars (VRM)

Colocá aquí los modelos `.vrm` exportados desde **VRoid Studio**.

## Archivo por defecto

1. Exportá tu avatar desde VRoid Studio como VRM.
2. Guardalo como `default.vrm` en esta carpeta.
3. El YAML de personalidad ya apunta a ese path:

```yaml
avatar:
  model: "avatars/default.vrm"
  idle_animation: "idle"
```

Vite sirve `public/` en la raíz de la SPA, así que el browser pide `/avatars/default.vrm`.

## Cambiar de avatar (G5)

Opción A — otro archivo:

1. Copiá un segundo `.vrm` a esta carpeta (p. ej. `alt.vrm`).
2. Cambiá `avatar.model` en `config/personality.yaml` a `avatars/alt.vrm`.
3. Reiniciá la API y hacé reload de la SPA.

Opción B — mismo nombre:

1. Reemplazá `default.vrm` por otro export.
2. Reload duro del browser (cache del `.vrm`).

No hace falta tocar código del cliente. No hay picker ni PUT de personalidad en esta etapa.

## Fondos de escena

Fotos 2D para el backdrop del VRM: [`backgrounds/`](./backgrounds/). Ver
[`backgrounds/README.md`](./backgrounds/README.md). Las nueve ventanas siguen
la hora y el clima. La placa de la demo (`office-dotnetconf.png`) es aparte:
`DEMO_SCENE.enabled` en `src/clients/web/src/features/chat/demoScene.ts`. No hay
picker en Settings ni campo en el YAML.

## Clips Mixamo (gestos)

Los GLB de cuerpo (`wave`, opcional `shrug`) viven en [`clips/`](./clips/). Ver
[`clips/README.md`](./clips/README.md) para origen, licencia y cómo reemplazar un clip.
Si falta el archivo, el cliente usa el gesto procedural (PT-25).

## Licencia

No commitees un VRM de terceros sin licencia clara. El export propio del autor del repo es el camino esperado para G1.
