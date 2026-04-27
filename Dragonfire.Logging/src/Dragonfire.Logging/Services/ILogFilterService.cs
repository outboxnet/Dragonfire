using System.Collections.Generic;

namespace Dragonfire.Logging.Services
{
    public interface ILogFilterService
    {
        /// <summary>
        /// Filters and sanitises arbitrary data for logging. Returns a filtered
        /// object suitable for serialisation, or null when <paramref name="data"/> is null.
        /// </summary>
        object? FilterData(
            object? data,
            string[]? excludeProperties = null,
            string[]? includeProperties = null,
            int maxLength = 0,
            int maxDepth  = 0);

        /// <summary>Truncates and sanitises a raw string (request bodies, etc.).</summary>
        string? FilterString(string? data, int maxLength = 0);

        /// <summary>Returns true when <paramref name="propertyName"/> passes include/exclude rules.</summary>
        bool ShouldLogProperty(string propertyName, string[]? excludeProperties, string[]? includeProperties);

        /// <summary>
        /// Extracts properties decorated with <c>[LogProperty]</c> from <paramref name="data"/>
        /// via reflection. Used by the runtime DispatchProxy interceptor.
        /// </summary>
        Dictionary<string, object?> ExtractNamedProperties(object? data);
    }
}
