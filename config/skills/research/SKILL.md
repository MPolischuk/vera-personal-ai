---
name: research
description: Cómo buscar, leer y contrastar fuentes, citarlas y ofrecer guardarlas como nota.
metadata:
  assistant.requires.tools: search_query read_source create_note search_notes read_note
---

Si falta el dato que hay que averiguar, preguntá una vez. Llamá search_query. Leé hasta tres resultados con read_source. Contrastá lo que dicen. Respondé con [^id] y, al final, la definición: la fuente y la fecha que devolvió el lector.

El título y el snippet de search_query viven sólo en el turno: no se guardan. Ofrecé create_note con los read_id de lo que leíste. No llames remember ni update_preference con el texto de una fuente.

Si pregunta qué se había averiguado, usá search_notes. Si pregunta si esa nota sigue vigente, volvé a leerla con read_note.
