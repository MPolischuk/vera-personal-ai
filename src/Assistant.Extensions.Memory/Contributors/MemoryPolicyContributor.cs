using Assistant.Abstractions.Context;

namespace Assistant.Extensions.Memory.Contributors;

/// <summary>
/// Short appended nudge so remember/recall fire when the store is empty
/// and relevant/preference fragments are absent (ADR-0015).
/// </summary>
internal sealed class MemoryPolicyContributor : IContextContributor
{
    internal const string Body =
        "Antes de responder o de llamar una tool, identificá qué datos de la persona hacen falta (lugar, horario, trabajo, alguien cercano). Usá primero los hechos fijados y las preferencias que ya están en el turno. Si falta uno, llamá recall buscando ese dato, no la pregunta entera. Si no está, preguntá una vez y, si lo dice, guardalo.\n"
        + "Nombre, dónde vive, lugar de trabajo, zona horaria, idioma y gente cercana se guardan con remember y pinned. Un gusto, un episodio o una tarea no se fija. Hay como máximo 8 fijados: si remember rechaza el noveno, reemplazá uno con replaces_id.\n"
        + "Una mudanza o un cambio de trabajo: recall del hecho anterior y remember con replaces_id. No guardes lo que devuelve una tool en vivo. No inventes.\n"
        + "Si el turno ya trae las condiciones actuales, respondé con eso y no llames tools de clima, ni recall, ni la hora. Si no están, no inventes latitude ni longitude: search_locations con la ciudad sola de donde vive la persona, no otra ciudad. Hablá de la persona en segunda persona.\n"
        + "Para elegir o recomendar, search_memory con hasta 3 consultas en la misma llamada, de lo amplio a lo específico.\n"
        + "Si un recuerdo trae el rótulo [inferido], presentalo como suposición.";

    public string Name => "memory.policy";

    public int Priority => MemoryContextDefaults.PolicyPriority;

    public bool IsEnabled => true;

    public ValueTask<ContextFragment?> ContributeAsync(ContextRequest request, CancellationToken ct)
        => ValueTask.FromResult<ContextFragment?>(
            ContextFragment.Text(Name, ContextPlacement.AppendedMessage, Body));
}
