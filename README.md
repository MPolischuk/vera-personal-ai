# Vera

Asistente personal de un solo usuario. Habla y escribe en español rioplatense, recuerda lo que le confirman, busca en la web, lee documentos, agenda recordatorios y puede dejar una tarea corta corriendo con un tope de costo.

La API es ASP.NET Core sobre .NET 10. El cliente es una PWA en React. Los datos viven en Postgres con pgvector. El modelo de conversación entra por OpenRouter. La voz usa Deepgram y ElevenLabs. Los embeddings previstos por el esquema son locales (Harrier, 640 dimensiones).

Licencia: [GPL-3.0](LICENSE).

## Qué incluye

- Chat en tiempo real (SignalR) y escena con avatar VRM.
- Memoria, preferencias y notas.
- Recordatorios, Web Push y Telegram.
- Investigación con SearXNG y Docling.
- Briefing, metas, automatizaciones y diario con aprobación.
- Extensiones MCP para clima, búsqueda y el panel de Coolify.

El detalle, con las decisiones y lo que sigue abierto, está en [docs/caracteristicas](docs/caracteristicas/README.md). Quien vaya a modificar el repo lee [AGENTS.md](AGENTS.md).

## Arranque local

Hace falta .NET 10 SDK, Node 22, Docker y un archivo `.env` que no se commitea.

```powershell
Copy-Item .env.example .env
```

Completá en `.env` la contraseña de Postgres, un bearer de al menos 32 caracteres, la clave de OpenRouter, Deepgram, ElevenLabs (voice id incluido) y la clave de Docling. El resto puede quedar vacío: Telegram, Web Push y Coolify arrancan apagados o sin efecto.

```powershell
docker compose up --build
```

La API y la PWA quedan en `http://localhost:8080`. El dashboard de telemetría, en `http://localhost:18888`.

Para trabajar el cliente aparte:

```powershell
npm install --prefix src/clients/web
npm run dev --prefix src/clients/web
dotnet run --project src/Assistant.Api
```

El ejemplo del cliente está en `src/clients/web/.env.example`.

## Configuración que no viaja con el git

| Qué | Dónde queda |
|---|---|
| Claves y contraseñas | `.env` (plantilla: `.env.example`) |
| Perfil propio, opcional | `config/personality.local.yaml`. Si no está, se usa la plantilla |
| Pesos de Harrier | `models/` |
| Alias SSH del operador | Variable `ASSISTANT_SSH_HOST` en la máquina local |

`config/personality.yaml` es la plantilla pública: idioma `es-AR`, trato de vos, zona `America/Argentina/Buenos_Aires`. Si en la misma carpeta existe `personality.local.yaml`, el proceso usa ese archivo. No lo subas.

## Pruebas

```powershell
npm test --prefix src/clients/web
dotnet build src/Assistant.Api/Assistant.Api.csproj
```

La solución nombra una carpeta `tests/` que este árbol no trae. Hasta que esté, el `dotnet test` de la solución no corre.

## Despliegue

`compose.production.yaml` no publica el puerto de la base, no hornea el bearer en la imagen y pasa la API a contraseña con JWT. Las claves las inyecta el entorno. No hay direcciones de un servidor en el repositorio.
