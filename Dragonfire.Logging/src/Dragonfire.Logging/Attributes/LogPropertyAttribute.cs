using System;

namespace Dragonfire.Logging.Attributes
{
    /// <summary>
    /// Promotes a method parameter or object property value to a first-class named
    /// structured-log property. The value appears as an individual scope entry rather
    /// than being buried inside the serialised payload JSON.
    ///
    /// In Application Insights the property surfaces as:
    /// <c>customDimensions["TenantId"]</c> — directly queryable in KQL without
    /// digging inside a JSON string.
    ///
    /// <example>
    /// <code>
    /// // On a method parameter:
    /// Task ProcessAsync([LogProperty("TenantId")] string tenantId, OrderDto order)
    ///
    /// // On a DTO property:
    /// public class OrderDto
    /// {
    ///     [LogProperty]               // key = "CustomerId"
    ///     public string CustomerId { get; set; }
    ///
    ///     [LogProperty("OrderRef")]   // key = "OrderRef"
    ///     public string ExternalRef  { get; set; }
    /// }
    /// </code>
    /// </example>
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Property | AttributeTargets.Parameter,
        Inherited    = true,
        AllowMultiple = false)]
    public sealed class LogPropertyAttribute : Attribute
    {
        /// <summary>
        /// The key used in the structured log scope.
        /// When <c>null</c> the decorated member's own name is used.
        /// </summary>
        public string? Name { get; }

        /// <param name="name">
        /// Optional override for the structured-log key.
        /// Defaults to the decorated member name when omitted.
        /// </param>
        public LogPropertyAttribute(string? name = null)
        {
            Name = name;
        }
    }
}
