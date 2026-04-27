using System;

namespace Dragonfire.Logging.Attributes
{
    /// <summary>
    /// Suppresses logging for the decorated target:
    /// <list type="bullet">
    ///   <item>On a <b>property</b> — excludes the property from serialised log payloads.</item>
    ///   <item>On a <b>method</b> — the proxy passes the call straight through without any logging.</item>
    ///   <item>On a <b>class</b> — disables logging for all methods in the class.</item>
    /// </list>
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Class,
        Inherited    = true,
        AllowMultiple = false)]
    public sealed class LogIgnoreAttribute : Attribute
    {
        /// <summary>Human-readable reason recorded in source code (not at runtime).</summary>
        public string Reason { get; }

        public LogIgnoreAttribute(string reason = "Sensitive data")
        {
            Reason = reason;
        }
    }
}
