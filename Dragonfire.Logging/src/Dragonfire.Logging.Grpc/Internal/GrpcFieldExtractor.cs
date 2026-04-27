using System.Collections.Generic;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Dragonfire.Logging.Grpc.Internal
{
    /// <summary>
    /// Extracts scalar fields from proto-generated messages via <see cref="IMessage"/> descriptor
    /// reflection and writes them into a <see cref="DragonfireGrpcScopeState"/> with a given prefix.
    ///
    /// <para>
    /// Proto-generated types implement <see cref="IMessage"/> and expose a
    /// <see cref="MessageDescriptor"/> that describes every field. This extractor reads all
    /// scalar fields (numerics, bool, string, enum) and skips nested messages, repeated fields,
    /// map fields, and byte arrays.
    /// </para>
    ///
    /// <para>
    /// Field names are taken from <see cref="FieldDescriptor.JsonName"/> — the lowerCamelCase
    /// JSON mapping defined in the proto spec (e.g. proto field <c>tenant_id</c> → key
    /// <c>Request.tenantId</c>).
    /// </para>
    ///
    /// <para>
    /// If the message does not implement <see cref="IMessage"/> (non-proto type) the method
    /// returns immediately without writing anything.
    /// </para>
    /// </summary>
    internal static class GrpcFieldExtractor
    {
        /// <summary>
        /// Reads all eligible scalar fields from <paramref name="message"/> and writes them
        /// to <paramref name="scope"/> as <c>{prefix}.{jsonFieldName}</c> keys.
        /// </summary>
        /// <param name="message">Proto-generated request or response object.</param>
        /// <param name="prefix">Dimension prefix — typically <c>"Request"</c> or <c>"Response"</c>.</param>
        /// <param name="excludeFields">Field JSON names to suppress (case-insensitive).</param>
        /// <param name="includeFields">
        /// Whitelist of field JSON names. When non-empty, only listed fields are written.
        /// When empty, all non-excluded scalar fields are written.
        /// </param>
        /// <param name="scope">Target scope bag.</param>
        internal static void Extract(
            object? message,
            string prefix,
            ISet<string> excludeFields,
            ISet<string> includeFields,
            DragonfireGrpcScopeState scope)
        {
            if (message is not IMessage protoMsg)
                return;

            foreach (var field in protoMsg.Descriptor.Fields.InFieldNumberOrder())
            {
                // ── Skip non-scalar types ────────────────────────────────────────
                // Repeated fields and maps produce collections — not suitable as a
                // single customDimension value.
                // Nested message fields would require recursive extraction which is
                // too aggressive for a logging interceptor.
                // Bytes fields are binary and have no safe string representation.
                // Group is a deprecated proto2 construct.
                if (field.IsRepeated
                    || field.IsMap
                    || field.FieldType == FieldType.Message
                    || field.FieldType == FieldType.Bytes
                    || field.FieldType == FieldType.Group)
                    continue;

                // ── Apply include/exclude filter ─────────────────────────────────
                var jsonName = field.JsonName;  // lowerCamelCase per proto JSON spec

                if (excludeFields.Contains(jsonName))
                    continue;

                if (includeFields.Count > 0 && !includeFields.Contains(jsonName))
                    continue;

                // ── Read field value via accessor ────────────────────────────────
                // For enum fields, GetValue returns the enum value object; ToString() gives
                // the enum member name which is more readable than the numeric ordinal.
                var value = field.Accessor.GetValue(protoMsg);
                scope[$"{prefix}.{jsonName}"] = value;
            }
        }
    }
}
