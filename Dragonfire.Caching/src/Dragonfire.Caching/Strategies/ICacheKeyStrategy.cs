using System.Reflection;
using System.Text;
using Dragonfire.Caching.Attributes;

namespace Dragonfire.Caching.Strategies;

/// <summary>
/// Generates cache keys from method signatures and arguments.
/// Replace the default implementation by registering a custom <see cref="ICacheKeyStrategy"/> in DI.
/// </summary>
public interface ICacheKeyStrategy
{
    /// <summary>
    /// Build a cache key for a method call. When <paramref name="keyTemplate"/> is provided
    /// it takes precedence; otherwise the key is auto-generated.
    /// </summary>
    string GenerateKey(MethodInfo method, object?[] arguments, string? keyTemplate = null);

    /// <summary>Resolve a cache key/pattern string (may contain <c>{name}</c> placeholders).</summary>
    string GeneratePattern(MethodInfo method, object?[] arguments, string pattern);
}

/// <summary>Default key strategy: uses templates when provided, otherwise <c>Type.Method(param=value,...)</c>.</summary>
public sealed class DefaultCacheKeyStrategy : ICacheKeyStrategy
{
    public string GenerateKey(MethodInfo method, object?[] arguments, string? keyTemplate = null)
    {
        if (!string.IsNullOrEmpty(keyTemplate))
            return ResolveTemplate(method, arguments, keyTemplate);

        var sb = new StringBuilder();
        sb.Append(method.DeclaringType?.Name).Append('.').Append(method.Name).Append('(');

        var parameters = method.GetParameters();
        var cacheKeyParams = parameters
            .Select((p, i) => (p, i))
            .Where(x => x.p.GetCustomAttribute<CacheKeyAttribute>() != null || !parameters.Any(p => p.GetCustomAttribute<CacheKeyAttribute>() != null))
            .ToArray();

        for (int j = 0; j < cacheKeyParams.Length; j++)
        {
            if (j > 0) sb.Append(',');
            var (p, i) = cacheKeyParams[j];
            var alias = p.GetCustomAttribute<CacheKeyAttribute>()?.Name ?? p.Name;
            sb.Append(alias).Append('=').Append(GetStableValue(arguments.ElementAtOrDefault(i)));
        }

        sb.Append(')');
        return sb.ToString();
    }

    public string GeneratePattern(MethodInfo method, object?[] arguments, string pattern)
        => ResolveTemplate(method, arguments, pattern);

    private static string ResolveTemplate(MethodInfo method, object?[] arguments, string template)
    {
        var result = template;
        var parameters = method.GetParameters();

        for (int i = 0; i < parameters.Length; i++)
        {
            result = result
                .Replace($"{{{parameters[i].Name}}}", GetStableValue(arguments.ElementAtOrDefault(i)))
                .Replace($"{{{i}}}", GetStableValue(arguments.ElementAtOrDefault(i)));
        }

        return result;
    }

    private static string GetStableValue(object? arg)
    {
        if (arg is null) return "null";
        var type = arg.GetType();
        return type.IsValueType || type == typeof(string) ? arg.ToString()! : arg.GetHashCode().ToString();
    }
}
