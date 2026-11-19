using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace VpsOperator.Mcp;

[McpServerToolType]
internal sealed class OperatorTools
{
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Backup = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Upgrade = TimeSpan.FromMinutes(6);
    private static readonly TimeSpan Rotate = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Restart = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Migrate = TimeSpan.FromMinutes(15);

    private static readonly string[] InspectFields =
    [
        "image", "status", "health", "autoupdate_defined", "custom_compose", "soketi",
        "realtime_present", "published_6001", "published_6002", "coolify_env_mode",
        "application_env_mode", "application_compose_mode", "trust_stores", "third_party_images",
    ];

    private static readonly string[] AliasFields = ["listeners", "same_name"];
    private static readonly string[] InstanceFields = ["path", "bytes", "toc_lines"];
    private static readonly string[] AssistantBackupFields = ["ok", "exit", "key"];
    private static readonly string[] UpgradeFields = ["image", "status", "health", "autoupdate_defined"];
    private static readonly string[] TokenFields = ["tokens"];
    private static readonly string[] RotateFields =
    [
        "new_id", "expires_at", "abilities", "status_http", "alias_count", "token_length",
        "db_env", "env_mode", "compose_mode", "revoked_id", "revoke_skipped", "stale_stopped",
    ];

    private static readonly string[] RevokeFields = ["revoked_id"];
    private static readonly string[] AuditFields = ["events"];
    private static readonly string[] RestartFields = ["status_http", "alias_count", "refused", "via"];
    private static readonly string[] DeployFields = ["status_http", "alias_count", "refused", "via"];
    private static readonly string[] DbInspectFields =
    [
        "image", "extension", "extversion", "role", "role_exists", "rolsuper", "rolcanlogin",
        "member_of_admin", "index", "index_exists",
    ];
    private static readonly string[] DbExtensionFields = ["extension", "extversion"];
    private static readonly string[] DbReindexFields = ["index", "reindexed"];
    private static readonly string[] DbRoleFields = ["role", "rolsuper", "member_of_admin", "env", "api_redeployed"];
    private static readonly string[] DbImageFields = ["image", "recreated", "volume_unchanged"];
    private static readonly string[] MigrateFields = ["applied", "head"];
    private static readonly string[] ConfigurationAuditFields = ["exit_code", "output"];
    private static readonly string[] LogScanFields = ["matches"];

    private readonly IRemoteShell shell;
    private readonly IVpsEndpoint endpoint;
    private readonly ITcpProbe probe;

    public OperatorTools(IRemoteShell shell, IVpsEndpoint endpoint, ITcpProbe probe)
    {
        this.shell = shell;
        this.endpoint = endpoint;
        this.probe = probe;
    }

    [McpServerTool(Name = "host_inspect", ReadOnly = true, OpenWorld = false)]
    [Description("Versión y salud de Coolify, AUTOUPDATE, soketi, coolify-realtime, puertos 6001-6002, modo de los env, ca-certificates de la API y de coolify-mcp, y la fecha de imagen de Aspire Dashboard y del MCP de clima. No muestra secretos ni el almacén de certificados.")]
    public async Task<CallToolResult> HostInspect(CancellationToken cancellationToken)
    {
        var remoteTask = shell.RunAsync(new RemoteRequest("host-inspect.sh", [], TimeSpan.FromSeconds(90)), cancellationToken);
        var probeTask = ProbePortsAsync(cancellationToken);
        await Task.WhenAll(remoteTask, probeTask).ConfigureAwait(false);
        var output = await remoteTask.ConfigureAwait(false);
        var ports = await probeTask.ConfigureAwait(false);
        var shaped = ResultShaper.TryShape(output.Stdout, InspectFields);
        if (output.ExitCode != 0 || shaped is null)
        {
            return ToolResults.Fail(FailureText(output, shaped));
        }

        var node = JsonNode.Parse(shaped)!.AsObject();
        node["probe_host"] = JsonValue.Create(ports.Host ?? "unresolved");
        node["probe_6001"] = JsonValue.Create(ports.Port6001);
        node["probe_6002"] = JsonValue.Create(ports.Port6002);
        return ToolResults.Ok(OutputRedactor.Redact(node.ToJsonString()));
    }

    [McpServerTool(Name = "coolify_mcp_alias", ReadOnly = true, OpenWorld = false)]
    [Description("Qué contenedor tiene el alias coolify-mcp y qué otros nombres coinciden. El largo del token, nunca el valor.")]
    public Task<CallToolResult> CoolifyMcpAlias(CancellationToken cancellationToken) =>
        RunAsync("mcp-alias.sh", [], Quick, AliasFields, cancellationToken);

    [McpServerTool(Name = "coolify_instance_backup", ReadOnly = false, OpenWorld = false)]
    [Description("Dump de la base de Coolify en /home/operator/coolify-instance-backups. Devuelve ruta, bytes y líneas del TOC.")]
    public async Task<CallToolResult> CoolifyInstanceBackup(CancellationToken cancellationToken)
    {
        var result = await RunAsync("instance-backup.sh", [], Backup, InstanceFields, cancellationToken).ConfigureAwait(false);
        if (result.IsError == true)
        {
            return result;
        }

        var text = Text(result);
        var path = JsonNode.Parse(text)?["path"]?.GetValue<string>();
        if (!BackupKey.IsInstanceDump(path))
        {
            return ToolResults.Fail("The instance backup did not return a recognized dump path.");
        }

        return result;
    }

    [McpServerTool(Name = "assistant_db_backup", ReadOnly = false, OpenWorld = false)]
    [Description("Corre scripts/backup-db.sh. Devuelve el exit y la clave daily/assistant-AAAAMMDD-HHMMSS.sql.age, nunca el secreto.")]
    public async Task<CallToolResult> AssistantDbBackup(CancellationToken cancellationToken)
    {
        var result = await RunAsync("assistant-backup.sh", [], Backup, AssistantBackupFields, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsError == true)
        {
            return result;
        }

        try
        {
            if (JsonNode.Parse(Text(result)) is not JsonObject node)
            {
                return ToolResults.Fail("The backup result was not JSON.");
            }

            var key = node["key"]?.GetValue<string>();
            if (!BackupKey.IsAssistantDaily(key))
            {
                node["key"] = null;
                node["ok"] = false;
                return ToolResults.Fail(OutputRedactor.Redact(node.ToJsonString()));
            }
        }
        catch (JsonException)
        {
            return ToolResults.Fail("The backup result was not JSON.");
        }

        return result;
    }

    [McpServerTool(Name = "coolify_upgrade", ReadOnly = false, OpenWorld = false)]
    [Description("Sube Coolify a una 4.4.N ≥ 4.4.7 y deja AUTOUPDATE sin definir. confirm tiene que ser esa misma versión. Se niega si hay soketi, si la versión es anterior a 4.4.7, o si el panel ya está en una versión mayor.")]
    public Task<CallToolResult> CoolifyUpgrade(
        [Description("Versión 4.4.N, como mínimo 4.4.7.")] string version,
        [Description("La misma versión, repetida para confirmar.")] string confirm,
        CancellationToken cancellationToken)
    {
        if (!VersionPin.IsCoolify44(version) || !string.Equals(confirm, version, StringComparison.Ordinal))
        {
            return Task.FromResult(ToolResults.Fail("confirm must be the 4.4.N version to install, at least 4.4.7."));
        }

        return RunAsync("coolify-upgrade.sh", [version], Upgrade, UpgradeFields, cancellationToken);
    }

    [McpServerTool(Name = "coolify_token_list", ReadOnly = true, OpenWorld = false)]
    [Description("Tokens de Coolify: id, abilities, vencimiento y si expiró. Sin hash y sin secreto. Un token revocado no aparece.")]
    public Task<CallToolResult> CoolifyTokenList(CancellationToken cancellationToken) =>
        RunAsync("token-list.sh", [], Quick, TokenFields, cancellationToken);

    [McpServerTool(Name = "coolify_token_rotate", ReadOnly = false, OpenWorld = false)]
    [Description("Crea un token read+deploy con vencimiento, lo instala en el env del MCP, comprueba la API y solo entonces revoca el id anterior. El valor no vuelve. confirm tiene que ser rotate.")]
    public Task<CallToolResult> CoolifyTokenRotate(
        [Description("Escribí rotate para confirmar.")] string confirm,
        [Description("Id del token que se revoca recién después de un GET 200.")] int revokeId,
        CancellationToken cancellationToken,
        [Description("Días de vigencia, de 1 a 90. Por defecto 90.")] int? validDays = null,
        [Description("Si es true, para assistant-coolify-mcp y le pone restart=no.")] bool? stopStale = null)
    {
        var days = validDays ?? 90;
        if (!string.Equals(confirm, "rotate", StringComparison.Ordinal) || days is < 1 or > 90 || revokeId <= 0)
        {
            return Task.FromResult(ToolResults.Fail(
                "confirm must be rotate, valid_days must be 1..90, and revoke_id must be the current token id."));
        }

        return RunAsync(
            "token-rotate.sh",
            [days.ToString(System.Globalization.CultureInfo.InvariantCulture), revokeId.ToString(System.Globalization.CultureInfo.InvariantCulture), stopStale == true ? "1" : "0"],
            Rotate,
            RotateFields,
            cancellationToken);
    }

    [McpServerTool(Name = "coolify_token_revoke", ReadOnly = false, OpenWorld = false)]
    [Description("Revoca un token por id. Se niega si es el único read+deploy vigente. confirm tiene que ser revoke:<id>.")]
    public Task<CallToolResult> CoolifyTokenRevoke(
        [Description("revoke:<id>, por ejemplo revoke:3.")] string confirm,
        [Description("Id del token.")] int tokenId,
        CancellationToken cancellationToken)
    {
        if (tokenId <= 0 || !string.Equals(confirm, "revoke:" + tokenId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return Task.FromResult(ToolResults.Fail("confirm must be revoke:<id>."));
        }

        return RunAsync(
            "token-revoke.sh",
            [tokenId.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Quick,
            RevokeFields,
            cancellationToken);
    }

    [McpServerTool(Name = "coolify_audit", ReadOnly = true, OpenWorld = false)]
    [Description("Auditoría de Coolify filtrada por id de token y por restarted o ability_denied. Sin metadata, mail, IP ni user agent.")]
    public Task<CallToolResult> CoolifyAudit(
        [Description("Id del token.")] int tokenId,
        [Description("restarted o ability_denied.")] string action,
        CancellationToken cancellationToken)
    {
        if (tokenId <= 0 || action is not ("restarted" or "ability_denied"))
        {
            return Task.FromResult(ToolResults.Fail("token_id is required and action must be restarted or ability_denied."));
        }

        return RunAsync(
            "coolify-audit.sh",
            [tokenId.ToString(System.Globalization.CultureInfo.InvariantCulture), action],
            Quick,
            AuditFields,
            cancellationToken);
    }

    [McpServerTool(Name = "assistant_restart", ReadOnly = false, OpenWorld = false)]
    [Description("Reinicia la app assistant por la API de Coolify, para que quede el evento en Coolify. No muestra la tarjeta de Vera. confirm tiene que ser restart-assistant.")]
    public Task<CallToolResult> AssistantRestart(
        [Description("Escribí restart-assistant para confirmar.")] string confirm,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(confirm, "restart-assistant", StringComparison.Ordinal))
        {
            return Task.FromResult(ToolResults.Fail("confirm must be restart-assistant."));
        }

        return RunAsync("assistant-restart.sh", [], Restart, RestartFields, cancellationToken);
    }

    [McpServerTool(Name = "assistant_deploy", ReadOnly = false, OpenWorld = false)]
    [Description("Reconstruye la app assistant por la API de Coolify, para tomar la imagen base flotante. No muestra la tarjeta de Vera. confirm tiene que ser deploy-assistant.")]
    public Task<CallToolResult> AssistantDeploy(
        [Description("Escribí deploy-assistant para confirmar.")] string confirm,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(confirm, "deploy-assistant", StringComparison.Ordinal))
        {
            return Task.FromResult(ToolResults.Fail("confirm must be deploy-assistant."));
        }

        return RunAsync("assistant-deploy.sh", [], Restart, DeployFields, cancellationToken);
    }

    [McpServerTool(Name = "assistant_health", ReadOnly = true, OpenWorld = false)]
    [Description("Estado exacto running/healthy de la API. Si falló por una migración pendiente, dice el nombre. No la aplica.")]
    public async Task<CallToolResult> AssistantHealth(CancellationToken cancellationToken)
    {
        RemoteOutput output;
        try
        {
            output = await shell.RunAsync(new RemoteRequest("assistant-health.sh", [], Quick), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return ToolResults.Fail(OutputRedactor.Redact(exception.Message));
        }

        var stdout = output.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var marker = stdout.IndexOf("\nLOG\n", StringComparison.Ordinal);
        var jsonPart = (marker < 0 ? stdout : stdout[..marker]).Trim();
        var log = marker < 0 ? string.Empty : stdout[(marker + "\nLOG\n".Length)..];
        JsonObject? node;
        try
        {
            node = JsonNode.Parse(string.IsNullOrWhiteSpace(jsonPart) ? "{}" : jsonPart) as JsonObject;
        }
        catch (JsonException)
        {
            node = null;
        }

        if (node is null || (output.ExitCode != 0 && node["status"] is null))
        {
            return ToolResults.Fail(FailureText(output, null));
        }

        var status = node["status"]?.GetValue<string>();
        var health = node["health"]?.GetValue<string>();
        var healthy = HealthReport.IsHealthy(status, health);
        var migration = healthy ? null : HealthReport.FindMigration(log);
        var report = new JsonObject
        {
            ["status"] = JsonValue.Create(status),
            ["health"] = JsonValue.Create(health),
            ["exit_code"] = node["exit_code"]?.DeepClone(),
            ["image"] = node["image"]?.DeepClone(),
            ["runtime"] = node["runtime"]?.DeepClone(),
            ["healthy"] = JsonValue.Create(healthy),
            ["pending_migration"] = JsonValue.Create(migration),
            ["migration_signal"] = JsonValue.Create(!healthy && HealthReport.MentionsMigration(log)),
        };
        if (!healthy)
        {
            report["log_excerpt"] = JsonValue.Create(OutputRedactor.Redact(HealthReport.Excerpt(log)));
        }

        return ToolResults.Ok(OutputRedactor.Redact(report.ToJsonString()));
    }

    [McpServerTool(Name = "assistant_audit", ReadOnly = true, OpenWorld = false)]
    [Description("Corre dotnet Assistant.Api.dll audit dentro del contenedor de la API. Devuelve la salida y el exit code. No muestra secretos.")]
    public Task<CallToolResult> AssistantAudit(CancellationToken cancellationToken) =>
        RunAsync("assistant-audit.sh", [], Quick, ConfigurationAuditFields, cancellationToken);

    [McpServerTool(Name = "assistant_log_scan", ReadOnly = true, OpenWorld = false)]
    [Description("Cuenta líneas de log de la API que contienen api.telegram.org/bot seguido de dígitos. No devuelve la línea: tiene el token. Cero es el resultado bueno.")]
    public Task<CallToolResult> AssistantLogScan(CancellationToken cancellationToken) =>
        RunAsync("assistant-log-scan.sh", [], Restart, LogScanFields, cancellationToken);

    [McpServerTool(Name = "assistant_db_inspect", ReadOnly = true, OpenWorld = false)]
    [Description("Versión de una extensión ya instalada, flags del rol y si el índice existe. Sin contraseñas. Por defecto vector, assistant_app e ix_memories_embedding.")]
    public Task<CallToolResult> AssistantDbInspect(
        CancellationToken cancellationToken,
        [Description("Nombre de la extensión. Por defecto vector.")] string? extension = null,
        [Description("Nombre del rol. Por defecto assistant_app.")] string? role = null,
        [Description("Nombre del índice. Por defecto ix_memories_embedding.")] string? index = null)
    {
        var extensionName = extension ?? "vector";
        var roleName = role ?? "assistant_app";
        var indexName = index ?? "ix_memories_embedding";
        if (!PostgresName.IsIdentifier(extensionName)
            || !PostgresName.IsIdentifier(roleName)
            || !PostgresName.IsIdentifier(indexName))
        {
            return Task.FromResult(ToolResults.Fail("extension, role, and index must be lowercase Postgres identifiers."));
        }

        return RunAsync(
            "assistant-db-inspect.sh",
            [extensionName, roleName, indexName],
            Quick,
            DbInspectFields,
            cancellationToken);
    }

    [McpServerTool(Name = "assistant_db_extension_update", ReadOnly = false, OpenWorld = false)]
    [Description("ALTER EXTENSION UPDATE de una extensión que ya está instalada. No la crea ni la borra. confirm tiene que ser update-extension:<nombre>.")]
    public Task<CallToolResult> AssistantDbExtensionUpdate(
        [Description("Nombre de la extensión, por ejemplo vector.")] string extension,
        [Description("update-extension:<nombre>, por ejemplo update-extension:vector.")] string confirm,
        CancellationToken cancellationToken)
    {
        if (!PostgresName.IsIdentifier(extension)
            || !string.Equals(confirm, "update-extension:" + extension, StringComparison.Ordinal))
        {
            return Task.FromResult(ToolResults.Fail("confirm must be update-extension:<name>."));
        }

        return RunAsync("assistant-db-extension.sh", [extension], Backup, DbExtensionFields, cancellationToken);
    }

    [McpServerTool(Name = "assistant_db_reindex", ReadOnly = false, OpenWorld = false)]
    [Description("REINDEX INDEX CONCURRENTLY de un índice que ya existe, fuera de una transacción. confirm tiene que ser reindex:<indice>.")]
    public Task<CallToolResult> AssistantDbReindex(
        [Description("Nombre del índice, por ejemplo ix_memories_embedding.")] string index,
        [Description("reindex:<indice>.")] string confirm,
        CancellationToken cancellationToken)
    {
        if (!PostgresName.IsIdentifier(index)
            || !string.Equals(confirm, "reindex:" + index, StringComparison.Ordinal))
        {
            return Task.FromResult(ToolResults.Fail("confirm must be reindex:<index>."));
        }

        return RunAsync("assistant-db-reindex.sh", [index], Upgrade, DbReindexFields, cancellationToken);
    }

    [McpServerTool(Name = "assistant_db_runtime_role", ReadOnly = false, OpenWorld = false)]
    [Description("Crea o actualiza assistant_app sin SUPERUSER y sin imprimir la contraseña. No redeploya la API. confirm tiene que ser ensure-runtime-role.")]
    public Task<CallToolResult> AssistantDbRuntimeRole(
        [Description("Escribí ensure-runtime-role para confirmar.")] string confirm,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(confirm, "ensure-runtime-role", StringComparison.Ordinal))
        {
            return Task.FromResult(ToolResults.Fail("confirm must be ensure-runtime-role."));
        }

        return RunAsync("assistant-db-role.sh", [], Backup, DbRoleFields, cancellationToken);
    }

    [McpServerTool(Name = "assistant_db_image", ReadOnly = false, OpenWorld = false)]
    [Description("Recrea sólo el contenedor db con pgvector 0.8.7-pg17 fijado. Conserva el volumen y no redeploya la API. confirm tiene que ser pin-db-image:0.8.7-pg17.")]
    public Task<CallToolResult> AssistantDbImage(
        [Description("Escribí pin-db-image:0.8.7-pg17 para confirmar.")] string confirm,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(confirm, "pin-db-image:" + PgVectorPin.Tag, StringComparison.Ordinal))
        {
            return Task.FromResult(ToolResults.Fail("confirm must be pin-db-image:0.8.7-pg17."));
        }

        return RunAsync("assistant-db-image.sh", [PgVectorPin.Tag], Upgrade, DbImageFields, cancellationToken);
    }

    [McpServerTool(Name = "assistant_db_migrate", ReadOnly = false, OpenWorld = false)]
    [Description("Aplica las migraciones pendientes con dotnet ef como POSTGRES_USER. No usa assistant_app, no hace GRANT y no despliega la API. confirm tiene que ser migrate-assistant.")]
    public Task<CallToolResult> AssistantDbMigrate(
        [Description("Escribí migrate-assistant para confirmar.")] string confirm,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(confirm, "migrate-assistant", StringComparison.Ordinal))
        {
            return Task.FromResult(ToolResults.Fail("confirm must be migrate-assistant."));
        }

        return RunAsync("assistant-db-migrate.sh", [], Migrate, MigrateFields, cancellationToken);
    }

    [McpServerTool(Name = "assistant_tool_audit", ReadOnly = true, OpenWorld = false)]
    [Description("Auditoría de Vera para coolify_control: fecha, evento y resultado. Sin el texto del usuario.")]
    public Task<CallToolResult> AssistantToolAudit(CancellationToken cancellationToken) =>
        RunAsync("assistant-tool-audit.sh", [], Quick, AuditFields, cancellationToken);

    private async Task<CallToolResult> RunAsync(
        string script,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string[] fields,
        CancellationToken cancellationToken)
    {
        if (arguments.Any(argument => !ScriptArguments.IsSafe(argument)))
        {
            return ToolResults.Fail("An argument was rejected before the remote call.");
        }

        RemoteOutput output;
        try
        {
            output = await shell.RunAsync(new RemoteRequest(script, arguments, timeout), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return ToolResults.Fail(OutputRedactor.Redact(exception.Message));
        }

        var shaped = ResultShaper.TryShape(output.Stdout, fields);
        if (output.ExitCode != 0)
        {
            return ToolResults.Fail(FailureText(output, shaped));
        }

        return shaped is null
            ? ToolResults.Fail("The remote result was not the expected JSON.")
            : ToolResults.Ok(OutputRedactor.Redact(shaped));
    }

    private async Task<(string? Host, string Port6001, string Port6002)> ProbePortsAsync(CancellationToken cancellationToken)
    {
        var host = await endpoint.ResolveHostAsync(cancellationToken).ConfigureAwait(false);
        if (host is null)
        {
            return (null, "unresolved", "unresolved");
        }

        var first = probe.ProbeAsync(host, 6001, cancellationToken);
        var second = probe.ProbeAsync(host, 6002, cancellationToken);
        await Task.WhenAll(first, second).ConfigureAwait(false);
        return (host, await first.ConfigureAwait(false), await second.ConfigureAwait(false));
    }

    private static string FailureText(RemoteOutput output, string? shaped)
    {
        var stderr = OutputRedactor.Redact(output.Stderr);
        var detail = string.IsNullOrWhiteSpace(stderr) ? "The remote command failed." : stderr;
        if (shaped is not null)
        {
            detail = OutputRedactor.Redact(shaped) + "\n" + detail;
        }

        return detail;
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content?.Select(block => block is TextContentBlock text ? text.Text : string.Empty) ?? []);
}
