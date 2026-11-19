# Conversación y cliente

## Implementado

Vera atiende un turno de texto por un hub SignalR (`/hub/assistant`). El mismo proceso sirve la API, la PWA y los archivos estáticos. El cliente es React 19 con Vite, Zustand y Tailwind. En el teléfono (ancho menor a 768 px) la cáscara deja el chat al frente y esconde las otras vistas.

Las vistas son chat, memoria, ajustes, uso, extensiones, recordatorios, actividad, briefing y diario.

La personalidad sale de `config/personality.yaml` al arrancar y pisa lo que hubiera en la base. El idioma de respuesta es `es-AR` y la zona horaria del perfil es `America/Argentina/Buenos_Aires`. El proceso guarda tiempos en UTC.

Hay dos modos de autenticación. En Compose local el cliente lleva un bearer (`AUTH_BEARER_TOKEN`, también horneado en la SPA al construir la imagen). El overlay de producción usa contraseña, JWT y un dispositivo que se puede revocar. `change-password` reemplaza el hash.

El presupuesto de contexto es 8000 tokens y el historial corto es de 40 mensajes (`appsettings.Example.json`).

## Decisión

Un solo proceso y un solo usuario por instalación. La zona horaria no se pide en cada turno: la lee el perfil. El bearer local evita un login en la máquina de desarrollo; la contraseña queda para el despliegue, donde el token no debe quedar dentro del JavaScript.

La personalidad es un archivo, no una fila editable en caliente. Reiniciar aplica el cambio y el arranque vuelve a escribir el perfil.

## Alternativas

- Varios usuarios con zona horaria propia en la fila del usuario.
- Login con contraseña también en local.
- Editar la personalidad desde la UI y guardarla solo en la base.
- Partir la SPA y la API en dos despliegues.

## A evaluar

- El bearer de desarrollo se incrusta en el build de la imagen. Sirve en la PC y no debe usarse en un host compartido.
- Revocar un dispositivo anula el refresh. Conviene comprobar, en un socket ya abierto, que el hub deja de aceptar turnos en cuanto el token deja de valer.
- La placa de oficina del cliente existe, y el flag `DEMO_SCENE.enabled` está en falso: la escena sigue la hora y el clima. Prender el flag fija esa foto.

## Pendiente

- Sesión multiusuario.
- Cierre inmediato del hub al revocar el dispositivo, si la conexión abierta sigue viva después del vencimiento.
