using System.Collections.Concurrent;
using System.Reflection;
using Dragonfire.Caching.Interfaces;

namespace Dragonfire.Caching.Core;

/// <summary>
/// Resolves <c>{PropertyName}</c> placeholders in cache key/tag templates using
/// the public properties of a parameter object.
/// </summary>
internal sealed class TemplateResolver : ITemplateResolver
{
    private readonly ConcurrentDictionary<Type, PropertyInfo[]> _propCache = new();

    public string Resolve(string template, object? parameters)
    {
        if (parameters is null) return template;

        var props = _propCache.GetOrAdd(parameters.GetType(), static t => t.GetProperties());
        var result = template;

        foreach (var prop in props)
        {
            var value = prop.GetValue(parameters)?.ToString() ?? "null";
            result = result.Replace($"{{{prop.Name}}}", value);
        }

        return result;
    }
}
