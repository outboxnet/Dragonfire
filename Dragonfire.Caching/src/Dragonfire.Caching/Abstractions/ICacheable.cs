namespace Dragonfire.Caching.Abstractions
{
    /// <summary>
    /// Marker interface. Classes that implement <see cref="ICacheable"/> are
    /// discovered at compile time by <c>Dragonfire.Caching.Generator</c>, which
    /// emits a sealed wrapper for every public method on every non-framework
    /// interface they implement. The wrapper applies <c>[Cache]</c> and
    /// <c>[CacheInvalidate]</c> semantics by calling into the runtime
    /// <see cref="Interfaces.ICacheService"/> and
    /// <see cref="Strategies.ICacheKeyStrategy"/> singletons.
    ///
    /// Usage:
    /// <code>
    /// public interface IDataService
    /// {
    ///     [Cache(SlidingExpirationSeconds = 300, KeyTemplate = "data:{id}")]
    ///     Task&lt;Data&gt; GetAsync(string id);
    /// }
    ///
    /// public class DataService : IDataService, ICacheable
    /// {
    ///     public Task&lt;Data&gt; GetAsync(string id) =&gt; ...;
    /// }
    /// </code>
    /// Then register and decorate:
    /// <code>
    /// builder.Services.AddScoped&lt;IDataService, DataService&gt;();
    /// builder.Services.AddDragonfireCaching();
    /// builder.Services.AddDragonfireGeneratedCaching();   // generated method
    /// </code>
    /// Call <c>AddDragonfireGeneratedCaching</c> after all service registrations
    /// so the decorator scan can find them.
    /// </summary>
    public interface ICacheable { }
}
