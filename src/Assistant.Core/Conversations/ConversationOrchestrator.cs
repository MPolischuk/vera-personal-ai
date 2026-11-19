using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Assistant.Abstractions;
using Assistant.Abstractions.Context;
using Assistant.Abstractions.Tools;
using Assistant.Core.Confirmations;
using Assistant.Core.Configuration;
using Assistant.Core.Context;
using Assistant.Core.Events;
using Assistant.Core.Models;
using Assistant.Core.Permissions;
using Assistant.Core.Personality;
using Assistant.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Core.Conversations;

internal sealed class ConversationOrchestrator(
    IConversationStore store,
    IPendingConfirmationStore confirmations,
    IContextAssembler assembler,
    IToolRegistry catalog,
    ToolDeclarationFilter tools,
    IToolDispatcher dispatcher,
    IModelRouter router,
    ActiveTurns activeTurns,
    IEventBus events,
    IOptions<ContextOptions> contextOptions,
    IOptions<TurnOptions> turnOptions,
    IOptions<PermissionOptions> permissionOptions,
    IPersonalityService personality,
    TimeProvider clock,
    ILogger<ConversationOrchestrator> log) : IConversationOrchestrator
{
    private const string CapReachedWithoutText =
        "No pude completar la acción: se alcanzó el límite de pasos con herramientas.";

    public async IAsyncEnumerable<TurnUpdate> RunTurnAsync(
        TurnRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var start = clock.GetTimestamp();
        var turnId = Guid.CreateVersion7();

        var conversation = await store.FindAsync(request.ConversationId, request.UserId, ct);
        if (conversation is null)
        {
            yield return Failed(start, TurnErrorCodes.ConversationNotFound,
                "No se encontró la conversación o no pertenece al usuario.");
            yield break;
        }

        var registration = activeTurns.TryRegister(conversation.Id, ct, out var turnCt);
        if (registration is null)
        {
            TurnLog.TurnInProgress(log, conversation.Id);
            yield return Failed(start, TurnErrorCodes.TurnInProgress,
                "Ya hay un turno en curso en esta conversación.");
            yield break;
        }

        try
        {
            await foreach (var update in ExecuteTurnAsync(
                               request, conversation, turnId, start, turnCt, ct))
            {
                yield return update;
            }
        }
        finally
        {
            registration.Dispose();
        }
    }

    private async IAsyncEnumerable<TurnUpdate> ExecuteTurnAsync(
        TurnRequest request,
        Conversation conversation,
        Guid turnId,
        long start,
        CancellationToken turnCt,
        CancellationToken callerCt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _ = ct;
        using var activity = TurnTelemetry.ActivitySource.StartActivity("turn");
        activity?.SetTag("conversation.id", conversation.Id.ToString("D"));
        activity?.SetTag("turn.id", turnId.ToString("D"));

        var emittedText = new StringBuilder();
        var invocations = new List<ToolInvocationRecord>();
        var firstTokenRecorded = false;
        var outcome = TurnOutcome.Completed;
        string? errorCode = null;
        string? errorMessage = null;
        var iterations = 0;
        TurnConfirmationRequired? confirmation = null;
        var cueParser = new CharacterCueParser();

        if (!request.IsResume)
        {
            var userPersisted = await TryAsync(
                () => store.AppendMessageAsync(
                    new AppendMessageRequest(
                        conversation.Id, request.UserId, MessageRole.User, request.UserMessage),
                    turnCt).AsTask(),
                callerCt);

            if (userPersisted.Interrupted)
            {
                yield return await CloseAndFinishAsync(
                    request, conversation.Id, turnId, emittedText, TurnOutcome.Interrupted,
                    invocations, iterations, start, null, null, callerCt);
                yield break;
            }

            if (userPersisted.Error is not null)
            {
                throw userPersisted.Error;
            }
        }

        yield return new TurnAccepted(turnId);

        var startPublished = await TryAsync(
            () => events.PublishAsync(
                new TurnStarted(conversation.Id, request.UserId, turnId), turnCt).AsTask(),
            callerCt);

        if (startPublished.Interrupted)
        {
            yield return await CloseAndFinishAsync(
                request, conversation.Id, turnId, emittedText, TurnOutcome.Interrupted,
                invocations, iterations, start, null, null, callerCt);
            yield break;
        }

        if (request is { IsResume: true, ConfirmationId: { } closedId }
            && (request.Expired || !request.Approved))
        {
            var resolution = request.Expired
                ? ConfirmationResolutionOutcome.Expired
                : ConfirmationResolutionOutcome.Denied;
            var closureMessage = request.Expired
                ? "No ejecuté la acción: la confirmación venció."
                : "No ejecuté la acción porque rechazaste la confirmación.";

            await foreach (var update in CloseDeniedAsync(
                               request, conversation, turnId, closedId, resolution, closureMessage,
                               emittedText, invocations, iterations, start, turnCt, callerCt, ct))
            {
                yield return update;
            }

            yield break;
        }

        var prepared = await PrepareMessagesAsync(
            request, conversation, turnId, turnCt, callerCt);

        if (prepared.Interrupted)
        {
            yield return await CloseAndFinishAsync(
                request, conversation.Id, turnId, emittedText, TurnOutcome.Interrupted,
                invocations, iterations, start, null, null, callerCt);
            yield break;
        }

        if (prepared.ErrorCode is not null)
        {
            yield return Finish(
                start, TurnOutcome.Failed, null, prepared.ErrorCode, prepared.ErrorMessage);
            yield break;
        }

        var messages = prepared.Messages!;
        var declarations = prepared.Declarations!;
        var turnContaminated = prepared.ContainsUntrustedContent;
        var client = router.GetClientFor(
            TaskKind.Conversation, new ModelCallContext(conversation.Id, turnId));
        var options = new ChatOptions { Tools = declarations };
        var maxRounds = turnOptions.Value.MaxToolIterations;
        var cutByCap = false;
        var exit = false;

        if (request is { IsResume: true, Approved: true, ConfirmationId: { } approvedId })
        {
            var resumed = await ResumeApprovedToolAsync(
                request,
                conversation,
                turnId,
                approvedId,
                messages,
                invocations,
                turnContaminated,
                turnCt,
                callerCt);

            if (resumed.Interrupted)
            {
                yield return await CloseAndFinishAsync(
                    request, conversation.Id, turnId, emittedText, TurnOutcome.Interrupted,
                    invocations, iterations, start, null, null, callerCt);
                yield break;
            }

            if (resumed.ErrorCode is not null)
            {
                yield return await CloseAndFinishAsync(
                    request, conversation.Id, turnId, emittedText, TurnOutcome.Failed,
                    invocations, iterations, start, resumed.ErrorCode, resumed.ErrorMessage, callerCt);
                yield break;
            }

            foreach (var update in resumed.Updates)
            {
                yield return update;
            }
        }

        while (!exit)
        {
            if (iterations >= maxRounds)
            {
                cutByCap = true;
                TurnLog.ToolIterationCap(log, turnId, maxRounds);
                break;
            }

            iterations++;

            var channel = Channel.CreateUnbounded<TurnUpdate>();
            var roundTask = ReceiveModelRoundAsync(
                client,
                messages,
                options,
                ToolNames(declarations),
                cueParser,
                emittedText,
                () =>
                {
                    if (firstTokenRecorded)
                    {
                        return;
                    }

                    firstTokenRecorded = true;
                    TurnTelemetry.TimeToFirstToken.Record(
                        clock.GetElapsedTime(start).TotalMilliseconds);
                },
                channel.Writer,
                turnCt,
                callerCt);

            await foreach (var update in channel.Reader.ReadAllAsync(ct))
            {
                yield return update;
            }

            var round = await roundTask;

            if (round.Interrupted)
            {
                outcome = TurnOutcome.Interrupted;
                break;
            }

            if (round.Failure is not null)
            {
                TurnLog.ProviderFailed(log, round.Failure, turnId);
                outcome = TurnOutcome.Failed;
                errorCode = TurnErrorCodes.ModelUnavailable;
                errorMessage = "El modelo no está disponible en este momento.";
                break;
            }

            messages.AddRange(round.Response!.Messages);

            var requests = round.Response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (requests.Count == 0)
            {
                break;
            }

            var executionContext = new ToolExecutionContext(
                request.UserId, conversation.Id, turnId);

            foreach (var call in requests)
            {
                if (turnCt.IsCancellationRequested && !callerCt.IsCancellationRequested)
                {
                    outcome = TurnOutcome.Interrupted;
                    exit = true;
                    break;
                }

                var argumentsJson = JsonSerializer.Serialize(
                    call.Arguments ?? new Dictionary<string, object?>());
                var arguments = ToolArguments.FromJson(argumentsJson);

                yield return new TurnToolStarted(call.Name);

                var dispatch = await TryAsync(
                    () => dispatcher.InvokeAsync(
                        call.Name,
                        arguments,
                        executionContext with
                        {
                            ToolCallId = call.CallId,
                            TurnContaminated = turnContaminated,
                        },
                        turnCt).AsTask(),
                    callerCt);

                if (dispatch.Interrupted)
                {
                    outcome = TurnOutcome.Interrupted;
                    exit = true;
                    break;
                }

                if (dispatch.Error is not null)
                {
                    throw dispatch.Error;
                }

                var result = dispatch.Value!;
                if (UntrustedContentEnvelope.ContainsMarker(result.Result?.Content)
                    || UntrustedContentEnvelope.ContainsMarker(result.Result?.Error))
                {
                    turnContaminated = true;
                }

                catalog.TryGetDescriptor(call.Name, out var descriptor);
                var extension = descriptor?.ExtensionName ?? "unknown";
                var level = descriptor?.Level ?? PermissionLevel.Undeclared;

                invocations.Add(new ToolInvocationRecord(
                    call.Name,
                    extension,
                    argumentsJson,
                    level,
                    result.Permission,
                    result.Reason,
                    result.Result?.Content,
                    result.Result?.Error,
                    result.Duration));

                var succeeded = result.Permission is PermissionOutcome.Allow
                                && result.Result?.Status is ToolStatus.Ok;
                yield return new TurnToolCompleted(call.Name, result.Permission, succeeded);

                if (result.Permission is PermissionOutcome.RequireConfirmation)
                {
                    var prompt = result.ConfirmationPrompt
                        ?? $"¿Confirmás ejecutar '{call.Name}'?";
                    var expiresAt = clock.GetUtcNow()
                        .AddSeconds(permissionOptions.Value.ConfirmationTimeoutSeconds);

                    var persisted = await TryAsync(
                        () => confirmations.CreateAsync(
                            new CreatePendingConfirmationRequest(
                                conversation.Id,
                                request.UserId,
                                call.Name,
                                argumentsJson,
                                prompt,
                                expiresAt,
                                turnContaminated),
                            turnCt).AsTask(),
                        callerCt);

                    if (persisted.Interrupted)
                    {
                        outcome = TurnOutcome.Interrupted;
                        exit = true;
                        break;
                    }

                    if (persisted.Error is not null)
                    {
                        throw persisted.Error;
                    }

                    var pending = persisted.Value!;

                    var requested = await TryAsync(
                        () => events.PublishAsync(
                            new ConfirmationRequested(
                                pending.Id,
                                conversation.Id,
                                request.UserId,
                                call.Name,
                                prompt,
                                pending.ExpiresAt),
                            turnCt).AsTask(),
                        callerCt);

                    if (requested.Interrupted)
                    {
                        outcome = TurnOutcome.Interrupted;
                        exit = true;
                        break;
                    }

                    if (requested.Error is not null)
                    {
                        throw requested.Error;
                    }

                    outcome = TurnOutcome.AwaitingConfirmation;
                    confirmation = new TurnConfirmationRequired(
                        pending.Id, call.Name, prompt, pending.ExpiresAt);
                    exit = true;
                    break;
                }

                string contentForModel;
                if (result.Permission is PermissionOutcome.Deny)
                {
                    contentForModel = result.Reason
                        ?? "No tenés permiso para ejecutar esta acción.";
                }
                else if (result.Result?.Status is ToolStatus.Ok)
                {
                    contentForModel = result.Result.Content ?? string.Empty;
                }
                else
                {
                    contentForModel = result.Result?.Error
                        ?? "La herramienta falló sin detalle.";
                }

                messages.Add(new ChatMessage(
                    ChatRole.Tool,
                    [new FunctionResultContent(call.CallId, contentForModel)]));
            }
        }

        if (confirmation is not null)
        {
            yield return confirmation;
        }

        if (outcome is TurnOutcome.Completed && cutByCap && emittedText.Length == 0)
        {
            emittedText.Append(CapReachedWithoutText);
        }

        if (outcome is not TurnOutcome.Failed
            && outcome is not TurnOutcome.Interrupted
            && outcome is not TurnOutcome.AwaitingConfirmation)
        {
            outcome = TurnOutcome.Completed;
        }

        RecordCharacterCue(
            cueParser.Outcome ?? CueParseOutcome.Absent,
            cueParser.ResolvedExpression,
            cueParser.RawHead,
            log);

        yield return await CloseAndFinishAsync(
            request, conversation.Id, turnId, emittedText, outcome,
            invocations, iterations, start, errorCode, errorMessage, callerCt);
    }

    private async Task<Preparation> PrepareMessagesAsync(
        TurnRequest request,
        Conversation conversation,
        Guid turnId,
        CancellationToken turnCt,
        CancellationToken callerCt)
    {
        using var contextSpan = TurnTelemetry.ActivitySource.StartActivity("context");

        try
        {
            var fullHistory = await store.GetHistoryAsync(
                conversation.Id,
                request.UserId,
                contextOptions.Value.HistoryMaxMessages,
                turnCt);

            IReadOnlyList<ConversationMessage> history;
            string utterance;
            if (request.IsResume)
            {
                history = fullHistory;
                utterance = fullHistory.LastOrDefault(m => m.Role is MessageRole.User)?.Content
                    ?? string.Empty;
            }
            else
            {
                history =
                    fullHistory.Count > 0
                    && fullHistory[^1].Role is MessageRole.User
                    && fullHistory[^1].Content == request.UserMessage
                        ? fullHistory.Take(fullHistory.Count - 1).ToList()
                        : fullHistory;
                utterance = request.UserMessage;
            }

            var recent = history
                .Select(m => new ContextMessage(m.Role, m.Content))
                .ToList();

            var context = await assembler.AssembleAsync(
                new ContextRequest(
                    conversation.Id,
                    request.UserId,
                    turnId,
                    utterance,
                    recent,
                    clock.GetUtcNow()),
                turnCt);

            var profile = (await personality.GetActiveAsync(turnCt)).Profile;
            var now = clock.GetUtcNow();
            var previousUserAt = PreviousUserMessageAt(history, request.IsResume);
            var messages = request.IsResume
                ? ChatMessageComposer.ComposeResume(
                    context,
                    WithClockOnLastUser(history, now, profile, previousUserAt))
                : ChatMessageComposer.Compose(
                    context,
                    history,
                    UserTurnClock.Decorate(
                        request.UserMessage,
                        now,
                        profile.TimeZone,
                        profile.Language,
                        previousUserAt));
            var declarations = ToolDeclarationMapper.Declare(
                tools.Apply(catalog.GetAvailableTools(
                    new ToolExecutionContext(request.UserId, conversation.Id, turnId))));

            return new Preparation(
                messages, declarations, null, null, false, context.ContainsUntrustedContent);
        }
        catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
        {
            return new Preparation(null, null, null, null, true);
        }
        catch (ContextBudgetExceededException e)
        {
            return new Preparation(
                null, null, TurnErrorCodes.ContextBudgetExceeded, e.Message, false);
        }
    }

    private async Task<ResumeDispatch> ResumeApprovedToolAsync(
        TurnRequest request,
        Conversation conversation,
        Guid turnId,
        Guid confirmationId,
        List<ChatMessage> messages,
        List<ToolInvocationRecord> invocations,
        bool historyContaminated,
        CancellationToken turnCt,
        CancellationToken callerCt)
    {
        var pending = await TryAsync(
            () => confirmations.FindAsync(
                confirmationId, conversation.Id, request.UserId, turnCt).AsTask(),
            callerCt);

        if (pending.Interrupted)
        {
            return ResumeDispatch.Interrupt();
        }

        if (pending.Error is not null)
        {
            throw pending.Error;
        }

        if (pending.Value is null)
        {
            return ResumeDispatch.Fail(
                TurnErrorCodes.ConfirmationInvalid,
                "La confirmación no es válida o no pertenece a esta conversación.");
        }

        var claim = await TryAsync(
            () => confirmations.TryClaimAsync(
                confirmationId,
                conversation.Id,
                request.UserId,
                ConfirmationResolutionOutcome.Approved,
                pending.Value.ToolName,
                clock.GetUtcNow(),
                turnCt).AsTask(),
            callerCt);

        if (claim.Interrupted)
        {
            return ResumeDispatch.Interrupt();
        }

        if (claim.Error is not null)
        {
            throw claim.Error;
        }

        if (claim.Value is not { Won: true, Confirmation: { } approved })
        {
            return ResumeDispatch.Fail(
                TurnErrorCodes.ConfirmationInvalid,
                "La confirmación no es válida o ya fue resuelta.");
        }

        var published = await TryAsync(
            () => events.PublishAsync(
                new ConfirmationResolved(
                    approved.Id,
                    conversation.Id,
                    request.UserId,
                    approved.ToolName,
                    ConfirmationResolutionOutcome.Approved),
                turnCt).AsTask(),
            callerCt);

        if (published.Interrupted)
        {
            return ResumeDispatch.Interrupt();
        }

        if (published.Error is not null)
        {
            throw published.Error;
        }

        var argumentsJson = approved.ArgumentsJson ?? "{}";
        var arguments = ToolArguments.FromJson(argumentsJson);
        var callId = approved.Id.ToString("D");
        var executionContext = new ToolExecutionContext(
            request.UserId,
            conversation.Id,
            turnId,
            approved.Id,
            historyContaminated || approved.TurnContaminated,
            callId);

        var updates = new List<TurnUpdate> { new TurnToolStarted(approved.ToolName) };

        var dispatch = await TryAsync(
            () => dispatcher.InvokeAsync(
                approved.ToolName, arguments, executionContext, turnCt).AsTask(),
            callerCt);

        if (dispatch.Interrupted)
        {
            return ResumeDispatch.Interrupt();
        }

        if (dispatch.Error is not null)
        {
            throw dispatch.Error;
        }

        var result = dispatch.Value!;
        catalog.TryGetDescriptor(approved.ToolName, out var descriptor);
        var extension = descriptor?.ExtensionName ?? "unknown";
        var level = descriptor?.Level ?? PermissionLevel.Undeclared;

        invocations.Add(new ToolInvocationRecord(
            approved.ToolName,
            extension,
            argumentsJson,
            level,
            result.Permission,
            result.Reason,
            result.Result?.Content,
            result.Result?.Error,
            result.Duration));

        var succeeded = result.Permission is PermissionOutcome.Allow
                        && result.Result?.Status is ToolStatus.Ok;
        updates.Add(new TurnToolCompleted(approved.ToolName, result.Permission, succeeded));

        string contentForModel;
        if (result.Permission is PermissionOutcome.Deny)
        {
            contentForModel = result.Reason
                ?? "No tenés permiso para ejecutar esta acción.";
        }
        else if (result.Result?.Status is ToolStatus.Ok)
        {
            contentForModel = result.Result.Content ?? string.Empty;
        }
        else
        {
            contentForModel = result.Result?.Error
                ?? "La herramienta falló sin detalle.";
        }

        messages.Add(new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent(callId, contentForModel)]));

        return new ResumeDispatch(updates, null, null, false);
    }

    private async IAsyncEnumerable<TurnUpdate> CloseDeniedAsync(
        TurnRequest request,
        Conversation conversation,
        Guid turnId,
        Guid confirmationId,
        ConfirmationResolutionOutcome resolution,
        string closureMessage,
        StringBuilder emittedText,
        List<ToolInvocationRecord> invocations,
        int iterations,
        long start,
        CancellationToken turnCt,
        CancellationToken callerCt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _ = ct;

        var pending = await TryAsync(
            () => confirmations.FindAsync(
                confirmationId, conversation.Id, request.UserId, turnCt).AsTask(),
            callerCt);

        if (pending.Interrupted)
        {
            yield return await CloseAndFinishAsync(
                request, conversation.Id, turnId, emittedText, TurnOutcome.Interrupted,
                invocations, iterations, start, null, null, callerCt);
            yield break;
        }

        if (pending.Error is not null)
        {
            throw pending.Error;
        }

        if (pending.Value is null)
        {
            yield return Finish(
                start,
                TurnOutcome.Failed,
                null,
                TurnErrorCodes.ConfirmationInvalid,
                "La confirmación no es válida o no pertenece a esta conversación.");
            yield break;
        }

        var claim = await TryAsync(
            () => confirmations.TryClaimAsync(
                confirmationId,
                conversation.Id,
                request.UserId,
                resolution,
                pending.Value.ToolName,
                clock.GetUtcNow(),
                turnCt).AsTask(),
            callerCt);

        if (claim.Interrupted)
        {
            yield return await CloseAndFinishAsync(
                request, conversation.Id, turnId, emittedText, TurnOutcome.Interrupted,
                invocations, iterations, start, null, null, callerCt);
            yield break;
        }

        if (claim.Error is not null)
        {
            throw claim.Error;
        }

        if (claim.Value is not { Won: true, Confirmation: { } closed })
        {
            yield return Finish(
                start,
                TurnOutcome.Failed,
                null,
                TurnErrorCodes.ConfirmationInvalid,
                "La confirmación no es válida o ya fue resuelta.");
            yield break;
        }

        var published = await TryAsync(
            () => events.PublishAsync(
                new ConfirmationResolved(
                    closed.Id,
                    conversation.Id,
                    request.UserId,
                    closed.ToolName,
                    resolution),
                turnCt).AsTask(),
            callerCt);

        if (published.Interrupted)
        {
            yield return await CloseAndFinishAsync(
                request, conversation.Id, turnId, emittedText, TurnOutcome.Interrupted,
                invocations, iterations, start, null, null, callerCt);
            yield break;
        }

        if (published.Error is not null)
        {
            throw published.Error;
        }

        emittedText.Append(closureMessage);
        yield return await CloseAndFinishAsync(
            request, conversation.Id, turnId, emittedText, TurnOutcome.Completed,
            invocations, iterations, start, null, null, callerCt);
    }

    private static async Task<ModelRound> ReceiveModelRoundAsync(
        IChatClient client,
        List<ChatMessage> messages,
        ChatOptions options,
        IReadOnlySet<string> toolNames,
        CharacterCueParser cueParser,
        StringBuilder emittedText,
        Action onFirstToken,
        ChannelWriter<TurnUpdate> updates,
        CancellationToken turnCt,
        CancellationToken callerCt)
    {
        using var modelSpan = TurnTelemetry.ActivitySource.StartActivity("model");
        Exception? failure = null;
        var received = new List<ChatResponseUpdate>();
        var interrupted = false;
        var gate = new LeakedToolCallGate(toolNames);
        cueParser.BeginRound();

        try
        {
            await using var enumerator = client
                .GetStreamingResponseAsync(messages, options, turnCt)
                .GetAsyncEnumerator(turnCt);

            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
                {
                    interrupted = true;
                    break;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    failure = e;
                    break;
                }

                if (!string.IsNullOrEmpty(update.Text))
                {
                    await EmitVisibleAsync(
                        gate.Push(update.Text), cueParser, emittedText, onFirstToken, updates)
                        .ConfigureAwait(false);
                }

                received.Add(update);
            }

            await EmitVisibleAsync(gate.Flush(), cueParser, emittedText, onFirstToken, updates)
                .ConfigureAwait(false);

            foreach (var update in cueParser.Flush())
            {
                await WriteVisibleUpdateAsync(update, emittedText, onFirstToken, updates)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            updates.Complete();
        }

        if (interrupted || failure is not null)
        {
            return new ModelRound(null, failure, interrupted);
        }

        var response = LeakedToolCallParser.AttachRecoveredCalls(
            received.ToChatResponse(), gate.RecoveredCalls);
        return new ModelRound(response, null, false);
    }

    private static async Task EmitVisibleAsync(
        IEnumerable<string> chunks,
        CharacterCueParser cueParser,
        StringBuilder emittedText,
        Action onFirstToken,
        ChannelWriter<TurnUpdate> updates)
    {
        foreach (var raw in chunks)
        {
            if (raw.Length == 0)
            {
                continue;
            }

            foreach (var update in cueParser.Push(raw))
            {
                await WriteVisibleUpdateAsync(update, emittedText, onFirstToken, updates)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteVisibleUpdateAsync(
        TurnUpdate update,
        StringBuilder emittedText,
        Action onFirstToken,
        ChannelWriter<TurnUpdate> updates)
    {
        switch (update)
        {
            case TurnToken { Text: { Length: 0 } }:
                return;
            case TurnToken token:
                onFirstToken();
                emittedText.Append(token.Text);
                await updates.WriteAsync(token, CancellationToken.None).ConfigureAwait(false);
                return;
            default:
                await updates.WriteAsync(update, CancellationToken.None).ConfigureAwait(false);
                return;
        }
    }

    private static void RecordCharacterCue(
        CueParseOutcome outcome,
        string? expression,
        string rawHead,
        ILogger logger)
    {
        using var activity = TurnTelemetry.ActivitySource.StartActivity("character.cue");
        activity?.SetTag("outcome", outcome.ToString());

        if (logger.IsEnabled(LogLevel.Information))
        {
            var outcomeName = outcome.ToString();
            var preview = string.IsNullOrEmpty(rawHead)
                ? "(empty)"
                : rawHead.Replace("\n", "\\n", StringComparison.Ordinal);
            PersonalityLog.CueOutcome(logger, outcomeName, preview);
        }

        switch (outcome)
        {
            case CueParseOutcome.Parsed:
                CharacterTelemetry.CueParsed.Add(
                    1, new KeyValuePair<string, object?>("expression", expression ?? "none"));
                break;
            case CueParseOutcome.Invalid:
                CharacterTelemetry.CueInvalid.Add(1);
                PersonalityLog.LogInvalidCueOnce(logger);
                break;
            case CueParseOutcome.Absent:
                CharacterTelemetry.CueAbsent.Add(1);
                break;
        }
    }

    private static HashSet<string> ToolNames(IEnumerable<AITool> declarations)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in declarations)
        {
            if (!string.IsNullOrEmpty(tool.Name))
            {
                names.Add(tool.Name);
            }
        }

        return names;
    }

    private async Task<TurnFinished> CloseAndFinishAsync(
        TurnRequest request,
        Guid conversationId,
        Guid turnId,
        StringBuilder emittedText,
        TurnOutcome outcome,
        List<ToolInvocationRecord> invocations,
        int iterations,
        long start,
        string? errorCode,
        string? errorMessage,
        CancellationToken callerCt)
    {
        var messageId = await CloseAsync(
            request, conversationId, turnId, emittedText.ToString(),
            outcome, invocations, iterations, start, callerCt);
        return Finish(start, outcome, messageId, errorCode, errorMessage);
    }

    private async ValueTask<Guid?> CloseAsync(
        TurnRequest request,
        Guid conversationId,
        Guid turnId,
        string text,
        TurnOutcome outcome,
        List<ToolInvocationRecord> invocations,
        int iterations,
        long start,
        CancellationToken callerCt)
    {
        // The turn token may already be canceled: using it would drop exactly what
        // we are trying to save (D10).
        using var closing = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
        closing.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = closing.Token;

        Guid? messageId = null;
        var interrupted = outcome is TurnOutcome.Interrupted
                           || (outcome is TurnOutcome.Failed && text.Length > 0);

        var shouldPersist = outcome is TurnOutcome.Completed || text.Length > 0;
        if (shouldPersist)
        {
            var metadata = invocations.Count == 0
                ? null
                : JsonSerializer.Serialize(new
                {
                    tools = invocations.Select(i => i.ToolName).Distinct().ToArray(),
                    iterations,
                });

            var message = await store.AppendMessageAsync(
                new AppendMessageRequest(
                    conversationId,
                    request.UserId,
                    MessageRole.Assistant,
                    text,
                    metadata,
                    WasInterrupted: interrupted)
                {
                    Invocations = invocations,
                },
                ct);
            messageId = message.Id;
        }

        var duration = clock.GetElapsedTime(start);
        TurnTelemetry.Duration.Record(
            duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        TurnTelemetry.ToolIterations.Record(iterations);

        switch (outcome)
        {
            case TurnOutcome.Completed when messageId is { } id:
                // D11 cheap exit: terminal turn events carry the request user id
                // so audit does not need a conversation lookup.
                await events.PublishAsync(
                    new TurnCompleted(
                        conversationId, request.UserId, turnId, id, invocations.Count, duration),
                    ct);
                break;

            case TurnOutcome.Interrupted:
                await events.PublishAsync(
                    new TurnInterrupted(
                        conversationId, request.UserId, turnId, messageId, text.Length, duration),
                    ct);
                break;
        }

        return messageId;
    }

    private static DateTimeOffset? PreviousUserMessageAt(
        IReadOnlyList<ConversationMessage> history,
        bool resume)
    {
        var seen = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Role is not MessageRole.User)
            {
                continue;
            }

            seen++;
            // Resume history already includes this user turn; the previous one is the second hit.
            if (resume ? seen == 2 : seen == 1)
            {
                return history[i].CreatedAt;
            }
        }

        return null;
    }

    private static IReadOnlyList<ConversationMessage> WithClockOnLastUser(
        IReadOnlyList<ConversationMessage> history,
        DateTimeOffset utcNow,
        PersonalityProfile profile,
        DateTimeOffset? previousUserAt)
    {
        var index = -1;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Role is MessageRole.User)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return history;
        }

        var copy = history.ToList();
        var current = copy[index];
        copy[index] = current with
        {
            Content = UserTurnClock.Decorate(
                current.Content,
                utcNow,
                profile.TimeZone,
                profile.Language,
                previousUserAt),
        };
        return copy;
    }

    private static async Task<Attempt<T>> TryAsync<T>(Func<Task<T>> action, CancellationToken callerCt)
    {
        try
        {
            return new Attempt<T>(await action(), null, false);
        }
        catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
        {
            return new Attempt<T>(default, null, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new Attempt<T>(default, e, false);
        }
    }

    private static async Task<Attempt<bool>> TryAsync(Func<Task> action, CancellationToken callerCt)
    {
        try
        {
            await action();
            return new Attempt<bool>(true, null, false);
        }
        catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
        {
            return new Attempt<bool>(false, null, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new Attempt<bool>(false, e, false);
        }
    }

    private TurnFinished Finish(
        long start,
        TurnOutcome outcome,
        Guid? messageId,
        string? errorCode,
        string? errorMessage)
        => new(outcome, messageId, clock.GetElapsedTime(start), errorCode, errorMessage);

    private TurnFinished Failed(long start, string code, string message)
        => Finish(start, TurnOutcome.Failed, null, code, message);

    private readonly record struct Attempt<T>(T? Value, Exception? Error, bool Interrupted);

    private sealed record Preparation(
        List<ChatMessage>? Messages,
        IList<AITool>? Declarations,
        string? ErrorCode,
        string? ErrorMessage,
        bool Interrupted,
        bool ContainsUntrustedContent = false);

    private sealed record ResumeDispatch(
        IReadOnlyList<TurnUpdate> Updates,
        string? ErrorCode,
        string? ErrorMessage,
        bool Interrupted)
    {
        public static ResumeDispatch Interrupt()
            => new([], null, null, true);

        public static ResumeDispatch Fail(string errorCode, string errorMessage)
            => new([], errorCode, errorMessage, false);
    }

    private sealed record ModelRound(
        ChatResponse? Response,
        Exception? Failure,
        bool Interrupted);
}
