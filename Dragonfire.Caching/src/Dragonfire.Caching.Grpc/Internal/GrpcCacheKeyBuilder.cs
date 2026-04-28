using System;
using System.Collections.Generic;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Dragonfire.Caching.Grpc.Internal
{
    /// <summary>
    /// Reads scalar fields from a proto-generated request via <see cref="IMessage"/> descriptor
    /// reflection and produces an <see cref="IReadOnlyDictionary{TKey,TValue}"/> compatible with
    /// <see cref="Caching.Strategies.ICacheKeyStrategy"/>.
    ///
    /// <para>Repeated fields, maps, nested messages, and bytes are skipped — they are not
    /// suitable as deterministic key components. Field names use proto JSON names
    /// (lowerCamelCase, e.g. <c>tenant_id</c> → <c>tenantId</c>).</para>
    ///
    /// <para>Non-proto messages (anything not implementing <see cref="IMessage"/>) yield an
    /// empty argument set; the strategy will then either resolve the template against an empty
    /// map or auto-generate <c>Service.Method()</c>. This keeps the interceptor safe for any
    /// transport-level type.</para>
    /// </summary>
    internal static class GrpcCacheKeyBuilder
    {
        private static readonly IReadOnlyDictionary<string, object?> Empty =
            new Dictionary<string, object?>(0, StringComparer.Ordinal);

        /// <summary>
        /// Extract scalar fields from <paramref name="message"/>.
        /// </summary>
        /// <param name="message">Proto-generated request object.</param>
        /// <param name="includeFields">
        /// Whitelist of proto JSON names. When empty, all eligible scalar fields are returned.
        /// </param>
        public static IReadOnlyDictionary<string, object?> Extract(
            object? message,
            string[] includeFields)
        {
            if (message is not IMessage protoMsg)
                return Empty;

            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var field in protoMsg.Descriptor.Fields.InFieldNumberOrder())
            {
                // Skip non-scalar types — same rules as GrpcFieldExtractor in Logging.Grpc.
                if (field.IsRepeated
                    || field.IsMap
                    || field.FieldType == FieldType.Message
                    || field.FieldType == FieldType.Bytes
                    || field.FieldType == FieldType.Group)
                    continue;

                var jsonName = field.JsonName;

                if (includeFields.Length > 0 && Array.IndexOf(includeFields, jsonName) < 0)
                    continue;

                dict[jsonName] = field.Accessor.GetValue(protoMsg);
            }

            return dict;
        }

        /// <summary>
        /// Split a gRPC full-method path (<c>/package.Service/Method</c>) into
        /// <c>(serviceName, methodName)</c> using only the trailing service name.
        /// Used by the strategy to build readable auto-keys like <c>OrderService.GetOrder(...)</c>.
        /// </summary>
        public static (string Service, string Method) ParseFullMethod(string fullMethod)
        {
            if (string.IsNullOrEmpty(fullMethod)) return (string.Empty, string.Empty);

            var trimmed = fullMethod.TrimStart('/');
            var slash   = trimmed.LastIndexOf('/');
            if (slash < 0) return (trimmed, trimmed);

            var serviceFullName = trimmed.Substring(0, slash);
            var methodName      = trimmed.Substring(slash + 1);

            var dot = serviceFullName.LastIndexOf('.');
            var serviceName = dot >= 0
                ? serviceFullName.Substring(dot + 1)
                : serviceFullName;

            return (serviceName, methodName);
        }
    }
}
