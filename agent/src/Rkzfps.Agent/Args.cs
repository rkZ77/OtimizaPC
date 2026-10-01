namespace Rkzfps.Agent;

/// <summary>Parser mínimo: posicionais, --flag e --opcao valor. Sem dependência externa no Agent.</summary>
public sealed class Args
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positional { get; } = [];

    public Args(IEnumerable<string> argv, IEnumerable<string> valueOptions)
    {
        var takesValue = valueOptions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var e = argv.GetEnumerator();
        while (e.MoveNext())
        {
            var a = e.Current;
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                Positional.Add(a);
                continue;
            }

            var name = a[2..];
            var eq = name.IndexOf('=');
            if (eq > 0)
                _options[name[..eq]] = name[(eq + 1)..];
            else if (takesValue.Contains(name))
                _options[name] = e.MoveNext() ? e.Current : throw new ArgumentException($"--{name} precisa de um valor.");
            else
                _options[name] = null;
        }
    }

    public bool Flag(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.TryGetValue(name, out var v) ? v : null;

    public int Int(string name, int fallback) => int.TryParse(Get(name), out var v) ? v : fallback;
}
