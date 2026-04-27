using System;
using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.Logging.Extensions
{
    /// <summary>
    /// Minimal, zero-dependency implementation of the "Decorate" pattern
    /// that Scrutor provides.  Internal — consumed only by
    /// <see cref="DragonfireLoggingExtensions.DecorateLoggableServices"/>.
    ///
    /// Strategy: locate the last <see cref="ServiceDescriptor"/> registered for
    /// <paramref name="serviceType"/>, replace it in-place with a new descriptor
    /// whose factory resolves the original implementation and wraps it with the
    /// decorator.  The original lifetime is preserved.
    /// </summary>
    internal static class ServiceDecorationExtensions
    {
        internal static IServiceCollection Decorate(
            this IServiceCollection services,
            Type serviceType,
            Func<object, IServiceProvider, object> decorator)
        {
            int index = LastIndexOf(services, serviceType);
            if (index < 0)
                throw new InvalidOperationException(
                    $"Service type '{serviceType.FullName}' is not registered. " +
                    "Register all services before calling DecorateLoggableServices() " +
                    "or enabling EnableServiceInterception.");

            var original = services[index];
            services[index] = Wrap(original, decorator);
            return services;
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static ServiceDescriptor Wrap(
            ServiceDescriptor d,
            Func<object, IServiceProvider, object> decorator)
        {
            // Pre-built singleton instance (e.g. services.AddSingleton(instance))
            if (d.ImplementationInstance is not null)
            {
                var inst = d.ImplementationInstance;
                return ServiceDescriptor.Describe(
                    d.ServiceType,
                    sp => decorator(inst, sp),
                    d.Lifetime);
            }

            // Factory registration (e.g. services.AddScoped(sp => new Foo()))
            if (d.ImplementationFactory is not null)
            {
                var factory = d.ImplementationFactory;
                return ServiceDescriptor.Describe(
                    d.ServiceType,
                    sp => decorator(factory(sp), sp),
                    d.Lifetime);
            }

            // Type registration (e.g. services.AddScoped<IFoo, Foo>())
            if (d.ImplementationType is not null)
            {
                var implType = d.ImplementationType;
                return ServiceDescriptor.Describe(
                    d.ServiceType,
                    sp => decorator(ActivatorUtilities.CreateInstance(sp, implType), sp),
                    d.Lifetime);
            }

            throw new InvalidOperationException(
                $"Cannot decorate '{d.ServiceType.FullName}': unrecognised descriptor kind.");
        }

        private static int LastIndexOf(IServiceCollection services, Type serviceType)
        {
            for (int i = services.Count - 1; i >= 0; i--)
                if (services[i].ServiceType == serviceType) return i;
            return -1;
        }
    }
}
