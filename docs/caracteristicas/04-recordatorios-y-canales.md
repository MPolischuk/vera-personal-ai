# Recordatorios y canales

## Implementado

Los recordatorios se crean, listan y cancelan por tools y por la API. Hay lead time mínimo de 30 segundos, horizonte de 730 días, 50 activos, 6 postergaciones por ocurrencia, gracia de 2 minutos y “perdido” a las 12 horas. El scheduler pulsa cada 5 segundos, con lote de 10 y lease de 2 minutos.

La entrega prueba canales en orden `hub`, `webpush`, `telegram`, `whatsapp`, y se queda con el primero que funciona. Hay horario de silencio (23:30 a 08:00 en la config de entrega; 22:00 a 08:00 en sugerencias) y un tope de 30 notificaciones por hora.

Telegram está implementado: emparejamiento con código de una hora, webhook o polling, mensajes ricos y tope de 120 segundos de voz. En Compose local queda apagado (`TELEGRAM_ENABLED=false`) y, si se prende, usa polling. En el overlay de producción el modo es webhook y hace falta `Channels__PublicBaseUrl` en https.

Web Push tiene claves VAPID, suscripción del navegador y un aviso de prueba. Los hosts de endpoint permitidos son los de FCM, Apple, Mozilla y Windows.

El webhook de Index (`index-01`) se aprueba por la firma, sin código de emparejamiento. El secreto entra por entorno, con prefijo `whsec_` y 24 a 64 bytes. No hay un secreto en `appsettings.json`.

WhatsApp existe como `ChannelId` y como casilla de retención de la bandeja (8 días; Telegram, 3). No hay un proyecto de canal de WhatsApp en `src/`.

## Decisión

Un recordatorio es una fila con reloj, no un mensaje que el modelo “se acuerda” de decir. El scheduler es el que dispara.

El primer canal que confirma corta la cadena. El hub gana si la PWA está abierta; si no, push; si no, Telegram.

Emparejar Telegram exige un código que la persona aprueba en Ajustes → Canales. Hasta entonces el bot no responde el contenido. El texto del código no nombra a nadie.

Index no usa ese código: la firma HMAC es la prueba, y el par nace aprobado para el usuario de la instalación.

## Alternativas

- Fan-out a todos los canales en vez de `FirstSuccess`.
- WhatsApp Cloud como canal real, con plantillas fuera de la ventana de 24 h. El modelo de mensaje ya reserva ese caso.
- Confirmaciones solo en el hub, sin push.
- Un bot de Telegram sin emparejamiento, aceptando cualquier chat.

## A evaluar

- `AllowSensitiveActions` está en verdadero para el canal remoto. Una acción sensible desde Telegram depende de esa bandera y de la tarjeta de confirmación.
- El horario de silencio de la entrega y el de las sugerencias no coinciden (23:30 contra 22:00). Hay que decidir si son dos políticas o una sola.
- La retención de la bandeja de WhatsApp corre aunque no haya canal. No guarda mensajes de ese canal; solo nombra la clave.

## Pendiente

- Canal de WhatsApp.
- Plantillas aprobadas y ventana de servicio, ya esbozadas en `ChannelCapabilities`.
