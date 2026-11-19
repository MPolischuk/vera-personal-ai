using System.Text;
using Assistant.Abstractions.Channels;
using Assistant.Abstractions.Security;
using Assistant.Abstractions.Time;
using Assistant.Api.Audit;
using Assistant.Api.Webhooks;
using Assistant.Api.Authentication;
using Assistant.Api.Channels;
using Assistant.Api.Configuration;
using Assistant.Api.Endpoints;
using Assistant.Api.Observability;
using Assistant.Api.Personality;
using Assistant.Api.Protocol;
using Assistant.Api.Realtime;
using Assistant.Api.Security;
using Assistant.Api.Speech;
using Assistant.Api.Tools;
using Assistant.Channels;
using Assistant.Channels.Delivery;
using Assistant.Api.Cli;
using Assistant.Channels.Telegram;
using Assistant.Channels.Telegram.Pairing;
using Assistant.Channels.Telegram.Webhook;
using Assistant.Channels.WebPush;
using Assistant.Core;
using Assistant.Core.Audit;
using Assistant.Core.Conversations;
using Assistant.Core.Events;
using Assistant.Core.Permissions;
using Assistant.Core.Personality;
using Assistant.Core.Speech;
using Assistant.Core.Usage;
using Assistant.Extensions.Delegation;
using Assistant.Extensions.Memory;
using Assistant.Extensions.Notes;
using Assistant.Extensions.Reminders;
using Assistant.Extensions.Mcp;
using Assistant.Extensions.Research;
using Assistant.Extensions.Proactive;
using Assistant.Extensions.Skills;
using Assistant.Persistence;
using Assistant.Persistence.Proactive;
using Assistant.Persistence.Memory;
using Assistant.Providers.Llm.Configuration;
using Assistant.Scheduling;
using Assistant.Providers.Llm;
using Assistant.Providers.Speech;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;

const string CorsPolicy = "ConfiguredOrigins";

if (VapidKeysCommand.TryDispatch(args, out var vapidExitCode))
{
    Environment.ExitCode = vapidExitCode;
    return;
}

if (ConfigurationAudit.TryDispatch(args, AppContext.BaseDirectory, out var auditExitCode))
{
    Environment.ExitCode = auditExitCode;
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureSerilog();
builder.Host.ConfigureHostOptions(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

var connectionString = builder.Configuration.GetConnectionString("Assistant")
    ?? throw new InvalidOperationException("ConnectionStrings:Assistant is required.");

var allowedOrigins = builder.Configuration
    .GetSection(ApiOptions.SectionName)
    .Get<ApiOptions>()
    ?.AllowedOrigins
    .ToArray() ?? [];

builder.Services
    .AddAssistantOptions(builder.Configuration)
    .AddAssistantCore()
    .AddAssistantPersistence(connectionString)
    .AddProactiveStores()
    .AddSingleton<ISecretBox, DataProtectionSecretBox>()
    .AddAssistantPersonality()
    .AddAssistantLlm()
    .AddAssistantSpeech(builder.Configuration)
    .AddAssistantConversation()
    .AddAssistantUsageAccounting()
    .AddAssistantAudit()
    .AddSingleton<IPersonalityProfileProvider, YamlPersonalityProfileProvider>()
    .AddAssistantTool<GetCurrentDateTimeTool>()
    .AddAssistantTool<GetAssistantInfoTool>()
    .AddAssistantTool<EchoWithConfirmationTool>()
    .AddAssistantMemory()
    .AddAssistantReminders()
    .AddAssistantDelegation(builder.Configuration)
    .AddAssistantMcp(builder.Configuration)
    .AddAssistantResearch(builder.Configuration)
    .AddAssistantNotes()
    .AddAssistantSkills(builder.Configuration)
    .AddAssistantProactive();

DecorateRemoteChannelPolicy(builder.Services);
builder.Services.AddScoped<ITurnOriginAccessor, TurnOriginAccessor>();
builder.Services.AddContextContributor<ChannelSituationContributor>();
builder.Services.AddContextContributor<RecentNotificationsContributor>();
builder.Services.AddScoped<ITurnIngress, TurnIngress>();
builder.Services.AddScoped<IInboundAudioTranscriber>(sp =>
    new InboundAudioTranscriber(
        sp.GetService<ISpeechToTextProvider>(),
        sp.GetRequiredService<IPersonalityService>()));
builder.Services.AddOptions<ChannelsOptions>()
    .Bind(builder.Configuration.GetSection(ChannelsOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddScoped<IChannelAuditSink, ChannelAuditSink>();
builder.Services.AddChannels(builder.Configuration);
builder.Services.AddTelegramChannel(builder.Configuration);
builder.Services.AddWebPushChannel(builder.Configuration);
if (TelegramChannelEnabled(builder.Configuration))
{
    TelegramWebhookEndpoint.AddRateLimiter(builder.Services);
    builder.Services.AddSingleton<IPairingMessenger, TelegramPairingMessenger>();
}
builder.Services.AddOptions<DiagnosticsOptions>()
    .Bind(builder.Configuration.GetSection(DiagnosticsOptions.SectionName));
builder.Services.AddSingleton<HubPresence>();
builder.Services.AddSingleton<HubAckRegistry>();
builder.Services.AddSingleton<IDeliveryAckWait>(sp => sp.GetRequiredService<HubAckRegistry>());
builder.Services.AddSingleton<IUserTimeZoneProvider, UserTimeZoneProvider>();
builder.Services.AddScoped<HubOutboundChannel>();
builder.Services.AddScoped<IOutboundChannel>(sp => sp.GetRequiredService<HubOutboundChannel>());

builder.Services.AddProblemDetails();
builder.Services.AddAssistantOpenTelemetry(builder.Configuration);
builder.Services.AddSingleton<TurnConnections>();
builder.Services.AddSingleton<VoicePlaybackLedger>();
builder.Services.AddSingleton<VoiceSessionRegistry>();
builder.Services.AddSingleton<VoiceTurnPipeline>();
builder.Services.AddSingleton<TurnRunner>();
builder.Services.AddDomainEventHandler<TurnStarted, ClientBroadcastSubscriber>();
builder.Services.AddDomainEventHandler<TurnCompleted, ClientBroadcastSubscriber>();

if (!AuthModes.TryParse(builder.Configuration["Auth:Mode"], out var authMode))
{
    throw new InvalidOperationException("Auth:Mode must be Bearer or Password.");
}

var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = StaticBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = StaticBearerDefaults.AuthenticationScheme;
});

if (authMode == AuthMode.Password)
{
    var signingKey = builder.Configuration["Auth:JwtSigningKey"] ?? string.Empty;
    authentication.AddJwtBearer(StaticBearerDefaults.AuthenticationScheme, options =>
    {
        options.MapInboundClaims = true;
        var keyBytes = Encoding.UTF8.GetBytes(signingKey);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero,
            IssuerSigningKey = keyBytes.Length == 0 ? null : new SymmetricSecurityKey(keyBytes),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Headers.ContainsKey("Authorization"))
                {
                    return Task.CompletedTask;
                }

                if (!context.Request.Path.StartsWithSegments("/hub/assistant"))
                {
                    return Task.CompletedTask;
                }

                var accessToken = context.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(accessToken))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });
    builder.Services.AddSingleton<AccessTokenIssuer>();
}
else
{
    authentication.AddScheme<AuthenticationSchemeOptions, StaticBearerAuthenticationHandler>(
        StaticBearerDefaults.AuthenticationScheme, _ => { });
}

builder.Services.AddAuthorization();
builder.Services.AddScoped<PasswordCredentialService>();
builder.Services.AddHostedService<PasswordCredentialBootstrap>();

if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(
            CorsPolicy,
            policy => policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
    });
}

builder.Services
    .AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions = ProtocolJson.Options);

// Hosted services stop in reverse registration order. Expiry stops first so it
// does not start new closes during drain; the pump must outlive turns finishing
// during shutdown so terminal events still reach audit.
builder.Services.AddHostedService<DomainEventPumpService>();
builder.Services.AddHostedService<TurnDrainService>();
// Registered after the drain so shutdown stops the scheduler first (reverse order).
builder.Services.AddScheduling(builder.Configuration);
builder.Services.AddSingleton<StandardWebhooksVerifier>();
builder.Services.AddSingleton<PebbleIndexSignatureVerifier>();
builder.Services.AddSingleton<InboundWebhookTestProbe>();
builder.Services.AddScoped<InboundWebhookSignatureMiddleware>();
builder.Services.AddJobHandler<PurgeInboundWebhookDeliveriesJob>();
builder.Services.AddHostedService<InboundWebhookPurgeSeed>();
builder.Services.AddHostedService<IndexPeerSeed>();
builder.Services.AddOptions<ConfirmationExpiryOptions>()
    .Bind(builder.Configuration.GetSection(ConfirmationExpiryOptions.SectionName));
builder.Services.AddHostedService<ConfirmationExpiryService>();
builder.Services.AddHostedService<MemoryConsolidationService>();

var app = builder.Build();

try
{
    if (args is ["change-password"])
    {
        Environment.ExitCode = await ChangePasswordAsync(app.Services, authMode);
        await app.DisposeAsync();
        return;
    }

    if (TelegramWebhookCommand.IsRequest(args))
    {
        Environment.ExitCode = await TelegramWebhookCommand.RunAsync(args, app.Services);
        await app.DisposeAsync();
        return;
    }

    if (args is [MemoryReembedCommand.Argument])
    {
        var embeddingModel = app.Services.GetRequiredService<IOptions<ModelsOptions>>().Value.Embedding.Model;
        Environment.ExitCode = await MemoryReembedCommand.RunAsync(
            app.Services, embeddingModel, Console.Out, CancellationToken.None);
        await app.DisposeAsync();
        return;
    }

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();

    if (allowedOrigins.Length > 0)
    {
        app.UseCors(CorsPolicy);
    }

    app.UseAuthentication();
    app.UseAuthorization();
    if (app.Services.GetRequiredService<IOptions<TelegramOptions>>().Value.Enabled)
    {
        app.UseRateLimiter();
    }

    // Static files in Production only. Fallback is mapped after hub and /api so it
    // cannot swallow negotiate or REST. Middleware stays before Map* (ASP.NET Core).
    if (app.Environment.IsProduction())
    {
        // VRM / Mixamo clips are glTF-binary; without a mapping StaticFiles returns 404.
        var staticTypes = new FileExtensionContentTypeProvider();
        staticTypes.Mappings[".vrm"] = "model/gltf-binary";
        staticTypes.Mappings[".glb"] = "model/gltf-binary";

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = staticTypes });
    }

    app.UseWhen(
        static context => context.Request.Path.StartsWithSegments("/api/hooks"),
        branch => branch.UseMiddleware<InboundWebhookSignatureMiddleware>());

    app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
    if (app.Services.GetRequiredService<IOptions<TelegramOptions>>().Value.Enabled)
    {
        app.MapTelegramWebhook();
    }
    if (authMode == AuthMode.Password)
    {
        app.MapLoginEndpoints();
    }

    app.MapHub<AssistantHub>("/hub/assistant");

    var api = app.MapGroup("/api").RequireAuthorization();
    if (authMode == AuthMode.Password)
    {
        api.MapDeviceEndpoints();
    }

    api.MapConversationEndpoints();
    api.MapPersonalityEndpoints();
    api.MapUsageEndpoints();
    api.MapMemoryEndpoints();
    api.MapNoteEndpoints();
    api.MapDocumentEndpoints();
    api.MapExtensionEndpoints();
    api.MapSkillProposalEndpoints();
    api.MapReminderEndpoints();
    api.MapActivityEndpoints();
    api.MapNotificationEndpoints();
    api.MapChannelEndpoints();
    api.MapPushEndpoints();
    api.MapBriefingEndpoints();
    api.MapDiaryEndpoints();
    api.MapCalendarOAuthStart();
    app.MapCalendarOAuthCallback();
    // Index 01: enable Webhooks:Inbound:Sources:index with Format pebble-index.
    // The whsec_ secret stays out of the repo.
    IndexRingEndpoint.Map(app);
    InboundWebhookTestHost.Map(app);

    if (app.Environment.IsProduction())
    {
        app.MapFallbackToFile("index.html");
    }

    if (app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.VerifySchemaOnStart)
    {
        await SchemaCheck.EnsureUpToDateAsync(app.Services, CancellationToken.None);
    }

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var seed = scope.ServiceProvider.GetService<BriefingSeed>();
        if (seed is not null)
        {
            await seed.EnsureAsync(CancellationToken.None);
        }
    }

    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}

static bool TelegramChannelEnabled(IConfiguration configuration)
{
    var raw = configuration[$"{TelegramOptions.SectionName}:Enabled"];
    return bool.TryParse(raw, out var enabled) && enabled;
}

static async Task<int> ChangePasswordAsync(IServiceProvider services, AuthMode mode)
{
    if (mode != AuthMode.Password)
    {
        Log.Error("change-password requires Auth:Mode=Password.");
        return 1;
    }

    try
    {
        await using var scope = services.CreateAsyncScope();
        var credentials = scope.ServiceProvider.GetRequiredService<PasswordCredentialService>();
        await credentials.ReplaceAsync(CancellationToken.None);
        Log.Information("Password hash replaced.");
        return 0;
    }
    catch (InvalidOperationException ex)
    {
        Log.Error("{Message}", ex.Message);
        return 1;
    }
}

static void DecorateRemoteChannelPolicy(IServiceCollection services)
{
    var descriptor = services.Single(d => d.ServiceType == typeof(IPermissionPolicy));
    services.Remove(descriptor);
    services.AddScoped<IPermissionPolicy>(sp =>
    {
        var implementationType = descriptor.ImplementationType
            ?? throw new InvalidOperationException(
                "IPermissionPolicy must be registered by implementation type.");
        var inner = (IPermissionPolicy)ActivatorUtilities.CreateInstance(sp, implementationType);
        return ActivatorUtilities.CreateInstance<RemoteChannelPolicy>(sp, inner);
    });
}
