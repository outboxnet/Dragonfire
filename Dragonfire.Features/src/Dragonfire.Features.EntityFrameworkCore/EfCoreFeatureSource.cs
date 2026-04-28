using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Features.EntityFrameworkCore;

/// <summary>
/// Loads feature definitions from any <see cref="DbContext"/> that implements
/// <see cref="IFeaturesDbContext"/>. The DbContext is resolved per refresh tick from a
/// fresh DI scope to avoid leaking captured connections.
/// </summary>
public sealed class EfCoreFeatureSource : IFeatureSource
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EfCoreFeatureSource> _logger;

    public EfCoreFeatureSource(IServiceScopeFactory scopeFactory, ILogger<EfCoreFeatureSource> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public string Name => "efcore";

    public async Task<IReadOnlyList<FeatureDefinition>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IFeaturesDbContext>();

        var entities = await dbContext.Features
            .AsNoTracking()
            .Include(f => f.Rules)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var list = new List<FeatureDefinition>(entities.Count);
        foreach (var entity in entities)
            list.Add(BuildDefinition(entity));

        return list;
    }

    private FeatureDefinition BuildDefinition(FeatureEntity entity)
    {
        var rules = new List<FeatureRule>();
        foreach (var ruleEntity in entity.Rules.OrderBy(r => r.Order))
        {
            try
            {
                var rule = BuildRule(entity.Name, ruleEntity);
                if (rule is not null) rules.Add(rule);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Skipping malformed rule {RuleId} on feature '{Feature}'.", ruleEntity.Id, entity.Name);
            }
        }

        return new FeatureDefinition(
            entity.Name,
            defaultEnabled: entity.DefaultEnabled,
            rules: rules,
            description: entity.Description,
            version: entity.Version);
    }

    private static FeatureRule? BuildRule(string featureName, FeatureRuleEntity ruleEntity)
    {
        switch (ruleEntity.RuleType.ToLowerInvariant())
        {
            case "tenant":
                return new TenantAllowListRule(SplitList(ruleEntity.Payload));
            case "user":
                return new UserAllowListRule(SplitList(ruleEntity.Payload));
            case "percentage":
                var payload = JsonSerializer.Deserialize<PercentagePayload>(ruleEntity.Payload)
                              ?? new PercentagePayload();
                var bucket = Enum.TryParse<PercentageBucket>(payload.Bucket, ignoreCase: true, out var b)
                    ? b
                    : PercentageBucket.TenantThenUser;
                return new PercentageRule(featureName, payload.Value, bucket);
            default:
                return null;
        }
    }

    private static IEnumerable<string> SplitList(string payload)
    {
        // Tolerant of either a JSON array or a comma-separated list.
        var trimmed = payload.Trim();
        if (trimmed.StartsWith('['))
            return JsonSerializer.Deserialize<List<string>>(trimmed) ?? new List<string>();
        return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private sealed class PercentagePayload
    {
        public int Value { get; set; }
        public string Bucket { get; set; } = nameof(PercentageBucket.TenantThenUser);
    }
}
