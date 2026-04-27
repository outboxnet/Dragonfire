using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Dragonfire.Logging.Attributes;
using Dragonfire.Logging.Models;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Logging.Services
{
    public sealed class LogFilterService : ILogFilterService
    {
        private readonly SensitiveDataPolicy _policy;
        private readonly ILogger<LogFilterService>? _logger;
        private readonly int _defaultMaxExtractionDepth;

        public LogFilterService(
            SensitiveDataPolicy policy,
            ILogger<LogFilterService>? logger = null,
            int defaultMaxExtractionDepth = 5)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _logger = logger;
            _defaultMaxExtractionDepth = defaultMaxExtractionDepth;
        }

        public object? FilterData(
            object? data,
            string[]? excludeProperties = null,
            string[]? includeProperties = null,
            int maxLength = 0,
            int maxDepth  = 0)
        {
            if (data is null) return null;

            try
            {
                var result = FilterObject(data, excludeProperties ?? Array.Empty<string>(),
                    includeProperties ?? Array.Empty<string>(), maxDepth, 0,
                    new HashSet<object>(new ReferenceEqualityComparer()));

                if (maxLength > 0)
                    return TruncateDictionary(result, maxLength);

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to filter data");
                return new Dictionary<string, object?> { ["__error"] = "[FILTERING_FAILED]" };
            }
        }

        public string? FilterString(string? data, int maxLength = 0)
        {
            if (string.IsNullOrEmpty(data)) return data;

            var result = ApplyPatternRedaction(data);

            if (maxLength > 0 && result.Length > maxLength)
                result = result[..maxLength] + "...[TRUNCATED]";

            return result;
        }

        public bool ShouldLogProperty(
            string propertyName,
            string[]? excludeProperties,
            string[]? includeProperties)
        {
            if (string.IsNullOrEmpty(propertyName)) return false;

            if (includeProperties is { Length: > 0 })
                return includeProperties.Contains(propertyName, StringComparer.OrdinalIgnoreCase);

            if (excludeProperties is { Length: > 0 })
                return !excludeProperties.Contains(propertyName, StringComparer.OrdinalIgnoreCase);

            return true;
        }

        public Dictionary<string, object?> ExtractNamedProperties(object? data)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (data is null) return result;

            ExtractFromObject(data, result, maxDepth: _defaultMaxExtractionDepth);
            return result;
        }

        // ── Private filtering methods ─────────────────────────────────────────

        private Dictionary<string, object?>? FilterObject(
            object? obj,
            string[] exclude,
            string[] include,
            int maxDepth,
            int currentDepth,
            HashSet<object> visitedReferences)
        {
            if (obj is null) return null;

            // Handle primitive types
            if (IsSimpleType(obj.GetType()))
                return new Dictionary<string, object?> { ["__value"] = obj };

            // Check for circular reference
            if (visitedReferences.Contains(obj))
                return new Dictionary<string, object?> { ["__circular"] = "[CIRCULAR_REFERENCE]" };

            visitedReferences.Add(obj);

            // Depth limit reached
            if (maxDepth > 0 && currentDepth > maxDepth)
            {
                return obj is IEnumerable items && obj is not string
                    ? new Dictionary<string, object?> { ["__truncated"] = $"[{GetCollectionCount(items)} items]" }
                    : new Dictionary<string, object?> { ["__truncated"] = "[TRUNCATED]" };
            }

            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            // Handle collections
            if (obj is IEnumerable enumerable && obj is not string)
            {
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    if (list.Count >= 1000) // Prevent excessive logging
                    {
                        list.Add("[TOO_MANY_ITEMS]");
                        break;
                    }

                    var filtered = FilterObject(item, exclude, include, maxDepth, currentDepth + 1, visitedReferences);
                   
                    if(filtered != null)
                    {
                        list.Add(filtered);
                    }
                }
                result["__items"] = list;
                return result;
            }

            // Handle dictionaries
            if (obj is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = entry.Key?.ToString() ?? "null";
                    if (ShouldLogProperty(key, exclude, include) && !IsSensitiveField(key))
                    {
                        result[key] = FilterObject(entry.Value, exclude, include, maxDepth, currentDepth + 1, visitedReferences);
                    }
                }
                return result;
            }

            // Handle regular objects
            var type = obj.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (!ShouldLogProperty(prop.Name, exclude, include) || IsSensitiveField(prop.Name))
                    continue;

                try
                {
                    var value = prop.GetValue(obj);
                    result[prop.Name] = FilterObject(value, exclude, include, maxDepth, currentDepth + 1, visitedReferences);
                }
                catch (Exception ex)
                {
                    result[prop.Name] = $"[UNREADABLE: {ex.GetType().Name}]";
                }
            }

            return result.Count > 0 ? result : null;
        }

        private Dictionary<string, object?> TruncateDictionary(Dictionary<string, object?>? dict, int maxLength)
        {
            if (dict is null) return new Dictionary<string, object?> { ["__null"] = "[NULL]" };

            var json = System.Text.Json.JsonSerializer.Serialize(dict);
            if (json.Length <= maxLength)
                return dict;

            return new Dictionary<string, object?>
            {
                ["__truncated"] = json[..maxLength] + "...[TRUNCATED]"
            };
        }

        private string ApplyPatternRedaction(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var result = text;
            var hasChanges = false;

            foreach (var (pattern, replacement) in _policy.RedactionPatterns)
            {
                if (pattern.IsMatch(result))
                {
                    result = pattern.Replace(result, replacement);
                    hasChanges = true;
                }
            }

            if (_policy.RedactEmails && (!hasChanges || !result.Contains("[EMAIL_REDACTED]")))
            {
                result = Regex.Replace(result,
                    @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
                    "[EMAIL_REDACTED]",
                    RegexOptions.NonBacktracking);
            }

            if (_policy.RedactPhoneNumbers && (!hasChanges || !result.Contains("[PHONE_REDACTED]")))
            {
                result = Regex.Replace(result,
                    @"\b\d{3}[.\-]?\d{3}[.\-]?\d{4}\b",
                    "[PHONE_REDACTED]",
                    RegexOptions.NonBacktracking);
            }

            return result;
        }

        private bool IsSensitiveField(string fieldName)
            => _policy.SensitiveFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);

        private static int GetCollectionCount(IEnumerable enumerable)
        {
            if (enumerable is ICollection collection) return collection.Count;

            var count = 0;
            foreach (var _ in enumerable)
            {
                count++;
                if (count > 1000) break;
            }
            return count;
        }

        // ── [LogProperty] extraction ─────────────────────────────────────────

        private void ExtractFromObject(
            object data,
            Dictionary<string, object?> result,
            int depth = 0,
            int maxDepth = 5)
        {
            if (depth > maxDepth || data is null) return;

            var type = data.GetType();

            if (IsSimpleType(type)) return;

            // For collections, only process first level to prevent explosion
            if (data is IEnumerable enumerable && data is not string)
            {
                if (depth == 0)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is not null)
                            ExtractFromObject(item, result, depth + 1, maxDepth);
                    }
                }
                return;
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var attr = prop.GetCustomAttribute<LogPropertyAttribute>();
                    if (attr is null) continue;

                    var key = attr.Name ?? prop.Name;

                    if (!result.ContainsKey(key))
                    {
                        var value = prop.GetValue(data);
                        result.Add(key, value);
                    }
                }
                catch
                {
                    result.TryAdd(prop.Name, "[UNREADABLE]");
                }
            }
        }

        private static bool IsSimpleType(Type type)
        {
            if (type.IsPrimitive || type.IsEnum) return true;

            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null) return IsSimpleType(underlying);

            return type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(Guid)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Uri)
                || type == typeof(Version);
        }

        // ── Helper class ───────────────────────────────────────────────────

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}