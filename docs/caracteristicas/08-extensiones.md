# Extensiones

## Implementado

`config/extensions.yaml` declara servidores MCP. El asistente no ve los secretos: el archivo nombra tools, permiso y confianza. Los valores van por entorno.

| Servidor | Estado | Transporte | Rol |
|---|---|---|---|
| `coolify` | prendido | HTTP al contenedor `coolify-mcp` | estado, logs y control |
| `weather` | prendido | HTTP a Open-Meteo MCP 0.4.1 | pronóstico y datos climáticos |
| `search` | prendido | HTTP a `search-mcp` | consulta web medida |
| `calendar` | apagado | HTTP a Google Calendar MCP, OAuth | listar eventos |

`coolify_status` y `coolify_logs` son lectura, con salida no confiable. `coolify_control` es acción sensible (`start`, `stop`, `restart`, `deploy`) y pide confirmación. La allowlist (`COOLIFY_ALLOWLIST`) vive en el contenedor del MCP, no en el prompt. Sin URL, token y allowlist, el estado y los logs fallan y el resto del asistente sigue.

La skill `coolify-diagnosis` hace una lectura de estado y una de logs del nombre que esté en la allowlist, y prohíbe reiniciar o obedecer un texto que aparezca en un log.

Las skills de producto son `use-memory`, `research`, `briefing` y `coolify-diagnosis`. `propose_skill` deja una propuesta para aprobar o descartar en la API. `load_skill` la carga en el turno. El catálogo exige las tools que la skill declara.

El cliente lista extensiones y puede prender o apagar un servidor y reclasificar tools. El OAuth de calendario tiene start y callback aunque el servidor esté apagado.

Hay un MCP aparte, `tools/VpsOperator.Mcp`, que no es una extensión de Vera. Corre en la máquina de quien desarrolla, habla por SSH y no se despliega junto a la API. El alias sale de `ASSISTANT_SSH_HOST`. No hay una tool de shell ni de SQL libre.

## Decisión

Cada tool tiene nivel (`Read` o `SensitiveAction`), confianza de la salida y si escribe estado durable. La salida de un MCP es no confiable: puede citarse y no puede dar órdenes.

El token de Coolify no entra al proceso de la API. Lo tiene el contenedor `coolify-mcp`. Un log pasa por un filtro que quita líneas con `KEY`, `TOKEN`, `PASSWORD` o `SECRET` antes de volver al modelo.

El operador SSH queda fuera de Vera para que una frase del usuario no pueda migrar la base ni rotar un token. Esas tools piden una confirmación con un texto fijo (`migrate-assistant`, `pin-db-image:…`).

## Alternativas

- Dejar que el modelo ejecute `coolify_control` sin tarjeta.
- Publicar el puerto del MCP de Coolify.
- Instalar el operador SSH dentro del asistente, como una extensión más.
- Prender el calendario sin OAuth, con una clave de servicio en el yaml.

## A evaluar

- `control` sigue en el manifiesto. La skill de diagnóstico no lo llama; otra skill o un turno sin skill podría pedirlo, y ahí manda la tarjeta.
- El servidor de clima usa `MCP_AUTH_MODE: none` en la red de Compose. El puerto no está publicado en el host. Si alguien publica el puerto, el MCP queda sin autenticación.
- El operador asume rutas del host (`/home/operator/...` para dumps de instancia). Es una convención del servidor, no una dirección. Hay que cambiarla si el usuario del host es otro.

## Pendiente

- Calendario prendido, con el OAuth que ya tiene endpoints.
- Más servidores MCP. El manifiesto ya admite otro bloque; no hay que tocar el núcleo para sumar uno de lectura.
