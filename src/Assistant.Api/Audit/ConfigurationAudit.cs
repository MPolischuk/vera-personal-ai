using System.Text.Json;
using Assistant.Extensions.Mcp;
using Assistant.Extensions.Skills;
using Microsoft.Extensions.Configuration;

namespace Assistant.Api.Audit;

internal static class ConfigurationAudit
{
    public static bool TryDispatch(
        string[] args,
        string root,
        out int exitCode,
        TextWriter? output = null,
        TextWriter? error = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(root);

        if (args is ["audit"])
        {
            exitCode = Run(root, output);
            return true;
        }

        if (args.Length > 0 && args[0] == "telegram-webhook")
        {
            if (args is ["telegram-webhook", "info" or "set" or "delete"])
            {
                exitCode = 0;
                return false;
            }

            (error ?? Console.Error).WriteLine("usage: dotnet Assistant.Api.dll telegram-webhook info|set|delete");
            exitCode = 2;
            return true;
        }

        // WebApplicationFactory always passes --environment, --contentRoot and
        // --applicationName. Those are the host, not the audit command.
        if (args.Length > 0 && args.All(static arg => arg.StartsWith('-')))
        {
            exitCode = 0;
            return false;
        }

        if (args.Length > 0)
        {
            (error ?? Console.Error).WriteLine("usage: dotnet Assistant.Api.dll audit");
            exitCode = 2;
            return true;
        }

        exitCode = 0;
        return false;
    }

    public static int Run(string root, TextWriter? output = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var writer = output ?? Console.Out;
        var configuration = ChannelsConfigurationAudit.Load(root);
        var findings = Collect(root, configuration, out var pinned);
        WriteCatalog(writer, pinned);
        if (ChannelsConfigurationAudit.AllowsSensitiveRemoteActions(configuration))
        {
            writer.WriteLine("channels.remote_sensitive_allowed");
        }

        if (TelegramPollingIsActive(root))
        {
            writer.WriteLine("telegram.polling_enabled");
        }
        if (findings.Count == 0)
        {
            writer.WriteLine("audit: ok");
            return 0;
        }

        findings.Sort(static (left, right) =>
        {
            var byCode = string.CompareOrdinal(left.Code, right.Code);
            return byCode != 0 ? byCode : string.CompareOrdinal(left.Subject, right.Subject);
        });

        foreach (var finding in findings)
        {
            writer.WriteLine($"{finding.Severity} {finding.Code} {finding.Subject}");
        }

        return 1;
    }

    private static void WriteCatalog(TextWriter writer, IReadOnlyList<PinnedMcpServer> pinned)
    {
        foreach (var gap in AgenticRiskCatalog.Gaps)
        {
            writer.WriteLine($"asi.gap {gap.Risk} {gap.Package}");
        }

        foreach (var pin in pinned)
        {
            writer.WriteLine($"pinned {pin.Id} {pin.Version}");
        }
    }

    private static List<AuditFinding> Collect(
        string root,
        IConfiguration configuration,
        out IReadOnlyList<PinnedMcpServer> pinned)
    {
        var findings = new List<AuditFinding>();
        var composePath = Path.Combine(root, "compose.yaml");
        var overlayPath = Path.Combine(root, "compose.production.yaml");
        var inspection = InspectManifest(
            Path.Combine(root, "config", "extensions.yaml"),
            ComposeConfigurationAudit.ReadServicePins(composePath, overlayPath));
        pinned = inspection.PinnedServers;
        foreach (var finding in inspection.Findings)
        {
            findings.Add(new AuditFinding(finding.Severity, finding.Code, finding.Subject));
        }

        var tools = new HashSet<string>(inspection.UsableToolNames, StringComparer.Ordinal);
        foreach (var name in InProcessToolNames.All)
        {
            tools.Add(name);
        }

        foreach (var gap in SkillRequirementAudit.FindGaps(
            Path.Combine(root, "config", "skills"),
            tools,
            inspection.EnabledServerIds))
        {
            findings.Add(new AuditFinding("warning", "skill.requirement_missing", gap.Subject));
        }

        findings.AddRange(ComposeConfigurationAudit.Inspect(composePath, overlayPath));
        AddWebhookFindings(root, findings);
        findings.AddRange(ChannelsConfigurationAudit.Inspect(configuration));
        return findings;
    }

    private static ManifestInspection InspectManifest(
        string path,
        IReadOnlyDictionary<string, string> servicePins)
    {
        if (!File.Exists(path))
        {
            return new ManifestInspection(
                [],
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                []);
        }

        try
        {
            return ManifestConfigurationAudit.Inspect(File.ReadAllText(path), servicePins);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ManifestInspection(
                [new ManifestAuditFinding("error", "manifest.invalid", "extensions.yaml")],
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                []);
        }
    }

    /// <summary>
    /// Polling is active when the audit root's appsettings, overlaid with the process environment, enables it.
    /// An informational line only: it does not change the exit code.
    /// </summary>
    private static bool TelegramPollingIsActive(string root)
    {
        var enabled = false;
        var mode = "Webhook";
        var path = Path.Combine(root, "appsettings.json");
        if (File.Exists(path))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("Telegram", out var telegram))
                {
                    if (telegram.TryGetProperty("Enabled", out var enabledProperty)
                        && enabledProperty.ValueKind is JsonValueKind.True)
                    {
                        enabled = true;
                    }

                    if (telegram.TryGetProperty("Mode", out var modeProperty)
                        && modeProperty.ValueKind == JsonValueKind.String
                        && modeProperty.GetString() is { Length: > 0 } configured)
                    {
                        mode = configured;
                    }
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
            }
        }

        if (FirstEnvironment("Telegram__Enabled", "TELEGRAM_ENABLED") is { } enabledRaw
            && bool.TryParse(enabledRaw, out var enabledFromEnvironment))
        {
            enabled = enabledFromEnvironment;
        }

        if (FirstEnvironment("Telegram__Mode", "TELEGRAM_MODE") is { Length: > 0 } modeRaw)
        {
            mode = modeRaw;
        }

        return enabled && string.Equals(mode, "Polling", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstEnvironment(string primary, string alias)
        => Environment.GetEnvironmentVariable(primary) ?? Environment.GetEnvironmentVariable(alias);

    /// <summary>
    /// An enabled inbound source with no secret is an error. Secrets live in the environment;
    /// the file only has to name the source. The secret value is never written.
    /// </summary>
    private static void AddWebhookFindings(string root, List<AuditFinding> findings)
    {
        var path = Path.Combine(root, "appsettings.json");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("Webhooks", out var webhooks)
                || !webhooks.TryGetProperty("Inbound", out var inbound)
                || !inbound.TryGetProperty("Sources", out var sources)
                || sources.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var source in sources.EnumerateObject())
            {
                if (!SourceEnabled(source.Name, source.Value) || HasSecret(source.Name, source.Value))
                {
                    continue;
                }

                findings.Add(new AuditFinding("error", "webhooks.secret_missing", source.Name));
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
        }
    }

    private static bool SourceEnabled(string name, JsonElement source)
    {
        var enabled = source.TryGetProperty("Enabled", out var enabledProperty)
            && enabledProperty.ValueKind == JsonValueKind.True;
        var raw = Environment.GetEnvironmentVariable($"Webhooks__Inbound__Sources__{name}__Enabled");
        if (bool.TryParse(raw, out var enabledFromEnvironment))
        {
            enabled = enabledFromEnvironment;
        }

        return enabled;
    }

    private static bool HasSecret(string name, JsonElement source)
    {
        if (source.TryGetProperty("Secrets", out var secrets) && secrets.ValueKind == JsonValueKind.Array)
        {
            foreach (var secret in secrets.EnumerateArray())
            {
                if (secret.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(secret.GetString()))
                {
                    return true;
                }
            }
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable($"Webhooks__Inbound__Sources__{name}__Secrets__0"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable($"Webhooks__Inbound__Sources__{name}__Secrets__1"));
    }
}

internal readonly record struct AuditFinding(string Severity, string Code, string Subject);
