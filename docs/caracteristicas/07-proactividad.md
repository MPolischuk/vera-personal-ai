# Proactividad

## Implementado

Vera puede tomar la iniciativa con piezas que la persona dejó escritas:

- Metas (`set_goal`, `complete_goal`, `list_goals`).
- Automatizaciones con cadencia y hora local (`create_automation`, `pause_automation`, `delete_automation`, `list_automations`).
- Órdenes permanentes y la pausa de cada una.
- Intenciones condicionales: se disparan por el nombre de un evento, no por el reloj. El aviso lleva el texto guardado; el texto del evento no se concatena.
- Checklist del heartbeat.
- Briefing del día, con feedback por ítem, y un atajo de frase que dispara el briefing ya generado.
- Heartbeat en una ventana. En silencio (22:00–08:00) no sugiere. El tope es 3 sugerencias por día, cada 30 minutos.
- Diario: una propuesta se aprueba o se rechaza; una entrada se puede deshacer. La contradicción entre recuerdos se revisa en un job aparte, con un generate acotado (800 tokens de salida).

El reloj local de esos jobs es `America/Argentina/Buenos_Aires`.

La skill `briefing` obliga a un JSON (`summary`, `today`, `ahead`) y prohíbe inventar eventos o seguir instrucciones que vengan dentro de un evento.

## Decisión

Nada de esto infiere un hábito y actúa. Hay una fila que la persona creó (meta, automatización, orden, intención, checklist) o un briefing que se puede corregir con feedback. El heartbeat lee esas filas; no inventa presencia.

El diario no se escribe solo en firme. Pasa por una propuesta. Aprobar y rechazar son endpoints. Deshacer una entrada también.

La revisión de contradicciones es un job, no un turno. Si el modelo no devuelve la fecha esperada, no se aplica.

## Alternativas

- Sugerir a partir del historial, sin una orden escrita.
- Escribir el diario directo, sin propuesta.
- Un solo horario de silencio compartido con la entrega de notificaciones.
- Disparar intenciones por similitud semántica en vez de por `event_name` exacto.

## A evaluar

- Tres sugerencias por día y una cada 30 minutos pueden ser pocas o muchas según el uso. Están en configuración (`Proactive:Suggestions`), no en una constante escondida.
- El briefing y el heartbeat comparten zona horaria con el perfil. Si el perfil cambia de zona, los jobs que tienen el id escrito en código siguen en Argentina hasta que se unifiquen.
- Una intención mal nombrada no matchea. El nombre del evento es un contrato con quien publica `InboundFactReceived`, no una frase libre.

## Pendiente

- Unificar la zona horaria de los jobs con la del perfil, si una instalación no está en Argentina.
- Origen de hechos de calendario. El servidor de calendario está apagado; sin eventos externos, las intenciones condicionales solo ven lo que el propio proceso publica.
