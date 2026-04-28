using System.Text;

namespace Dragonfire.Caching.Strategies;

/// <summary>
/// Generates cache keys from named method arguments.
/// Replace the default implementation by registering a custom <see cref="ICacheKeyStrategy"/> in DI.
/// Generated proxies pass arguments by name (no <see cref="System.Reflection.MethodInfo"/> reflection).
/// </summary>
public interface ICacheKeyStrategy
{
    /// <summary>
    /// Build a cache key for a method call. When <paramref name="keyTemplate"/> is provided
    /// it takes precedence; otherwise the key is auto-generated as
    /// <c>ServiceName.MethodName(name=value,...)</c>.
    /// </summary>
    /// <param name="serviceName">The owning interface or class name (no namespace).</param>
    /// <param name="methodName">The method name.</param>
    /// <param name="arguments">
    /// Named argument map. The generator only includes parameters that participate in the
    /// key (all parameters by default, or only <c>[CacheKey]</c>-marked ones if present).
    /// Insertion order is preserved when iterated.
    /// </param>
    /// <param name="keyTemplate">Optional template with <c>{name}</c> placeholders.</param>
    string GenerateKey(string serviceName, string methodName,
        IReadOnlyDictionary<string, object?> arguments, string? keyTemplate = null);

    /// <summary>
    /// Resolve a template string (may contain <c>{name}</c> placeholders) using the
    /// supplied argument map. Used for invalidation patterns and tag templates.
    /// </summary>
    string GeneratePattern(string template, IReadOnlyDictionary<string, object?> arguments);
}

/// <summary>Default key strategy: uses templates when provided, otherwise <c>ServiceName.MethodName(name=value,...)</c>.</summary>
public sealed class DefaultCacheKeyStrategy : ICacheKeyStrategy
{
    public string GenerateKey(string serviceName, string methodName,
        IReadOnlyDictionary<string, object?> arguments, string? keyTemplate = null)
    {
        if (!string.IsNullOrEmpty(keyTemplate))
            return ResolveTemplate(keyTemplate!, arguments);

        var sb = new StringBuilder();
        sb.Append(serviceName).Append('.').Append(methodName).Append('(');

        var first = true;
        foreach (var kv in arguments)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(kv.Key).Append('=').Append(GetStableValue(kv.Value));
        }

        sb.Append(')');
        return sb.ToString();
    }

    public string GeneratePattern(string template, IReadOnlyDictionary<string, object?> arguments)
        => ResolveTemplate(template, arguments);

    private static string ResolveTemplate(string template, IReadOnlyDictionary<string, object?> arguments)
    {
        var result = template;
        foreach (var kv in arguments)
            result = result.Replace($"{{{kv.Key}}}", GetStableValue(kv.Value));
        return result;
    }

    private static string GetStableValue(object? arg)
    {
        if (arg is null) return "null";
        var type = arg.GetType();
        return type.IsValueType || type == typeof(string) ? arg.ToString()! : arg.GetHashCode().ToString();
    }
}
