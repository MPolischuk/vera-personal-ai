# VPS operator MCP

Servidor local para Cursor. Habla con el servidor por SSH y no tiene una tool de shell.

El alias sale de `ASSISTANT_SSH_HOST` (una entrada `Host` de `~/.ssh/config`). El repositorio no guarda direcciones. Sin esa variable el proceso no arranca.

No es una extensión de Vera: no va en `config/extensions.yaml` ni se despliega en el VPS. La tarjeta de `coolify_control` sigue siendo de Vera.

Antes de usarlo, compilalo en Release. El `mcp.json` arranca con `--no-build` para que la salida de MSBuild no se mezcle con el protocolo:

```powershell
dotnet build tools/VpsOperator.Mcp/VpsOperator.Mcp.csproj -c Release
```

Reiniciá el servidor MCP `vps-operator` en Cursor después de ese build.

## Base de la app

No hay una tool de SQL ni de shell. Estas cinco cubren la base de `assistant` y sirven para el próximo parche del mismo tipo. Ninguna imprime `POSTGRES_APP_PASSWORD`, borra el volumen ni redeploya la API.

| Tool | Confirmación |
|---|---|
| `assistant_db_inspect` | — |
| `assistant_db_extension_update` | `update-extension:<nombre>` |
| `assistant_db_reindex` | `reindex:<indice>` |
| `assistant_db_runtime_role` | `ensure-runtime-role` |
| `assistant_db_image` | `pin-db-image:0.8.7-pg17` |

`assistant_db_image` sólo recrea el servicio `db`. `assistant_db_runtime_role` escribe el login en el env de Coolify si faltaba. El redeploy de la API sigue siendo `assistant_deploy`, y va después de que el rol exista.

## Migraciones, auditoría y logs

`assistant_db_migrate` aplica `dotnet ef database update` como `POSTGRES_USER`, con el checkout y la clave que Coolify ya usa para `assistant`. No usa `assistant_app`, no hace `GRANT` y no despliega. Antes va `assistant_db_backup`. Si la base ya está al día, sale 0 y la lista de aplicadas queda vacía.

| Tool | Confirmación |
|---|---|
| `assistant_db_migrate` | `migrate-assistant` |
| `assistant_audit` | — |
| `assistant_log_scan` | — |

`assistant_audit` corre `dotnet Assistant.Api.dll audit` en el contenedor de la API y devuelve la salida con el exit code. `assistant_log_scan` sólo devuelve cuántas líneas de log contienen `api.telegram.org/bot` seguido de dígitos. La línea no sale: tiene el token. Cero es el resultado bueno.
