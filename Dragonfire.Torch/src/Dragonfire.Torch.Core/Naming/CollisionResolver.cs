namespace Dragonfire.Torch.Naming;

/// <summary>
/// Tracks names already handed out and disambiguates duplicates by appending
/// <c>_2</c>, <c>_3</c>, … . Names are compared with ordinal case sensitivity —
/// matching the C# identifier model.
/// </summary>
public sealed class CollisionResolver
{
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    public string Reserve(string name)
    {
        if (!_seen.TryGetValue(name, out var count))
        {
            _seen[name] = 1;
            return name;
        }

        count++;
        _seen[name] = count;
        return $"{name}_{count}";
    }
}
