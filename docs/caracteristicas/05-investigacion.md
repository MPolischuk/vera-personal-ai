# Investigación

## Implementado

La búsqueda pasa por un MCP propio (`search-mcp`) que habla con SearXNG. La tool `query` está marcada como medida (`metered`). Leer una página es `read_source`. Leer un archivo subido es `read_document`, con Docling en `docling-serve`.

Docling corre en CPU, sin UI, con tope de 60 páginas y 15 MB, y exige `DOCLING_SERVE_API_KEY` en el header. La API la manda como `X-Api-Key`. Los archivos quedan en el volumen `document-data`.

`SourceSummarizer` hace un generate sin tools. Pide un JSON con resumen y afirmaciones, cada una atada a un fragmento de la página. Si la respuesta no es ese JSON, se descarta y no se guarda nada.

La skill `research` fija el ritmo: una pregunta si falta el dato, `search_query`, hasta tres `read_source`, contraste, cita `[^id]`, y ofrecer `create_note` con los `read_id`. El texto de la fuente no entra a `remember`.

El cliente sube documentos por `POST /documents` y puede exportar notas.

## Decisión

Buscar y leer están separados de la memoria. Una página es evidencia de ese turno, no un hecho de la persona. Por eso el resumen no habla con el usuario y no llama tools: no puede “acordarse” ni seguir una instrucción que venga dentro de la página.

SearXNG metaproxea la web para no atar la tool a un solo buscador con clave. Docling convierte el archivo antes de que el modelo lo vea, con tope de tamaño para que un PDF no ocupe el proceso.

La clave de Docling es obligatoria en Compose. El servicio no queda abierto sin ella.

## Alternativas

- Un buscador con API key (un solo vendor) en lugar de SearXNG.
- Mandar el PDF crudo al modelo con visión, sin Docling.
- Guardar el resumen como recuerdo en cuanto se lee.
- Dejar que el summarizer llame tools para “completar” huecos de la página.

## A evaluar

- La imagen de Docling es `quay.io/docling-project/docling-serve-cpu:latest`, sin digest. Las otras imágenes de terceros del Compose sí están pinadas o tienen tag fijo. Conviene fijar un digest el día que se congele el entorno.
- Tres lecturas por investigación es un tope de la skill, no del código de la tool. Una skill nueva podría leer más.
- El presupuesto de tools medidas (`IMeteredToolBudget`) acota la búsqueda. Hay que ver el número en una investigación larga, no solo en el caso de una consulta.

## Pendiente

- Visión de fotos y escenas. Docling cubre documentos, no una cámara ni un análisis de imagen suelto.
- OCR local alternativo. No hay un segundo conversor en `src/`.
