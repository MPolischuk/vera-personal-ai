using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Assistant.Api.Audit;

internal static class ComposeConfigurationAudit
{
    public static IReadOnlyList<AuditFinding> Inspect(string baseComposePath, string overlayComposePath)
    {
        ArgumentNullException.ThrowIfNull(baseComposePath);
        ArgumentNullException.ThrowIfNull(overlayComposePath);

        var missing = new List<AuditFinding>();
        if (!File.Exists(baseComposePath))
        {
            missing.Add(new AuditFinding("error", "compose.missing", "compose.yaml"));
        }

        if (!File.Exists(overlayComposePath))
        {
            missing.Add(new AuditFinding("error", "compose.missing", "compose.production.yaml"));
        }

        if (missing.Count > 0)
        {
            return missing;
        }

        if (!TryLoadMerged(baseComposePath, overlayComposePath, out var merged))
        {
            return [Invalid()];
        }

        return Evaluate(merged);
    }

    public static IReadOnlyDictionary<string, string> ReadServicePins(
        string baseComposePath,
        string overlayComposePath)
    {
        ArgumentNullException.ThrowIfNull(baseComposePath);
        ArgumentNullException.ThrowIfNull(overlayComposePath);

        if (!TryLoadMerged(baseComposePath, overlayComposePath, out var merged))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return ExtractServicePins(merged);
    }

    private static bool TryLoadMerged(string baseComposePath, string overlayComposePath, out YamlValue merged)
    {
        merged = new YamlNull();
        if (!File.Exists(baseComposePath) || !File.Exists(overlayComposePath))
        {
            return false;
        }

        string baseText;
        string overlayText;
        try
        {
            baseText = File.ReadAllText(baseComposePath);
            overlayText = File.ReadAllText(overlayComposePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (ContainsAnchorOrMergeKey(baseText) || ContainsAnchorOrMergeKey(overlayText))
        {
            return false;
        }

        try
        {
            merged = Merge(Parse(baseText), Parse(overlayText), key: null);
            return true;
        }
        catch (Exception exception) when (exception is YamlException or InvalidDataException)
        {
            return false;
        }
    }

    private static AuditFinding Invalid()
        => new("error", "compose.invalid", "compose.yaml");

    private static bool ContainsAnchorOrMergeKey(string text)
    {
        try
        {
            var parser = new Parser(new StringReader(text));
            while (parser.MoveNext())
            {
                switch (parser.Current)
                {
                    case AnchorAlias:
                        return true;
                    case NodeEvent node when !node.Anchor.IsEmpty:
                        return true;
                    case Scalar scalar when scalar.Value == "<<":
                        return true;
                }
            }
        }
        catch (YamlException)
        {
            return true;
        }

        return false;
    }

    private static YamlValue Parse(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count != 1)
        {
            throw new InvalidDataException("Compose YAML must contain one document.");
        }

        return Convert(stream.Documents[0].RootNode);
    }

    private static YamlValue Convert(YamlNode node)
    {
        var reset = IsReset(node);
        YamlValue value = node switch
        {
            YamlScalarNode scalar => new YamlScalar(scalar.Value ?? string.Empty),
            YamlSequenceNode sequence => new YamlSeq(sequence.Children.Select(Convert).ToList()),
            YamlMappingNode mapping => new YamlMap(ToMap(mapping)),
            _ => new YamlNull(),
        };
        return reset ? value with { Reset = true } : value;
    }

    private static Dictionary<string, YamlValue> ToMap(YamlMappingNode mapping)
    {
        var entries = new Dictionary<string, YamlValue>(StringComparer.Ordinal);
        foreach (var (key, value) in mapping.Children)
        {
            if (key is not YamlScalarNode scalar || scalar.Value is null)
            {
                throw new InvalidDataException("Compose map keys must be scalars.");
            }

            entries[scalar.Value] = Convert(value);
        }

        return entries;
    }

    private static bool IsReset(YamlNode node)
    {
        var tag = node.Tag.ToString();
        return string.Equals(tag, "!reset", StringComparison.Ordinal);
    }

    private static YamlValue Merge(YamlValue? baseline, YamlValue overlay, string? key)
    {
        if (overlay.Reset)
        {
            return overlay with { Reset = false };
        }

        if (baseline is YamlMap baseMap && overlay is YamlMap overlayMap)
        {
            var entries = new Dictionary<string, YamlValue>(baseMap.Entries, StringComparer.Ordinal);
            foreach (var (childKey, child) in overlayMap.Entries)
            {
                entries.TryGetValue(childKey, out var existing);
                entries[childKey] = Merge(existing, child, childKey);
            }

            return new YamlMap(entries);
        }

        if (baseline is YamlSeq baseSeq && overlay is YamlSeq overlaySeq && Appends(key))
        {
            var items = new List<YamlValue>(baseSeq.Items.Count + overlaySeq.Items.Count);
            items.AddRange(baseSeq.Items);
            items.AddRange(overlaySeq.Items);
            return new YamlSeq(items);
        }

        return overlay with { Reset = false };
    }

    private static bool Appends(string? key)
        => key is "ports" or "expose" or "volumes" or "dns" or "dns_search" or "tmpfs" or "external_links";

    private static List<AuditFinding> Evaluate(YamlValue root)
    {
        var findings = new List<AuditFinding>();
        if (root is not YamlMap rootMap
            || !rootMap.Entries.TryGetValue("services", out var servicesNode)
            || servicesNode is not YamlMap services)
        {
            return findings;
        }

        foreach (var (name, serviceNode) in services.Entries)
        {
            if (serviceNode is not YamlMap service)
            {
                continue;
            }

            if (ContainsDockerSocket(service))
            {
                findings.Add(new AuditFinding("error", "compose.docker_socket", name));
            }

            if (TryGetImage(service, out var image) && IsLatestTag(image))
            {
                findings.Add(new AuditFinding("error", "compose.image_latest", name));
            }

            if (name is "db" or "dashboard" && HasPublishedPort(service) && !IsOnlyDisabledProfile(service))
            {
                findings.Add(new AuditFinding("error", "compose.published_port", name));
            }
        }

        if (HasBakedBearer(services))
        {
            findings.Add(new AuditFinding("error", "compose.baked_bearer", "api"));
        }

        return findings;
    }

    private static Dictionary<string, string> ExtractServicePins(YamlValue root)
    {
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root is not YamlMap rootMap
            || !rootMap.Entries.TryGetValue("services", out var servicesNode)
            || servicesNode is not YamlMap services)
        {
            return pins;
        }

        foreach (var (name, serviceNode) in services.Entries)
        {
            if (serviceNode is not YamlMap service)
            {
                continue;
            }

            if (service.Entries.ContainsKey("build") && !TryGetImage(service, out _))
            {
                pins[name] = "build";
                continue;
            }

            if (TryGetImage(service, out var image))
            {
                var pin = ImagePin(image);
                if (pin is not null)
                {
                    pins[name] = pin;
                }
            }
        }

        return pins;
    }

    private static bool TryGetImage(YamlMap service, out string image)
    {
        image = string.Empty;
        if (!service.Entries.TryGetValue("image", out var node) || node is not YamlScalar scalar)
        {
            return false;
        }

        image = scalar.Value.Trim();
        return image.Length > 0;
    }

    private static bool IsLatestTag(string image)
        => string.Equals(ImageTag(image), "latest", StringComparison.Ordinal);

    private static string? ImagePin(string image)
        => ImageTag(image) ?? ImageDigest(image);

    private static string? ImageTag(string image)
    {
        var name = StripDigest(image.Trim());
        var slash = name.LastIndexOf('/');
        var colon = name.LastIndexOf(':');
        if (colon < 0 || colon < slash)
        {
            return null;
        }

        var tag = name[(colon + 1)..];
        return tag.Length == 0 ? null : tag;
    }

    private static string? ImageDigest(string image)
    {
        var text = image.Trim();
        var at = text.LastIndexOf('@');
        if (at < 0 || at >= text.Length - 1)
        {
            return null;
        }

        return text[(at + 1)..];
    }

    private static string StripDigest(string image)
    {
        var at = image.LastIndexOf('@');
        return at < 0 ? image : image[..at];
    }

    private static bool HasPublishedPort(YamlMap service)
        => service.Entries.TryGetValue("ports", out var ports)
           && ports is YamlSeq sequence
           && sequence.Items.Count > 0;

    private static bool IsOnlyDisabledProfile(YamlMap service)
        => service.Entries.TryGetValue("profiles", out var profiles)
           && profiles is YamlSeq sequence
           && sequence.Items.Count == 1
           && sequence.Items[0] is YamlScalar { Value: "disabled" };

    private static bool HasBakedBearer(YamlMap services)
        => services.Entries.TryGetValue("api", out var api)
           && api is YamlMap apiMap
           && apiMap.Entries.TryGetValue("build", out var build)
           && build is YamlMap buildMap
           && buildMap.Entries.TryGetValue("args", out var args)
           && args is YamlMap argsMap
           && argsMap.Entries.ContainsKey("VITE_BEARER_TOKEN");

    private static bool ContainsDockerSocket(YamlMap service)
    {
        if (!service.Entries.TryGetValue("volumes", out var volumes) || volumes is not YamlSeq sequence)
        {
            return false;
        }

        foreach (var item in sequence.Items)
        {
            if (ContainsSocket(item))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSocket(YamlValue node)
    {
        switch (node)
        {
            case YamlScalar scalar:
                return scalar.Value.Contains("docker.sock", StringComparison.Ordinal);
            case YamlMap map:
                foreach (var child in map.Entries.Values)
                {
                    if (ContainsSocket(child))
                    {
                        return true;
                    }
                }

                return false;
            case YamlSeq sequence:
                foreach (var child in sequence.Items)
                {
                    if (ContainsSocket(child))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private abstract record YamlValue
    {
        public bool Reset { get; init; }
    }

    private sealed record YamlScalar(string Value) : YamlValue;

    private sealed record YamlMap(Dictionary<string, YamlValue> Entries) : YamlValue;

    private sealed record YamlSeq(List<YamlValue> Items) : YamlValue;

    private sealed record YamlNull : YamlValue;
}
