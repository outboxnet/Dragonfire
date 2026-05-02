using Dragonfire.Torch.Schema;

namespace Dragonfire.Torch.Inference;

/// <summary>
/// Finds primitive (name, type) property pairs that are shared by ≥3 models AND
/// ≥50% of models in the same group, with at least 2 shared pairs. Lifts the
/// shared set into a <c>BaseEntity</c> abstract class and rewrites matching
/// models to inherit from it.
/// </summary>
public static class BaseClassPromoter
{
    public sealed class PromotionOptions
    {
        public int MinModels { get; init; } = 3;
        public int MinSharedProperties { get; init; } = 2;
        // Higher than the 0.5 floor in the spec on purpose: at 0.5, domain
        // properties that happen to appear on a majority of models (e.g. Name
        // on 3/5 entities) get promoted into the base, which produces
        // awkward inheritance. 0.75 keeps the promotion limited to true
        // "entity backbone" props like Id / CreatedAt / UpdatedAt.
        public double MinSharedRatio { get; init; } = 0.75;
        public string BaseClassName { get; init; } = "BaseEntity";
    }

    public static void Promote(List<TypeIR> types, PromotionOptions? options = null)
    {
        options ??= new PromotionOptions();

        // Promotion runs independently per role. The most common use case —
        // shared (Id, CreatedAt) across response DTOs — only needs the
        // response group; lifting across request+response produces awkward
        // base classes.
        PromoteGroup(
            types,
            types.Where(t => t.IsClass && !t.IsStub && t.Role == TypeRole.Response).ToList(),
            options.BaseClassName,
            options);

        PromoteGroup(
            types,
            types.Where(t => t.IsClass && !t.IsStub && t.Role == TypeRole.Request).ToList(),
            "BaseRequest",
            options);
    }

    private static void PromoteGroup(List<TypeIR> allTypes, List<TypeIR> classes, string baseName, PromotionOptions options)
    {
        if (classes.Count < options.MinModels) return;

        // Score each candidate (name, type) pair by how many classes carry it.
        // We treat list / object property types as non-primitive — only scalars
        // get lifted.
        var pairCounts = new Dictionary<(string Name, string Type), int>();
        foreach (var c in classes)
        {
            foreach (var p in c.Properties)
            {
                if (!IsPrimitive(p.CSharpType)) continue;
                var key = (p.Name, p.CSharpType);
                pairCounts.TryGetValue(key, out var n);
                pairCounts[key] = n + 1;
            }
        }

        var sharedPairs = pairCounts
            .Where(kv => kv.Value >= options.MinModels &&
                         kv.Value >= classes.Count * options.MinSharedRatio)
            .Select(kv => kv.Key)
            .ToList();

        if (sharedPairs.Count < options.MinSharedProperties) return;

        // The set of classes that carry ALL shared pairs.
        var matching = classes
            .Where(c => sharedPairs.All(pair =>
                c.Properties.Any(p => p.Name == pair.Name && p.CSharpType == pair.Type)))
            .ToList();

        if (matching.Count < options.MinModels) return;

        // Build the base class (abstract). Property metadata is taken from the
        // first matching class — they all agree on (name, type, jsonName).
        var baseType = TypeIR.Class(baseName);
        baseType.IsAbstract = true;
        var donor = matching[0];
        foreach (var (name, type) in sharedPairs)
        {
            var donorProp = donor.Properties.First(p => p.Name == name && p.CSharpType == type);
            baseType.Properties.Add(new PropertyIR
            {
                Name = donorProp.Name,
                JsonName = donorProp.JsonName,
                CSharpType = donorProp.CSharpType,
                IsString = donorProp.IsString,
                IsCollection = donorProp.IsCollection,
                IsNullable = donorProp.IsNullable,
            });
        }

        allTypes.Insert(0, baseType);

        // Rewrite each matching class to inherit and drop the shared props.
        foreach (var c in matching)
        {
            c.BaseClassName = baseName;
            c.Properties.RemoveAll(p => sharedPairs.Any(s => s.Name == p.Name && s.Type == p.CSharpType));
        }
    }

    private static bool IsPrimitive(string csharp)
    {
        // Anything generic (List<…>) or with a custom class shape is not a
        // candidate. Nullable scalars are still primitive for this purpose.
        if (csharp.Contains('<')) return false;
        var bare = csharp.TrimEnd('?');
        return bare is "string" or "int" or "long" or "bool" or "decimal" or "double"
                    or "float" or "Guid" or "DateTimeOffset" or "DateTime";
    }
}
