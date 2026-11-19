# Pendientes

Lo que el código prepara y todavía no cierra. El detalle está en cada ficha.

| Tema | Estado en este árbol | Ficha |
|---|---|---|
| Canal de WhatsApp | Hay `ChannelId`, retención y capacidades de plantilla. No hay proyecto de canal. | Recordatorios y canales |
| Verificación de hablante | No hay módulo. Un «sí» hablado no se compara con una voz enrolada. | Voz y avatar |
| Fin de turno con modelo | El cliente corta por energía. Smart Turn quedó como medición offline. | Voz y avatar |
| Visión | Docling lee documentos. No hay análisis de fotos ni de cámara. | Investigación |
| Embeddings alineados | La columna es `vector(640)` (Harrier). Compose nombra un modelo remoto sin fijar dimensiones. | Memoria y notas |
| Zona horaria única | El perfil y varios jobs asumen `America/Argentina/Buenos_Aires`. | Proactividad |
| Calendario | Servidor apagado. OAuth y tools de escritura ya tienen código. | Extensiones |
| Agentes externos | El trabajador interno y el tope de costo están. El directorio de pares no. | Delegación |
| Hub al revocar | Revocar el dispositivo anula el refresh. Falta ver el socket que ya estaba abierto. | Conversación y cliente |
| Hogar | No hay proyecto de domótica ni de dispositivos locales. | — |
| Suite `tests/` | `personal_ai_assistant.slnx` la lista. Esa carpeta no está en este árbol. El cliente sí tiene Vitest. | — |

## Cómo se cierra uno

Una ficha pasa a implementado cuando el proyecto existe en `src/` o en el cliente, tiene una prueba que corre en este árbol y la configuración de ejemplo no pide un secreto ni una dirección. Hasta entonces se queda en esta tabla.
