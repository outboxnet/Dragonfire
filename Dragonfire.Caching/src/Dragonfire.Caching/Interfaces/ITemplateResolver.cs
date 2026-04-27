namespace Dragonfire.Caching.Interfaces;

/// <summary>
/// Resolves <c>{PropertyName}</c> placeholders in cache key/tag templates using
/// the properties of a given parameter object.
/// </summary>
public interface ITemplateResolver
{
    string Resolve(string template, object? parameters);
}
