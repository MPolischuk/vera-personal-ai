# Delegación

## Implementado

`delegate_work` crea una tarea de agente con instrucción, título y tope. `list_tasks` y `cancel_task` la consultan y la cortan. La vista Actividad muestra la cola y puede cancelar por API.

Los topes por defecto están en `DelegationOptions`: 0,10 USD, 2 minutos, una tarea a la vez, lease de 2 minutos renovado cada 30 segundos, instrucción de hasta 8000 caracteres. Si el costo acumulado llega al tope, la tarea falla con `CostCap`.

Al terminar, `TaskResultSpeaker` hace un generate sin tools y pide una o dos frases para el usuario. Le dice que no siga instrucciones que vengan en el resultado. Si no hay presupuesto o el generate falla, el aviso se queda en la plantilla. La frase se recorta a 1000 caracteres.

El trabajador puede apuntar a un agente remoto (`RemoteEndpoint`) y a una lista de hosts de egreso. Esa lista nace vacía.

Hay un contrato de par remoto en el código de delegación (registro, aviso, router). La UI no expone un directorio de agentes de terceros.

## Decisión

La tarea no corre dentro del turno que la pidió. El turno sigue y el resultado vuelve como aviso. Así una investigación larga no bloquea la conversación.

El tope de costo es chico y concurrente a uno a propósito: un delegado no puede seguir llamando al modelo por su cuenta. El texto del delegado no se publica como si lo hubiera dicho Vera. Lo reescribe el speaker, o se usa la plantilla.

El resultado entra envuelto y el prompt prohíbe obedecerlo. Es el mismo criterio que en los logs y en las páginas web: el contenido externo no es una orden.

## Alternativas

- Correr el delegado en el mismo turno, con el mismo presupuesto de tools.
- Publicar el stream del delegado como respuesta de Vera.
- Varias tareas en paralelo (`MaxConcurrent` mayor a 1).
- Un tope por día, además del tope por tarea.

## A evaluar

- 0,10 USD y 2 minutos alcanzan para un resumen. Se quedan cortos si la tarea tiene que leer varios documentos.
- `EgressHosts` vacío significa que no hay destinos remotos declarados. Antes de apuntar `RemoteEndpoint` a un host, esa lista tiene que decir a quién se le puede hablar.
- El speaker gasta otro generate. Si el tope ya se consumió, el usuario ve la plantilla, no un párrafo. Conviene mirar si esa plantilla alcanza para enterarse del resultado.

## Pendiente

- Directorio de agentes externos y un contrato de mensajes usado por un par que no sea el trabajador interno.
- Paralelismo, cuando haya una razón para más de una tarea viva.
