using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenantContext.Serialization;

/// <summary>
/// Default <see cref="ITenantContextSerializer"/> using <c>System.Text.Json</c>. Format is
/// stable and versioned via <see cref="ContentType"/> so future schema changes can be detected.
/// </summary>
public sealed class JsonTenantContextSerializer : ITenantContextSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ContentType => "application/x-tenant+json;v=1";

    public string Serialize(TenantInfo tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (!tenant.IsResolved) return string.Empty;
        var dto = new Dto
        {
            Id = tenant.TenantId.Value,
            Source = tenant.Source,
            Properties = tenant.Properties.Count == 0 ? null : new Dictionary<string, string>(tenant.Properties),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public TenantInfo Deserialize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return TenantInfo.None;
        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(payload, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Malformed tenant context payload.", ex);
        }
        if (dto is null || string.IsNullOrWhiteSpace(dto.Id)) return TenantInfo.None;
        return new TenantInfo(new TenantId(dto.Id), dto.Source, dto.Properties);
    }

    private sealed class Dto
    {
        public string Id { get; set; } = string.Empty;
        public string? Source { get; set; }
        public Dictionary<string, string>? Properties { get; set; }
    }
}
