namespace Dragonfire.Logging.Abstractions
{
    /// <summary>
    /// Marker interface. Services that implement <see cref="ILoggable"/> are automatically
    /// decorated with a Castle DynamicProxy interceptor when
    /// <see cref="Configuration.DragonfireLoggingOptions.EnableServiceInterception"/> is <c>true</c>.
    /// The proxy transparently logs every public method call — arguments, return value,
    /// elapsed time, and any exceptions — without any changes to the service code.
    ///
    /// Usage:
    /// <code>
    /// public class OrderService : IOrderService, ILoggable { ... }
    /// </code>
    /// Then register and decorate:
    /// <code>
    /// builder.Services.AddScoped&lt;IOrderService, OrderService&gt;();
    /// builder.Services.AddDragonfireLogging(opt => opt.EnableServiceInterception = true);
    /// </code>
    /// Call <c>AddDragonfireLogging</c> after all service registrations so the decorator
    /// scan can find them.
    /// </summary>
    public interface ILoggable { }
}
