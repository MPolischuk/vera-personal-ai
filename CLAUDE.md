# CLAUDE.md

> Leer `AGENTS.md` para instrucciones completas.

## Entorno

.NET 10 y Node en la raíz del repo. PowerShell. `dotnet build` del `.slnx` falla mientras falte `tests/`: compilá `src/Assistant.Api/Assistant.Api.csproj`.

## Idioma

Responder en español. Identificadores de código en inglés. Textos de producto en voseo.

## Permisos

No hay `.claude/settings.local.json` en este árbol. No asumas permisos de red ni de SSH. El operador de `tools/VpsOperator.Mcp` solo corre si existe `ASSISTANT_SSH_HOST`.

## Skills disponibles

Las de Vera, no las del editor: `config/skills/use-memory`, `research`, `briefing`, `coolify-diagnosis`. Se leen al armar un turno. No las copies a otro formato.

## MCP Servers

Los de la app están en `config/extensions.yaml` (`coolify`, `weather`, `search`; `calendar` apagado). El operador SSH no es uno de ellos.

## Flujo de trabajo

1. Leer la ficha en `docs/caracteristicas` del área que vas a tocar.
2. Compilar el proyecto.
3. Cambiar el mínimo.
4. Volver a compilar. Si el cambio es de UI, `npm test --prefix src/clients/web`.
5. Si el comportamiento público cambió, actualizar la ficha.

## Patrones del proyecto

Registrar una extensión en `Program.cs` con el método `AddAssistant*` de ese proyecto. Una tool no lee `IConfiguration` para sacar una API key: el proveedor ya la tiene.

Una skill declara tools en el front matter:

```yaml
metadata:
  assistant.requires.tools: remember recall
```

## No hacer

No pegues un secreto, una dirección ni un nombre de persona en un ejemplo. No edites migraciones a mano.
