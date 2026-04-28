#nullable enable
// This file IS the source generator, not generated output.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Dragonfire.Caching.Generator
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Well-known type/attribute names (used for semantic-model lookup)
    // ─────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Symbol display format that preserves nullable reference type annotations
    /// (e.g. <c>string?</c>) on top of the standard fully-qualified format.
    /// Required so the emitted proxy method signature matches the interface
    /// member exactly under <c>#nullable enable</c>.
    /// </summary>
    internal static class DisplayFormats
    {
        internal static readonly SymbolDisplayFormat FullyQualifiedWithNrt =
            SymbolDisplayFormat.FullyQualifiedFormat
                .WithMiscellaneousOptions(
                    SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                        | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
    }

    internal static class WellKnown
    {
        internal const string ICacheable               = "Dragonfire.Caching.Abstractions.ICacheable";
        internal const string CacheAttribute           = "Dragonfire.Caching.Attributes.CacheAttribute";
        internal const string CacheInvalidateAttribute = "Dragonfire.Caching.Attributes.CacheInvalidateAttribute";
        internal const string CacheKeyAttribute        = "Dragonfire.Caching.Attributes.CacheKeyAttribute";

        internal const string ICacheService            = "global::Dragonfire.Caching.Interfaces.ICacheService";
        internal const string ICacheKeyStrategy        = "global::Dragonfire.Caching.Strategies.ICacheKeyStrategy";
        internal const string CacheEntryOptions        = "global::Dragonfire.Caching.Models.CacheEntryOptions";

        internal const string IServiceCollection       = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
        internal const string ServiceDescriptor        = "global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor";
        internal const string ActivatorUtilities       = "global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities";
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Data models — immutable, value-equality so incremental caching works
    // ─────────────────────────────────────────────────────────────────────────────

    internal enum MethodReturnKind { Void, SyncReturn, Task, TaskOfT }

    /// <summary>Compile-time snapshot of a single [CacheInvalidate(...)] application.</summary>
    internal sealed class InvalidateAttributeModel : IEquatable<InvalidateAttributeModel>
    {
        public string? KeyPattern { get; }
        public string? Tag { get; }
        public bool InvalidateBefore { get; }

        public InvalidateAttributeModel(string? keyPattern, string? tag, bool before)
        {
            KeyPattern = keyPattern;
            Tag = tag;
            InvalidateBefore = before;
        }

        public bool Equals(InvalidateAttributeModel? other)
            => other != null && KeyPattern == other.KeyPattern && Tag == other.Tag && InvalidateBefore == other.InvalidateBefore;
        public override bool Equals(object? obj) => Equals(obj as InvalidateAttributeModel);
        public override int GetHashCode()
            => (KeyPattern?.GetHashCode() ?? 0) ^ (Tag?.GetHashCode() ?? 0) ^ InvalidateBefore.GetHashCode();
    }

    /// <summary>Compile-time snapshot of [Cache(...)].</summary>
    internal sealed class CacheAttributeModel : IEquatable<CacheAttributeModel>
    {
        public string? KeyTemplate { get; }
        public int AbsoluteExpirationSeconds { get; }
        public int SlidingExpirationSeconds { get; }
        public ImmutableArray<string> Tags { get; }
        public bool CacheNullValues { get; }

        public CacheAttributeModel(string? keyTemplate, int abs, int sliding,
            ImmutableArray<string> tags, bool cacheNulls)
        {
            KeyTemplate = keyTemplate;
            AbsoluteExpirationSeconds = abs;
            SlidingExpirationSeconds = sliding;
            Tags = tags;
            CacheNullValues = cacheNulls;
        }

        public bool Equals(CacheAttributeModel? other)
            => other != null
               && KeyTemplate == other.KeyTemplate
               && AbsoluteExpirationSeconds == other.AbsoluteExpirationSeconds
               && SlidingExpirationSeconds == other.SlidingExpirationSeconds
               && Tags.SequenceEqual(other.Tags)
               && CacheNullValues == other.CacheNullValues;
        public override bool Equals(object? obj) => Equals(obj as CacheAttributeModel);
        public override int GetHashCode() => (KeyTemplate?.GetHashCode() ?? 0) ^ AbsoluteExpirationSeconds ^ SlidingExpirationSeconds;
    }

    internal sealed class ParameterModel : IEquatable<ParameterModel>
    {
        public string  Name           { get; }
        public string  TypeFQN        { get; }
        public RefKind RefKind        { get; }
        public bool    IsParams       { get; }
        public bool    HasCacheKey    { get; }
        public string? CacheKeyAlias  { get; }   // null → use Name

        public ParameterModel(string name, string typeFQN, RefKind refKind, bool isParams,
            bool hasCacheKey, string? cacheKeyAlias)
        {
            Name = name;
            TypeFQN = typeFQN;
            RefKind = refKind;
            IsParams = isParams;
            HasCacheKey = hasCacheKey;
            CacheKeyAlias = cacheKeyAlias;
        }

        public string KeyAlias => CacheKeyAlias ?? Name;

        public bool Equals(ParameterModel? other)
            => other != null && Name == other.Name && TypeFQN == other.TypeFQN;
        public override bool Equals(object? obj) => Equals(obj as ParameterModel);
        public override int GetHashCode() => (Name?.GetHashCode() ?? 0) ^ (TypeFQN?.GetHashCode() ?? 0);
    }

    internal sealed class MethodModel : IEquatable<MethodModel>
    {
        public string                                 Name              { get; }
        public MethodReturnKind                       Kind              { get; }
        public string                                 ReturnTypeFQN     { get; }
        public string?                                TaskResultTypeFQN { get; }
        public ImmutableArray<ParameterModel>         Parameters        { get; }
        public ImmutableArray<string>                 TypeParameters    { get; }
        public CacheAttributeModel?                   CacheAttr         { get; }
        public ImmutableArray<InvalidateAttributeModel> InvalidateAttrs { get; }

        public MethodModel(string name, MethodReturnKind kind, string returnFQN, string? taskResultFQN,
            ImmutableArray<ParameterModel> parameters, ImmutableArray<string> typeParameters,
            CacheAttributeModel? cacheAttr, ImmutableArray<InvalidateAttributeModel> invalidateAttrs)
        {
            Name = name;
            Kind = kind;
            ReturnTypeFQN = returnFQN;
            TaskResultTypeFQN = taskResultFQN;
            Parameters = parameters;
            TypeParameters = typeParameters;
            CacheAttr = cacheAttr;
            InvalidateAttrs = invalidateAttrs;
        }

        public bool HasCacheKeyParams => Parameters.Any(p => p.HasCacheKey);

        /// <summary>Parameters that participate in cache-key generation.</summary>
        public IEnumerable<ParameterModel> KeyParams =>
            HasCacheKeyParams ? Parameters.Where(p => p.HasCacheKey) : Parameters;

        public bool Equals(MethodModel? other)
            => other != null && Name == other.Name && ReturnTypeFQN == other.ReturnTypeFQN
               && Parameters.Length == other.Parameters.Length;
        public override bool Equals(object? obj) => Equals(obj as MethodModel);
        public override int GetHashCode() => (Name?.GetHashCode() ?? 0) ^ (ReturnTypeFQN?.GetHashCode() ?? 0);
    }

    internal sealed class ServiceModel : IEquatable<ServiceModel>
    {
        public string                      ClassName      { get; }
        public string                      ClassNamespace { get; }
        public string                      ClassFQN       { get; }
        public ImmutableArray<string>      InterfacesFQN  { get; }
        public ImmutableArray<MethodModel> Methods        { get; }

        public ServiceModel(string className, string classNs, string classFQN,
            ImmutableArray<string> ifacesFQN, ImmutableArray<MethodModel> methods)
        {
            ClassName = className;
            ClassNamespace = classNs;
            ClassFQN = classFQN;
            InterfacesFQN = ifacesFQN;
            Methods = methods;
        }

        public string PrimaryIfaceFQN => InterfacesFQN[0];
        public string ProxyClassName  => $"{ClassName}CachingProxy";
        public string ProxyNamespace  => string.IsNullOrEmpty(ClassNamespace) ? "Dragonfire.Caching.Generated" : $"{ClassNamespace}.Generated";
        public string ProxyFQN        => $"global::{ProxyNamespace}.{ProxyClassName}";

        public bool Equals(ServiceModel? other)
            => other != null && ClassName == other.ClassName && ClassNamespace == other.ClassNamespace;
        public override bool Equals(object? obj) => Equals(obj as ServiceModel);
        public override int GetHashCode() => (ClassName?.GetHashCode() ?? 0) ^ (ClassNamespace?.GetHashCode() ?? 0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Incremental source generator entry point
    // ─────────────────────────────────────────────────────────────────────────────

    [Generator]
    public sealed class CachingProxyGenerator : IIncrementalGenerator
    {
        private static readonly DiagnosticDescriptor s_generatorError = new DiagnosticDescriptor(
            id:                 "DRC0001",
            title:              "Dragonfire.Caching.Generator error",
            messageFormat:      "An unexpected error occurred in Dragonfire.Caching.Generator: {0}",
            category:           "Dragonfire.Caching.Generator",
            defaultSeverity:    DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var services = context.SyntaxProvider
                .CreateSyntaxProvider(
                    static (n, _) => n is ClassDeclarationSyntax { BaseList: not null },
                    static (ctx, ct) => ModelExtractor.Extract(ctx, ct))
                .Where(static m => m is not null)
                .Select(static (m, _) => m!);

            context.RegisterSourceOutput(services.Collect(), (spc, models) =>
            {
                try
                {
                    if (models.IsEmpty) return;

                    foreach (var model in models)
                    {
                        try
                        {
                            spc.AddSource(
                                $"{model.ClassName}CachingProxy.g.cs",
                                SourceText.From(ProxyEmitter.Emit(model), Encoding.UTF8));
                        }
                        catch (Exception ex)
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(s_generatorError, Location.None,
                                $"Failed to emit proxy for {model.ClassName}: {ex}"));
                        }
                    }

                    try
                    {
                        spc.AddSource(
                            "DragonfireGeneratedCachingExtensions.g.cs",
                            SourceText.From(RegistrationEmitter.Emit(models), Encoding.UTF8));
                    }
                    catch (Exception ex)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(s_generatorError, Location.None,
                            $"Failed to emit DI registration extensions: {ex}"));
                    }
                }
                catch (Exception ex)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(s_generatorError, Location.None,
                        $"Unhandled exception in RegisterSourceOutput: {ex}"));
                }
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Model extraction
    // ─────────────────────────────────────────────────────────────────────────────

    internal static class ModelExtractor
    {
        public static ServiceModel? Extract(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            try
            {
                var classDecl   = (ClassDeclarationSyntax)ctx.Node;
                var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;

                if (classSymbol is null || classSymbol.IsAbstract || classSymbol.IsGenericType)
                    return null;

                if (!classSymbol.AllInterfaces.Any(IsICacheable)) return null;

                var serviceIfaces = classSymbol.AllInterfaces
                    .Where(i => !IsICacheable(i) && !IsFrameworkInterface(i))
                    .ToImmutableArray();

                if (serviceIfaces.IsEmpty) return null;

                var seen    = new HashSet<string>(StringComparer.Ordinal);
                var methods = new List<MethodModel>();

                foreach (var iface in serviceIfaces)
                {
                    foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                    {
                        if (member.MethodKind != Microsoft.CodeAnalysis.MethodKind.Ordinary) continue;

                        var key = $"{member.Name}({string.Join(",", member.Parameters.Select(p => p.Type.ToDisplayString()))})";
                        if (!seen.Add(key)) continue;

                        ct.ThrowIfCancellationRequested();

                        var classImpl = FindImplementation(classSymbol, member);
                        var sources = new List<ISymbol> { member };
                        if (classImpl is not null) sources.Add(classImpl);

                        var cacheAttr      = ReadCacheAttribute(sources);
                        var invalidateAttrs = ReadInvalidateAttributes(sources);

                        methods.Add(BuildMethod(member, ctx.SemanticModel.Compilation, cacheAttr, invalidateAttrs));
                    }
                }

                var ifacesFQN = serviceIfaces
                    .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .ToImmutableArray();

                var classFQN = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                return new ServiceModel(
                    classSymbol.Name, classSymbol.ContainingNamespace.ToDisplayString(),
                    classFQN, ifacesFQN, methods.ToImmutableArray());
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return null;
            }
        }

        // ── Symbol helpers ────────────────────────────────────────────────────

        private static bool IsICacheable(INamedTypeSymbol i)
            => $"{i.ContainingNamespace}.{i.Name}" == WellKnown.ICacheable;

        private static bool IsFrameworkInterface(INamedTypeSymbol i)
        {
            var ns = i.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return ns.StartsWith("System", StringComparison.Ordinal)
                || ns.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || ns.StartsWith("Dragonfire.Caching", StringComparison.Ordinal);
        }

        private static bool MatchAttrName(INamedTypeSymbol? cls, string fullName)
        {
            if (cls is null) return false;
            var fqn = $"{cls.ContainingNamespace}.{cls.Name}";
            return fqn == fullName || fqn + "Attribute" == fullName;
        }

        private static IMethodSymbol? FindImplementation(INamedTypeSymbol classSymbol, IMethodSymbol ifaceMethod)
        {
            // Try explicit/implicit interface mapping
            var impl = classSymbol.FindImplementationForInterfaceMember(ifaceMethod) as IMethodSymbol;
            return impl;
        }

        // ── [Cache] attribute reader ──────────────────────────────────────────

        private static CacheAttributeModel? ReadCacheAttribute(IEnumerable<ISymbol> sources)
        {
            foreach (var symbol in sources)
            {
                var attr = symbol.GetAttributes().FirstOrDefault(a =>
                    MatchAttrName(a.AttributeClass, WellKnown.CacheAttribute));
                if (attr is null) continue;

                string? keyTemplate = null;
                int absSec = 0;
                int slidingSec = 300;       // default per CacheAttribute
                bool cacheNulls = false;
                ImmutableArray<string> tags = ImmutableArray<string>.Empty;

                foreach (var arg in attr.NamedArguments)
                {
                    switch (arg.Key)
                    {
                        case "KeyTemplate":               keyTemplate = arg.Value.Value as string; break;
                        case "AbsoluteExpirationSeconds": if (arg.Value.Value is int a) absSec = a; break;
                        case "SlidingExpirationSeconds":  if (arg.Value.Value is int s) slidingSec = s; break;
                        case "CacheNullValues":           if (arg.Value.Value is bool b) cacheNulls = b; break;
                        case "Tags":
                            if (!arg.Value.Values.IsDefault)
                                tags = arg.Value.Values
                                    .Select(v => v.Value as string ?? string.Empty)
                                    .Where(v => !string.IsNullOrEmpty(v))
                                    .ToImmutableArray();
                            break;
                    }
                }

                return new CacheAttributeModel(keyTemplate, absSec, slidingSec, tags, cacheNulls);
            }
            return null;
        }

        // ── [CacheInvalidate] attribute reader (multiple allowed) ─────────────

        private static ImmutableArray<InvalidateAttributeModel> ReadInvalidateAttributes(IEnumerable<ISymbol> sources)
        {
            var list = new List<InvalidateAttributeModel>();
            foreach (var symbol in sources)
            {
                foreach (var attr in symbol.GetAttributes()
                    .Where(a => MatchAttrName(a.AttributeClass, WellKnown.CacheInvalidateAttribute)))
                {
                    string? keyPattern = null;
                    string? tag = null;
                    bool before = false;

                    // Constructor args
                    if (attr.ConstructorArguments.Length == 1
                        && attr.ConstructorArguments[0].Value is string singlePattern)
                    {
                        keyPattern = singlePattern;
                    }
                    else if (attr.ConstructorArguments.Length == 2
                        && attr.ConstructorArguments[0].Value is string entityType
                        && attr.ConstructorArguments[1].Value is string entityIdParam)
                    {
                        keyPattern = $"{entityType}:{{{entityIdParam}}}:*";
                        tag = entityType;
                    }

                    // Named args override constructor defaults
                    foreach (var arg in attr.NamedArguments)
                    {
                        switch (arg.Key)
                        {
                            case "KeyPattern":       keyPattern = arg.Value.Value as string ?? keyPattern; break;
                            case "Tag":              tag = arg.Value.Value as string ?? tag; break;
                            case "InvalidateBefore": if (arg.Value.Value is bool b) before = b; break;
                        }
                    }

                    list.Add(new InvalidateAttributeModel(keyPattern, tag, before));
                }
            }
            return list.ToImmutableArray();
        }

        // ── Method model builder ──────────────────────────────────────────────

        private static MethodModel BuildMethod(IMethodSymbol method, Compilation compilation,
            CacheAttributeModel? cacheAttr, ImmutableArray<InvalidateAttributeModel> invalidateAttrs)
        {
            var kind     = GetMethodKind(method, compilation);
            var retFQN   = method.ReturnType.ToDisplayString(DisplayFormats.FullyQualifiedWithNrt);
            string? taskResultFQN = null;

            if (kind == MethodReturnKind.TaskOfT
                && method.ReturnType is INamedTypeSymbol namedRet
                && !namedRet.TypeArguments.IsEmpty)
            {
                taskResultFQN = namedRet.TypeArguments[0].ToDisplayString(DisplayFormats.FullyQualifiedWithNrt);
            }

            var typeParams = method.TypeParameters.Select(tp => tp.Name).ToImmutableArray();
            var parameters = method.Parameters.Select(p => BuildParameter(p)).ToImmutableArray();

            return new MethodModel(method.Name, kind, retFQN, taskResultFQN,
                parameters, typeParams, cacheAttr, invalidateAttrs);
        }

        private static MethodReturnKind GetMethodKind(IMethodSymbol method, Compilation compilation)
        {
            if (method.ReturnsVoid) return MethodReturnKind.Void;

            var taskSymbol = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
            var taskOfT    = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");

            if (SymbolEqualityComparer.Default.Equals(method.ReturnType, taskSymbol))
                return MethodReturnKind.Task;

            if (method.ReturnType is INamedTypeSymbol nt && nt.IsGenericType
                && SymbolEqualityComparer.Default.Equals(nt.OriginalDefinition, taskOfT))
                return MethodReturnKind.TaskOfT;

            return MethodReturnKind.SyncReturn;
        }

        private static ParameterModel BuildParameter(IParameterSymbol param)
        {
            var ckAttr = param.GetAttributes().FirstOrDefault(a =>
                MatchAttrName(a.AttributeClass, WellKnown.CacheKeyAttribute));

            bool hasCk = ckAttr != null;
            string? alias = null;
            if (hasCk && ckAttr!.ConstructorArguments.Length > 0)
                alias = ckAttr.ConstructorArguments[0].Value as string;
            // Named arg override
            if (hasCk)
            {
                foreach (var arg in ckAttr!.NamedArguments)
                    if (arg.Key == "Name") alias = arg.Value.Value as string ?? alias;
            }

            return new ParameterModel(
                param.Name,
                param.Type.ToDisplayString(DisplayFormats.FullyQualifiedWithNrt),
                param.RefKind, param.IsParams,
                hasCk, alias);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Proxy emitter
    // ─────────────────────────────────────────────────────────────────────────────

    internal static class ProxyEmitter
    {
        public static string Emit(ServiceModel model)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated by Dragonfire.Caching.Generator");
            sb.AppendLine("// Replaces the runtime DispatchProxy with a compile-time wrapper.");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine($"namespace {model.ProxyNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Compile-time caching proxy for <see cref=\"{model.ClassName}\"/>.");
            sb.AppendLine($"    /// Calls into <see cref=\"{WellKnown.ICacheService}\"/> + <see cref=\"{WellKnown.ICacheKeyStrategy}\"/>.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"Dragonfire.Caching.Generator\", \"1.0.0\")]");
            sb.AppendLine($"    [global::System.Diagnostics.DebuggerNonUserCode]");

            var ifaceList = string.Join(", ", model.InterfacesFQN);
            sb.AppendLine($"    internal sealed class {model.ProxyClassName} : {ifaceList}");
            sb.AppendLine("    {");

            EmitFields(sb, model);
            EmitConstructor(sb, model);

            foreach (var method in model.Methods)
                EmitMethod(sb, model, method);

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── Fields & ctor ─────────────────────────────────────────────────────

        private static void EmitFields(StringBuilder sb, ServiceModel model)
        {
            foreach (var iface in model.InterfacesFQN)
                sb.AppendLine($"        private readonly {iface} {InnerField(iface)};");
            sb.AppendLine($"        private readonly {WellKnown.ICacheService} _cache;");
            sb.AppendLine($"        private readonly {WellKnown.ICacheKeyStrategy} _keyStrategy;");
            sb.AppendLine();
        }

        private static void EmitConstructor(StringBuilder sb, ServiceModel model)
        {
            sb.AppendLine($"        public {model.ProxyClassName}(");
            sb.AppendLine($"            {model.PrimaryIfaceFQN} inner,");
            sb.AppendLine($"            {WellKnown.ICacheService} cache,");
            sb.AppendLine($"            {WellKnown.ICacheKeyStrategy} keyStrategy)");
            sb.AppendLine("        {");
            foreach (var iface in model.InterfacesFQN)
                sb.AppendLine($"            {InnerField(iface)} = ({iface})inner;");
            sb.AppendLine("            _cache       = cache       ?? throw new global::System.ArgumentNullException(nameof(cache));");
            sb.AppendLine("            _keyStrategy = keyStrategy ?? throw new global::System.ArgumentNullException(nameof(keyStrategy));");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // ── Method dispatch ───────────────────────────────────────────────────

        private static void EmitMethod(StringBuilder sb, ServiceModel model, MethodModel method)
        {
            var inner      = InnerField(model.PrimaryIfaceFQN);
            var typeParams = method.TypeParameters.IsEmpty ? "" : $"<{string.Join(", ", method.TypeParameters)}>";
            var paramList  = string.Join(", ", method.Parameters.Select(FormatParam));
            var hasCache       = method.CacheAttr != null;
            var hasInvalidate  = !method.InvalidateAttrs.IsEmpty;
            // Mirror runtime proxy: [Cache] takes precedence; we don't generate invalidation in cache case.
            var pathRequiresAsync = (hasCache && (method.Kind == MethodReturnKind.SyncReturn || method.Kind == MethodReturnKind.TaskOfT))
                                    || (hasInvalidate && (method.Kind == MethodReturnKind.Task || method.Kind == MethodReturnKind.TaskOfT));
            var asyncKw = pathRequiresAsync ? "async " : "";

            sb.AppendLine($"        public {asyncKw}{method.ReturnTypeFQN} {method.Name}{typeParams}({paramList})");
            sb.AppendLine("        {");

            // Generic methods → plain pass-through (cannot generate generic Task<T> bridging without runtime reflection).
            if (!method.TypeParameters.IsEmpty)
            {
                EmitPassThrough(sb, method, inner);
                sb.AppendLine("        }");
                sb.AppendLine();
                return;
            }

            if (hasCache)
            {
                EmitCachedMethod(sb, model, method, inner);
            }
            else if (hasInvalidate)
            {
                EmitInvalidatingMethod(sb, model, method, inner);
            }
            else
            {
                EmitPassThrough(sb, method, inner);
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static void EmitPassThrough(StringBuilder sb, MethodModel method, string inner)
        {
            var retKw = method.Kind == MethodReturnKind.Void ? "" : "return ";
            sb.AppendLine($"            {retKw}{inner}.{method.Name}({CallArgs(method.Parameters)});");
        }

        // ── [Cache]: GetOrAdd ─────────────────────────────────────────────────

        private static void EmitCachedMethod(StringBuilder sb, ServiceModel m, MethodModel method, string inner)
        {
            var attr = method.CacheAttr!;

            // Cache-key argument map (only [CacheKey]-marked params if any are marked, else all).
            EmitArgsDictionary(sb, method.KeyParams, "__keyArgs", "            ");

            // [Cache(KeyTemplate=...)] → may be null
            var template = attr.KeyTemplate is null ? "null" : $"\"{Escape(attr.KeyTemplate)}\"";
            var serviceName = m.PrimaryIfaceFQN.Substring(m.PrimaryIfaceFQN.LastIndexOf('.') + 1);

            sb.AppendLine($"            var __key = _keyStrategy.GenerateKey(\"{serviceName}\", \"{method.Name}\", __keyArgs, {template});");

            // Tag templates need full args (templates can reference any param name); use full param set.
            var hasTagTemplates = !attr.Tags.IsEmpty;
            if (hasTagTemplates)
                EmitArgsDictionary(sb, method.Parameters, "__tagArgs", "            ");

            // Build a single options-configurator inline.
            sb.AppendLine("            global::System.Action<" + WellKnown.CacheEntryOptions + "> __configure = __opts =>");
            sb.AppendLine("            {");
            if (attr.AbsoluteExpirationSeconds > 0)
                sb.AppendLine($"                __opts.AbsoluteExpirationRelativeToNow = global::System.TimeSpan.FromSeconds({attr.AbsoluteExpirationSeconds});");
            else
                sb.AppendLine($"                __opts.SlidingExpiration = global::System.TimeSpan.FromSeconds({attr.SlidingExpirationSeconds});");

            foreach (var tagTemplate in attr.Tags)
                sb.AppendLine($"                __opts.Tags.Add(_keyStrategy.GeneratePattern(\"{Escape(tagTemplate)}\", __tagArgs));");

            sb.AppendLine("            };");

            switch (method.Kind)
            {
                case MethodReturnKind.TaskOfT:
                    sb.AppendLine($"            return await _cache.GetOrAddAsync<{method.TaskResultTypeFQN}>(");
                    sb.AppendLine("                __key,");
                    sb.AppendLine($"                () => {inner}.{method.Name}({CallArgs(method.Parameters)}),");
                    sb.AppendLine("                __configure).ConfigureAwait(false);");
                    break;

                case MethodReturnKind.SyncReturn:
                    // Sync return wrapped via Task.FromResult; sync-over-async at proxy boundary (matches existing runtime behavior).
                    sb.AppendLine($"            return _cache.GetOrAddAsync<{method.ReturnTypeFQN}>(");
                    sb.AppendLine("                    __key,");
                    sb.AppendLine($"                    () => global::System.Threading.Tasks.Task.FromResult({inner}.{method.Name}({CallArgs(method.Parameters)})),");
                    sb.AppendLine("                    __configure)");
                    sb.AppendLine("                .GetAwaiter().GetResult();");
                    break;

                case MethodReturnKind.Task:
                    // Non-generic Task: nothing to cache. Pass through.
                    sb.AppendLine($"            return {inner}.{method.Name}({CallArgs(method.Parameters)});");
                    break;

                case MethodReturnKind.Void:
                    // Cache attribute on void method makes no sense — pass through.
                    sb.AppendLine($"            {inner}.{method.Name}({CallArgs(method.Parameters)});");
                    break;
            }
        }

        // ── [CacheInvalidate]: pre/post invalidation ──────────────────────────

        private static void EmitInvalidatingMethod(StringBuilder sb, ServiceModel m, MethodModel method, string inner)
        {
            var beforeAttrs = method.InvalidateAttrs.Where(a => a.InvalidateBefore).ToArray();
            var afterAttrs  = method.InvalidateAttrs.Where(a => !a.InvalidateBefore).ToArray();

            // Args dictionary used by all pattern resolutions.
            EmitArgsDictionary(sb, method.Parameters, "__invArgs", "            ");

            foreach (var ia in beforeAttrs)
                EmitInvalidationCall(sb, ia, "            ", awaitIt: method.Kind != MethodReturnKind.Void && method.Kind != MethodReturnKind.SyncReturn);

            // Invoke target
            switch (method.Kind)
            {
                case MethodReturnKind.Void:
                    sb.AppendLine($"            {inner}.{method.Name}({CallArgs(method.Parameters)});");
                    break;
                case MethodReturnKind.SyncReturn:
                    sb.AppendLine($"            var __result = {inner}.{method.Name}({CallArgs(method.Parameters)});");
                    break;
                case MethodReturnKind.Task:
                    sb.AppendLine($"            await {inner}.{method.Name}({CallArgs(method.Parameters)}).ConfigureAwait(false);");
                    break;
                case MethodReturnKind.TaskOfT:
                    sb.AppendLine($"            var __result = await {inner}.{method.Name}({CallArgs(method.Parameters)}).ConfigureAwait(false);");
                    break;
            }

            foreach (var ia in afterAttrs)
                EmitInvalidationCall(sb, ia, "            ", awaitIt: method.Kind != MethodReturnKind.Void && method.Kind != MethodReturnKind.SyncReturn);

            if (method.Kind == MethodReturnKind.SyncReturn || method.Kind == MethodReturnKind.TaskOfT)
                sb.AppendLine("            return __result;");
        }

        private static void EmitInvalidationCall(StringBuilder sb, InvalidateAttributeModel ia, string indent, bool awaitIt)
        {
            var awaitKw = awaitIt ? "await " : "";
            var tail    = awaitIt ? ".ConfigureAwait(false)" : ".GetAwaiter().GetResult()";

            if (!string.IsNullOrEmpty(ia.Tag))
            {
                sb.AppendLine($"{indent}{awaitKw}_cache.InvalidateByTagAsync(\"{Escape(ia.Tag!)}\"){tail};");
            }

            if (!string.IsNullOrEmpty(ia.KeyPattern))
            {
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    var __pattern = _keyStrategy.GeneratePattern(\"{Escape(ia.KeyPattern!)}\", __invArgs);");
                sb.AppendLine($"{indent}    {awaitKw}_cache.RemoveByPatternAsync(__pattern){tail};");
                sb.AppendLine($"{indent}}}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void EmitArgsDictionary(StringBuilder sb, IEnumerable<ParameterModel> parameters, string varName, string indent)
        {
            var list = parameters.ToList();
            sb.AppendLine($"{indent}var {varName} = new global::System.Collections.Generic.Dictionary<string, object?>({list.Count}, global::System.StringComparer.Ordinal)");
            sb.AppendLine($"{indent}{{");
            foreach (var p in list)
                sb.AppendLine($"{indent}    [\"{p.KeyAlias}\"] = {p.Name},");
            sb.AppendLine($"{indent}}};");
        }

        private static string FormatParam(ParameterModel p)
        {
            var refMod    = p.RefKind switch { RefKind.Ref => "ref ", RefKind.Out => "out ", RefKind.In => "in ", _ => "" };
            var paramsMod = p.IsParams ? "params " : "";
            return $"{paramsMod}{refMod}{p.TypeFQN} {p.Name}";
        }

        private static string CallArgs(ImmutableArray<ParameterModel> parameters)
            => string.Join(", ", parameters.Select(p =>
            {
                var m = p.RefKind switch { RefKind.Ref => "ref ", RefKind.Out => "out ", RefKind.In => "in ", _ => "" };
                return $"{m}{p.Name}";
            }));

        private static string InnerField(string ifaceFQN)
        {
            var parts = ifaceFQN.Split('.');
            return "_inner" + parts[parts.Length - 1].Replace(">", "").Replace("<", "");
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // DI registration extension emitter
    // ─────────────────────────────────────────────────────────────────────────────

    internal static class RegistrationEmitter
    {
        public static string Emit(ImmutableArray<ServiceModel> models)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated by Dragonfire.Caching.Generator");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();
            sb.AppendLine("namespace Dragonfire.Caching.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>DI registration for all compile-time caching proxies.</summary>");
            sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Dragonfire.Caching.Generator\", \"1.0.0\")]");
            sb.AppendLine("    public static class DragonfireGeneratedCachingExtensions");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Decorate every previously-registered <see cref=\"global::Dragonfire.Caching.Abstractions.ICacheable\"/>");
            sb.AppendLine("        /// service with its compile-time caching proxy. Call after all your service registrations,");
            sb.AppendLine("        /// and after <c>AddDragonfireCaching()</c> + a provider (memory/redis/hybrid).");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static {WellKnown.IServiceCollection} AddDragonfireGeneratedCaching(");
            sb.AppendLine($"            this {WellKnown.IServiceCollection} services)");
            sb.AppendLine("        {");

            foreach (var model in models)
            {
                foreach (var iface in model.InterfacesFQN)
                {
                    sb.AppendLine($"            // {model.ClassName} \u2192 {model.ProxyClassName}");
                    sb.AppendLine($"            DecorateService(services, typeof({iface}),");
                    sb.AppendLine($"                (inner, sp) => new {model.ProxyFQN}(");
                    sb.AppendLine($"                    ({iface})inner,");
                    sb.AppendLine($"                    sp.GetRequiredService<{WellKnown.ICacheService}>(),");
                    sb.AppendLine($"                    sp.GetRequiredService<{WellKnown.ICacheKeyStrategy}>()));");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("            return services;");
            sb.AppendLine("        }");
            sb.AppendLine();

            EmitDecorateHelper(sb);

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void EmitDecorateHelper(StringBuilder sb)
        {
            sb.AppendLine($"        private static void DecorateService(");
            sb.AppendLine($"            {WellKnown.IServiceCollection} services,");
            sb.AppendLine("            global::System.Type serviceType,");
            sb.AppendLine("            global::System.Func<object, global::System.IServiceProvider, object> decorator)");
            sb.AppendLine("        {");
            sb.AppendLine("            for (int i = services.Count - 1; i >= 0; i--)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (services[i].ServiceType != serviceType) continue;");
            sb.AppendLine("                var d = services[i];");
            sb.AppendLine($"                services[i] = {WellKnown.ServiceDescriptor}.Describe(");
            sb.AppendLine("                    serviceType,");
            sb.AppendLine("                    sp => decorator(ResolveInner(sp, d), sp),");
            sb.AppendLine("                    d.Lifetime);");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static object ResolveInner(");
            sb.AppendLine("            global::System.IServiceProvider sp,");
            sb.AppendLine($"            {WellKnown.ServiceDescriptor} d)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (d.ImplementationInstance != null) return d.ImplementationInstance;");
            sb.AppendLine("            if (d.ImplementationFactory != null) return d.ImplementationFactory(sp);");
            sb.AppendLine($"            return {WellKnown.ActivatorUtilities}.CreateInstance(sp, d.ImplementationType!);");
            sb.AppendLine("        }");
        }
    }
}
