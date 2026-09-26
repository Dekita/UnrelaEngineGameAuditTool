namespace DekUnrealGameAudit.Commands;

/// <summary>Minimal flag parser: supports `--key value`, `--key=value`, repeated `--key`, and `-o` as a shorthand for `--out`.</summary>
public class ArgMap {
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);

    public static ArgMap Parse(IEnumerable<string> args) {
        var map = new ArgMap();
        string? current = null;

        foreach (var raw in args) {
            var arg = raw == "-o" ? "--out" : raw;

            if (arg.StartsWith("--")) {
                var body = arg[2..];
                var eq = body.IndexOf('=');
                if (eq >= 0) {
                    map.Add(body[..eq], body[(eq + 1)..]);
                    current = null;
                } else {
                    current = body;
                    map.Ensure(current);
                }
            } else if (current != null) {
                map.Add(current, arg);
            } else {
                throw new ArgumentException($"Unexpected argument: {arg}");
            }
        }

        return map;
    }

    private void Ensure(string key) {
        if (!_values.ContainsKey(key))
            _values[key] = new List<string>();
    }

    private void Add(string key, string value) {
        Ensure(key);
        _values[key].Add(value);
    }

    public List<string> Many(string key) => _values.TryGetValue(key, out var v) ? v : new List<string>();

    /// <summary>Every current caller treats this key as singleton - a repeated flag (e.g. two `--out`s) is
    /// always a mistake, not an intentional "last one wins"/"first one wins" override, so this throws instead
    /// of silently picking one and discarding the rest. A present-but-blank value (`--out ""`) is treated the
    /// same as not having been given at all, matching what every caller already does with the result anyway.</summary>
    public string? OneOrDefault(string key) {
        if (!_values.TryGetValue(key, out var v) || v.Count == 0)
            return null;
        if (v.Count > 1)
            throw new ArgumentException($"--{key} was given {v.Count} times - it only accepts one value.");
        return string.IsNullOrWhiteSpace(v[0]) ? null : v[0];
    }

    public string RequireOne(string key) =>
        OneOrDefault(key) ?? throw new ArgumentException($"Missing required argument: --{key}");

    /// <summary>For a flag that's allowed to repeat (e.g. multiple `--mods` roots, the same way `--aes` already
    /// allows multiple keys) and must have at least one non-blank value.</summary>
    public List<string> RequireMany(string key) {
        var values = Many(key).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (values.Count == 0)
            throw new ArgumentException($"Missing required argument: --{key}");
        return values;
    }

    /// <summary>Call at the top of a command's Run(), listing every flag it recognizes - `--aes` is inherently
    /// multi-value/shared across commands so it isn't included in the per-command "used" set some callers pass
    /// here. Turns a typoed flag (e.g. `--paks-folder` for `--paks`) from a silently-ignored no-op into a
    /// concise, immediate error instead of a confusing downstream "missing required argument".</summary>
    public void EnsureKnownKeys(params string[] known) {
        var unknown = _values.Keys.Except(known, StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown argument(s): {string.Join(", ", unknown.Select(k => $"--{k}"))}");
    }
}
