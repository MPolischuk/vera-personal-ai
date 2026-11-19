---
name: use-memory
description: Cuándo guardar un dato de la persona y cómo resolver una pregunta que depende de ese dato (el clima, el tiempo de viaje, un cumpleaños).
metadata:
  assistant.requires.tools: remember recall search_memory forget get_preferences update_preference
---

Antes de responder, identificá qué dato de la persona hace falta. Usá primero los hechos fijados y las preferencias que ya están en el turno. Si falta uno, llamá recall buscando ese dato, no la pregunta entera. Si no está, preguntá una vez y, si lo dice, guardalo con remember. No guardes lo que devuelve una tool en vivo. No inventes.

Ejemplos:
- «¿Llevo el paraguas?»: usá el lugar fijado. Si no está, recall de dónde vive. Si no está, preguntá una vez. No guardes el pronóstico. No inventes coordenadas: buscá esa ciudad sola con search_locations y pasá latitude y longitude de ese resultado.
- «¿Cuánto tardo en llegar al trabajo?»: usá el lugar de trabajo fijado. Si no está, recall de dónde trabaja. Si no está, preguntá una vez.
- «¿Cuándo cumple alguien cercano?»: quién es, desde los hechos fijados; la fecha, con recall de ese dato. Si no está, preguntá una vez. No inventes el día.

Nombre, dónde vive, lugar de trabajo, zona horaria, idioma y gente cercana se guardan con remember y pinned. Un gusto, un episodio o una tarea no se fija. Hay como máximo 8 fijados. Una mudanza o un cambio de trabajo: recall del hecho anterior y remember con replaces_id.

Cuando afirma un hecho estable o pide que te acuerdes, llamá remember en ese turno. Varios hechos: varias llamadas o un content con todos. Si el recuerdo nombra una obra, un lugar o un género, pasá index_terms con esas palabras. No las metas en content. Decir que quedó anotado o repetir la lista no guarda nada.

## Elegir o recomendar

Cuando pide elegir o recomendar y el dato no está en el turno:

1. Llamá search_memory una sola vez con queries, hasta 3, de lo amplio a lo específico. No armes una consulta por ronda.
2. Si un resultado nombra otra cosa que no está en el turno, la siguiente consulta de esa misma llamada puede buscarla.
3. En la respuesta, anotá qué recuerdo usás y por qué. Si unís dos, decí el vínculo. Si el dato no está, no lo inventes.

Usá update_preference cuando el dato tiene una clave estable, como el tono o un hábito con nombre. No guardes secretos, contraseñas ni datos de terceros que la persona no pidió recordar.

forget borra un recuerdo.
