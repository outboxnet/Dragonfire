using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Dragonfire.Features.Configuration;

/// <summary>
/// Reads feature definitions from <see cref="IConfiguration"/> under a configurable section
/// (default <c>Features</c>). Each feature is a child key whose value carries the rules.
///
/// <para>Schema:</para>
/// <code>
/// {
///   "Features": {
///     "NewCheckout": {
///       "DefaultEnabled": false,
///       "Description": "Replacement checkout flow",
///       "Version": 3,
///       "Tenants": [ "acme", "globex" ],
///       "Users":   [ "user-42" ],
///       "Percentage": 25,
///       "PercentageBucket": "TenantThenUser"
///     }
///   }
/// }
/// </code>
///
/// <para>Reload behaviour: the source re-reads <see cref="IConfiguration"/> on every refresh
/// tick. Pair with <c>reloadOnChange: true</c> so file/Key Vault edits propagate within
/// <c>FeatureRefreshOptions.RefreshInterval</c>.</para>
/// </summary>
public sealed class ConfigurationFeatureSource : IFeatureSource
{
    private readonly IConfiguration _configuration;
    private readonly string _sectionName;

    public ConfigurationFeatureSource(IConfiguration configuration, string sectionName = "Features")
    {
        _configuration = configuration;
        _sectionName   = sectionName;
    }

    public string Name => "configuration";

    public Task<IReadOnlyList<FeatureDefinition>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var section = _configuration.GetSection(_sectionName);
        var list = new List<FeatureDefinition>();

        foreach (var child in section.GetChildren())
        {
            var binding = child.Get<ConfigurationFeatureBinding>() ?? new ConfigurationFeatureBinding();
            list.Add(BuildDefinition(child.Key, binding));
        }

        return Task.FromResult<IReadOnlyList<FeatureDefinition>>(list);
    }

    private static FeatureDefinition BuildDefinition(string name, ConfigurationFeatureBinding binding)
    {
        var rules = new List<FeatureRule>();

        if (binding.Tenants is { Count: > 0 })
            rules.Add(new TenantAllowListRule(binding.Tenants));

        if (binding.Users is { Count: > 0 })
            rules.Add(new UserAllowListRule(binding.Users));

        if (binding.Percentage is { } pct)
        {
            var bucket = Enum.TryParse<PercentageBucket>(binding.PercentageBucket, ignoreCase: true, out var parsed)
                ? parsed
                : PercentageBucket.TenantThenUser;
            rules.Add(new PercentageRule(name, pct, bucket));
        }

        return new FeatureDefinition(
            name,
            defaultEnabled: binding.DefaultEnabled ?? false,
            rules: rules,
            description: binding.Description,
            version: binding.Version ?? 0);
    }

    /// <summary>Configuration-binder shape — public so users can document their schema.</summary>
    public sealed class ConfigurationFeatureBinding
    {
        public bool? DefaultEnabled { get; set; }
        public string? Description { get; set; }
        public long? Version { get; set; }
        public List<string>? Tenants { get; set; }
        public List<string>? Users { get; set; }
        public int? Percentage { get; set; }
        public string? PercentageBucket { get; set; }
    }
}
