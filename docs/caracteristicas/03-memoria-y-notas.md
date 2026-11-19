# Memoria y notas

## Implementado

Vera guarda hechos (`remember`, `recall`, `search_memory`, `forget`), preferencias (`get_preferences`, `update_preference`) y notas (`create_note`, `read_note`, `update_note`, `search_notes`, `link_notes`). La skill `use-memory` dice cuándo fijar un hecho (nombre, dónde vive, trabajo, zona, idioma, gente cercana): como máximo 8 fijados. Un gusto o una tarea no se fija.

Olvidar marca el recuerdo. `MemoryPurgeService` lo borra de la base a las 48 horas. La pantalla de memoria lo explica: el backup cifrado puede conservarlo 14 días más, y hace falta la clave privada de quien opera el backup para leerlo.

Hay export de memorias y de notas, y un recorrido (`/memories/journey`). Postgres usa pgvector. La columna de embedding es `vector(640)`.

El generador local es Harrier-OSS-v1 270M en fp32, dentro del proceso. Los pesos no se versionan: van en `models/` y el contenedor los monta en solo lectura. `appsettings.Example.json` deja ese perfil (`Provider: Local`, 640 dimensiones, prefijo de consulta).

`compose.yaml` define `Assistant__Models__Embedding__Model` con default `openai/text-embedding-3-small` y la misma API key del LLM. No define proveedor ni dimensiones.

## Decisión

Los embeddings de la base son de 640 dimensiones porque el perfil previsto es Harrier local: el recuerdo no sale a un proveedor para vectorizarse, y el modelo cabe en CPU. Las tools de memoria las llama el modelo de conversación; el vector es otro componente.

El olvido no es inmediato. Las 48 horas dejan una ventana para restaurar desde la UI (`/memories/{id}/restore`) antes del purge.

La skill separa hecho estable de dato vivo. El clima o el texto de una página no se guardan como recuerdo.

## Alternativas

- Embeddings remotos (`text-embedding-3-small` u otro) y una migración que cambie `vector(640)`.
- Borrar en el acto, sin gracia de 48 horas.
- Un grafo de notas en lugar de vínculos sueltos.
- Meter los pesos en la imagen. Hoy se leen de disco a propósito.

## A evaluar

- Si el entorno del contenedor pisa el modelo de embedding y no el proveedor, el arranque puede quedar a mitad de camino: la columna es 640 y el default de Compose nombra un modelo remoto de otra dimensión. Hay que alinear proveedor, modelo, dimensiones y la migración antes de cambiarlo.
- El prefijo de instrucción de Harrier no aplica a un modelo de OpenAI. Cambiar de proveedor pide revisar `QueryPrefix` y `DocumentPrefix`.
- Ocho hechos fijados es un tope de prompt. Conviene ver si el turno se llena antes de llegar al tope de tokens.

## Pendiente

- Documentar en el Compose un perfil de embedding que coincida con `vector(640)`, o migrar la columna si el perfil remoto es el que se quiere.
- Retención del backup: el texto de la UI habla de 14 días y de una clave privada. El script y el bucket no están en este árbol; el procedimiento vive en la máquina de quien opera.
