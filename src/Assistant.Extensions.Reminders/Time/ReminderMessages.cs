namespace Assistant.Extensions.Reminders.Time;

/// <summary>Spanish product copy returned to the model.</summary>
internal static class ReminderMessages
{
    public const string DueInvalidShape =
        "due_invalid: Tenés que indicar exactamente un vencimiento: un plazo (in) o una fecha y hora local (local_datetime).";

    public const string DueInvalidDuration =
        "due_invalid: El plazo tiene que ser una duración ISO 8601, por ejemplo PT20M, PT2H o P1D.";

    public const string DueInvalidLocal =
        "due_invalid: La fecha local tiene que ser AAAA-MM-DDTHH:mm, sin zona.";

    public const string DueInPast =
        "due_in_past: Esa hora ya pasó hoy: ¿mañana?";

    public const string DueTooFar =
        "due_out_of_range: Más de dos años no puedo agendar; ¿lo dejamos en una fecha más cercana?";

    public const string RecurrenceInvalid =
        "recurrence_invalid: La recurrencia no es válida. Usá daily, weekly o monthly; los días de la semana sólo con weekly; until y count no van juntos.";

    public const string RecurrenceMismatch =
        "recurrence_invalid: La fecha local no cae en los días de la recurrencia.";

    public const string RecurrenceEmpty =
        "recurrence_invalid: Esa recurrencia no tiene ninguna ocurrencia en ese horario.";

    public const string TextInvalid =
        "El texto del recordatorio tiene que tener entre 1 y 500 caracteres.";

    public const string Disabled = "Los recordatorios están desactivados.";

    public const string CancelMissing =
        "cancel_reminder: pasá id o short_ref, uno solo.";

    public const string NotFound = "No encontré ese recordatorio.";

    public const string NotActive = "Ese recordatorio ya no está activo.";

    public const string ShortRefMissing = "No hay un recordatorio activo con ese número.";

    public const string ListStatusInvalid =
        "list_reminders: status tiene que ser active, missed o all.";

    public const string ListLimitInvalid =
        "list_reminders: limit tiene que ser un entero entre 1 y 50.";

    public const string DstSkipped =
        "La hora pedida no existe por el cambio de horario; la moví a la primera hora válida.";

    public const string DstAmbiguous =
        "Esa hora ocurre dos veces por el cambio de horario; usé la primera.";

    public static string DueTooSoon(int minimumSeconds)
        => FormattableString.Invariant(
            $"due_out_of_range: El plazo tiene que ser de al menos {minimumSeconds} segundos y como máximo 730 días.");

    public static string TooManyActive(int max)
        => FormattableString.Invariant(
            $"too_many_active: Ya tenés {max} recordatorios activos. Cancelá alguno antes de crear otro.");
}
