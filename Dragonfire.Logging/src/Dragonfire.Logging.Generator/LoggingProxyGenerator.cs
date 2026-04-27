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

namespace Dragonfire.Logging.Generator
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Attribute name constants (used for semantic-model look-up only)
    // ─────────────────────────────────────────────────────────────────────────────
    internal static class WellKnown
    {
        internal const string ILoggable          = "Dragonfire.Logging.Abstractions.ILoggable";
        internal const string LogAttribute       = "Dragonfire.Logging.Attributes.LogAttribute";
        internal const string LogIgnoreAttribute = "Dragonfire.Logging.Attributes.LogIgnoreAttribute";
        internal const string LogProperty        = "Dragonfire.Logging.Attributes.LogPropertyAttribute";

        // Generated code type references
        internal const string IServiceCollection = "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
        internal const string ServiceDescriptor  = "global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor";
        internal const string ActivatorUtilities = "global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities";

        // Default compile-time log level (used when no [Log] attribute is present)
        internal const string DefaultLogLevel = "Information";
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Data models — immutable, value-equality so incremental caching works
    // ─────────────────────────────────────────────────────────────────────────────

    internal enum MethodKind { Void, SyncReturn, Task, TaskOfT }

    /// <summary>A single [LogProperty]-decorated property found on a DTO type.</summary>
    internal sealed class LogPropertyOnType : IEquatable<LogPropertyOnType>
    {
        public string PropertyName { get; }   // C# property name  e.g. "ExternalReference"
        public string LogKey       { get; }   // scope key         e.g. "OrderRef"

        public LogPropertyOnType(string propertyName, string logKey)
        {
            PropertyName = propertyName;
            LogKey       = logKey;
        }

        public bool Equals(LogPropertyOnType? other)
            => other != null && PropertyName == other.PropertyName && LogKey == other.LogKey;
        public override bool Equals(object? obj) => Equals(obj as LogPropertyOnType);
        public override int GetHashCode() => (PropertyName?.GetHashCode() ?? 0) ^ (LogKey?.GetHashCode() ?? 0);
    }

    /// <summary>Baked compile-time values from [Log(...)]. Only Level is needed — no serialisation.</summary>
    internal sealed class AttributeModel : IEquatable<AttributeModel>
    {
        public string Level { get; }

        public AttributeModel(string level)
        {
            Level = level;
        }

        public bool Equals(AttributeModel? other) => other != null && Level == other.Level;
        public override bool Equals(object? obj) => Equals(obj as AttributeModel);
        public override int GetHashCode() => Level?.GetHashCode() ?? 0;
    }

    internal sealed class ParameterModel : IEquatable<ParameterModel>
    {
        public string  Name            { get; }
        public string  TypeFQN         { get; }
        public RefKind RefKind         { get; }
        public bool    IsParams        { get; }

        // [LogProperty] on the parameter declaration itself
        public bool    HasLogProperty  { get; }
        public string? LogPropertyName { get; }  // null → use Name

        // [LogProperty] on properties of this parameter's type (resolved at build time)
        public ImmutableArray<LogPropertyOnType> DtoLogProperties { get; }

        public ParameterModel(string name, string typeFQN, RefKind refKind, bool isParams,
            bool hasLogProperty, string? logPropertyName,
            ImmutableArray<LogPropertyOnType> dtoLogProperties)
        {
            Name             = name;
            TypeFQN          = typeFQN;
            RefKind          = refKind;
            IsParams         = isParams;
            HasLogProperty   = hasLogProperty;
            LogPropertyName  = logPropertyName;
            DtoLogProperties = dtoLogProperties;
        }

        public bool Equals(ParameterModel? other)
            => other != null && Name == other.Name && TypeFQN == other.TypeFQN;
        public override bool Equals(object? obj) => Equals(obj as ParameterModel);
        public override int GetHashCode() => (Name?.GetHashCode() ?? 0) ^ (TypeFQN?.GetHashCode() ?? 0);
    }

    internal sealed class MethodModel : IEquatable<MethodModel>
    {
        public string                              Name                   { get; }
        public MethodKind                          Kind                   { get; }
        public string                              ReturnTypeFQN          { get; }
        public string?                             TaskResultTypeFQN      { get; }
        public ImmutableArray<ParameterModel>      Parameters             { get; }
        public ImmutableArray<string>              TypeParameters         { get; }
        public bool                                HasLogIgnore           { get; }
        public AttributeModel?                     LogAttr                { get; }

        // [LogProperty] on properties of the Task<T> result type
        public ImmutableArray<LogPropertyOnType>   ResultTypeLogProperties { get; }

        public MethodModel(string name, MethodKind kind, string returnTypeFQN,
            string? taskResultFQN, ImmutableArray<ParameterModel> parameters,
            ImmutableArray<string> typeParameters, bool hasLogIgnore, AttributeModel? logAttr,
            ImmutableArray<LogPropertyOnType> resultTypeLogProperties)
        {
            Name                    = name;
            Kind                    = kind;
            ReturnTypeFQN           = returnTypeFQN;
            TaskResultTypeFQN       = taskResultFQN;
            Parameters              = parameters;
            TypeParameters          = typeParameters;
            HasLogIgnore            = hasLogIgnore;
            LogAttr                 = logAttr;
            ResultTypeLogProperties = resultTypeLogProperties;
        }

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
        public string                      ClassFQN       { get; }  // for ILogger<T>
        public ImmutableArray<string>      InterfacesFQN  { get; }
        public ImmutableArray<MethodModel> Methods        { get; }

        public ServiceModel(string className, string classNs, string classFQN,
            ImmutableArray<string> ifacesFQN, ImmutableArray<MethodModel> methods)
        {
            ClassName      = className;
            ClassNamespace = classNs;
            ClassFQN       = classFQN;
            InterfacesFQN  = ifacesFQN;
            Methods        = methods;
        }

        public string PrimaryIfaceFQN => InterfacesFQN[0];
        public string ProxyClassName  => $"{ClassName}LoggingProxy";
        public string ProxyNamespace  => $"{ClassNamespace}.Generated";
        public string ProxyFQN        => $"global::{ProxyNamespace}.{ProxyClassName}";

        public bool Equals(ServiceModel? other)
            => other != null && ClassName == other.ClassName && ClassNamespace == other.ClassNamespace;
        public override bool Equals(object? obj) => Equals(obj as ServiceModel);
        public override int GetHashCode()
            => (ClassName?.GetHashCode() ?? 0) ^ (ClassNamespace?.GetHashCode() ?? 0);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Incremental source generator entry point
    // ─────────────────────────────────────────────────────────────────────────────

    [Generator]
    public sealed class LoggingProxyGenerator : IIncrementalGenerator
    {
        // Diagnostic descriptor for surfacing generator exceptions in IDE / build output
        private static readonly DiagnosticDescriptor s_generatorError = new DiagnosticDescriptor(
            id:                 "DRG0001",
            title:              "Dragonfire.Logging.Generator error",
            messageFormat:      "An unexpected error occurred in Dragonfire.Logging.Generator: {0}",
            category:           "Dragonfire.Logging.Generator",
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

                    // One proxy file per service
                    foreach (var model in models)
                    {
                        try
                        {
                            spc.AddSource(
                                $"{model.ClassName}LoggingProxy.g.cs",
                                SourceText.From(ProxyEmitter.Emit(model), Encoding.UTF8));
                        }
                        catch (Exception ex)
                        {
                            var msg = $"Failed to emit proxy for {model.ClassName}: {ex}";
                            Console.Error.WriteLine($"[Dragonfire.Logging.Generator] {msg}");
                            spc.ReportDiagnostic(Diagnostic.Create(s_generatorError, Location.None, msg));
                        }
                    }

                    // Shared: DI registration + options + scope state (all in one file)
                    try
                    {
                        spc.AddSource(
                            "DragonfireGeneratedExtensions.g.cs",
                            SourceText.From(RegistrationEmitter.Emit(models), Encoding.UTF8));
                    }
                    catch (Exception ex)
                    {
                        var msg = $"Failed to emit DI registration extensions: {ex}";
                        Console.Error.WriteLine($"[Dragonfire.Logging.Generator] {msg}");
                        spc.ReportDiagnostic(Diagnostic.Create(s_generatorError, Location.None, msg));
                    }
                }
                catch (Exception ex)
                {
                    var msg = $"Unhandled exception in RegisterSourceOutput: {ex}";
                    Console.Error.WriteLine($"[Dragonfire.Logging.Generator] {msg}");
                    spc.ReportDiagnostic(Diagnostic.Create(s_generatorError, Location.None, msg));
                }
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Model extraction — walks the Roslyn semantic model at build time
    // ─────────────────────────────────────────────────────────────────────────────

    internal static class ModelExtractor
    {
        // Diagnostic descriptor for extraction errors (re-used from generator)
        private static readonly DiagnosticDescriptor s_extractError = new DiagnosticDescriptor(
            id:                 "DRG0002",
            title:              "Dragonfire.Logging.Generator model extraction error",
            messageFormat:      "Failed to extract logging model from {0}: {1}",
            category:           "Dragonfire.Logging.Generator",
            defaultSeverity:    DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static ServiceModel? Extract(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            try
            {
                var classDecl   = (ClassDeclarationSyntax)ctx.Node;
                var classSymbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;

                if (classSymbol is null || classSymbol.IsAbstract || classSymbol.IsGenericType)
                    return null;

                if (!classSymbol.AllInterfaces.Any(IsILoggable)) return null;

                var serviceIfaces = classSymbol.AllInterfaces
                    .Where(i => !IsILoggable(i) && !IsFrameworkInterface(i))
                    .ToImmutableArray();

                if (serviceIfaces.IsEmpty) return null;

                var classLogAttr = ReadLogAttribute(classSymbol);

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

                        var hasIgnore  = HasAttribute(member, WellKnown.LogIgnoreAttribute);
                        var methodAttr = ReadLogAttribute(member) ?? classLogAttr;

                        methods.Add(BuildMethod(member, ctx.SemanticModel.Compilation, hasIgnore, methodAttr));
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
            catch (OperationCanceledException)
            {
                throw; // always re-throw cancellation
            }
            catch (Exception ex)
            {
                var typeName = ((ClassDeclarationSyntax)ctx.Node).Identifier.Text;
                var msg = $"[Dragonfire.Logging.Generator] Error extracting model from {typeName}: {ex}";
                Console.Error.WriteLine(msg);
                // Note: we cannot call spc.ReportDiagnostic here (no SourceProductionContext),
                // but the console output will appear in the build output / IDE error list.
                return null;
            }
        }

        // ── Symbol helpers ────────────────────────────────────────────────────

        private static bool IsILoggable(INamedTypeSymbol i)
            => $"{i.ContainingNamespace}.{i.Name}" == WellKnown.ILoggable;

        private static bool IsFrameworkInterface(INamedTypeSymbol i)
        {
            var ns = i.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return ns.StartsWith("System", StringComparison.Ordinal)
                || ns.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || ns.StartsWith("Dragonfire.Logging", StringComparison.Ordinal);
        }

        private static bool HasAttribute(ISymbol symbol, string fullName)
            => symbol.GetAttributes().Any(a =>
                MatchAttrName(a.AttributeClass, fullName));

        private static bool MatchAttrName(INamedTypeSymbol? cls, string fullName)
        {
            if (cls is null) return false;
            var fqn = $"{cls.ContainingNamespace}.{cls.Name}";
            return fqn == fullName || fqn + "Attribute" == fullName;
        }

        // ── [Log] attribute reader — only Level is extracted; no serialisation config ──

        private static AttributeModel? ReadLogAttribute(ISymbol symbol)
        {
            var attr = symbol.GetAttributes().FirstOrDefault(a =>
                MatchAttrName(a.AttributeClass, WellKnown.LogAttribute));

            if (attr is null) return null;

            string level = WellKnown.DefaultLogLevel;

            foreach (var arg in attr.NamedArguments)
            {
                if (arg.Key == "Level")
                {
                    level = arg.Value.Value?.ToString() switch
                    {
                        "0" => "Trace",
                        "1" => "Debug",
                        "2" => "Information",
                        "3" => "Warning",
                        "4" => "Error",
                        "5" => "Critical",
                        _   => WellKnown.DefaultLogLevel
                    };
                }
            }

            return new AttributeModel(level);
        }

        // ── Method model builder ──────────────────────────────────────────────

        private static MethodModel BuildMethod(IMethodSymbol method, Compilation compilation,
            bool hasIgnore, AttributeModel? logAttr)
        {
            var kind     = GetMethodKind(method, compilation);
            var retFQN   = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string? taskResultFQN = null;
            var resultLogProps = ImmutableArray<LogPropertyOnType>.Empty;

            if (kind == MethodKind.TaskOfT
                && method.ReturnType is INamedTypeSymbol namedRet
                && !namedRet.TypeArguments.IsEmpty)
            {
                var resultType = namedRet.TypeArguments[0];
                taskResultFQN  = resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                resultLogProps = ExtractDtoLogProperties(resultType);
            }

            var typeParams = method.TypeParameters.Select(tp => tp.Name).ToImmutableArray();
            var parameters = method.Parameters.Select(p => BuildParameter(p)).ToImmutableArray();

            return new MethodModel(method.Name, kind, retFQN, taskResultFQN,
                parameters, typeParams, hasIgnore, logAttr, resultLogProps);
        }

        private static MethodKind GetMethodKind(IMethodSymbol method, Compilation compilation)
        {
            if (method.ReturnsVoid) return MethodKind.Void;

            var taskSymbol = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
            var taskOfT    = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");

            if (SymbolEqualityComparer.Default.Equals(method.ReturnType, taskSymbol))
                return MethodKind.Task;

            if (method.ReturnType is INamedTypeSymbol nt && nt.IsGenericType
                && SymbolEqualityComparer.Default.Equals(nt.OriginalDefinition, taskOfT))
                return MethodKind.TaskOfT;

            return MethodKind.SyncReturn;
        }

        private static ParameterModel BuildParameter(IParameterSymbol param)
        {
            var lpAttr = param.GetAttributes().FirstOrDefault(a =>
                MatchAttrName(a.AttributeClass, WellKnown.LogProperty));

            bool   hasLp = lpAttr != null;
            string? lpName = null;
            if (hasLp && lpAttr!.ConstructorArguments.Length > 0)
                lpName = lpAttr.ConstructorArguments[0].Value as string;

            // Inspect the parameter's type for [LogProperty]-decorated properties
            var dtoLogProps = IsSimpleType(param.Type)
                ? ImmutableArray<LogPropertyOnType>.Empty
                : ExtractDtoLogProperties(param.Type);

            return new ParameterModel(
                param.Name,
                param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                param.RefKind, param.IsParams,
                hasLp, lpName,
                dtoLogProps);
        }

        /// <summary>
        /// Scans a type's public instance properties for [LogProperty] and returns
        /// name/key pairs. Works for types in both the current compilation and
        /// referenced assemblies (both are visible through the semantic model).
        /// </summary>
        private static ImmutableArray<LogPropertyOnType> ExtractDtoLogProperties(ITypeSymbol type)
        {
            if (IsSimpleType(type)) return ImmutableArray<LogPropertyOnType>.Empty;

            var result = new List<LogPropertyOnType>();

            foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public) continue;

                var lpAttr = member.GetAttributes().FirstOrDefault(a =>
                    MatchAttrName(a.AttributeClass, WellKnown.LogProperty));

                if (lpAttr is null) continue;

                string? overrideName = lpAttr.ConstructorArguments.Length > 0
                    ? lpAttr.ConstructorArguments[0].Value as string
                    : null;

                result.Add(new LogPropertyOnType(member.Name, overrideName ?? member.Name));
            }

            return result.ToImmutableArray();
        }

        private static bool IsSimpleType(ITypeSymbol type)
        {
            if (type.SpecialType != SpecialType.None) return true;  // primitives, string, etc.
            if (type.TypeKind == TypeKind.Enum) return true;

            var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return fqn is "global::System.Guid"
                or "global::System.DateTime"
                or "global::System.DateTimeOffset"
                or "global::System.TimeSpan"
                or "global::System.Decimal"
                or "global::System.Threading.CancellationToken";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Proxy emitter
    // Generates a concrete proxy class using only ILogger<T> — no Dragonfire
    // runtime services, no JSON serialisation. Only [LogProperty]-annotated
    // parameters and DTO properties are promoted to customDimensions (via scope).
    // Every access is explicit, direct C# property/parameter read — zero reflection.
    // ─────────────────────────────────────────────────────────────────────────────

    internal static class ProxyEmitter
    {
        // ILogger generic — one per proxy class (typed to the concrete implementation)
        private static string LoggerFQN(ServiceModel m)
            => $"global::Microsoft.Extensions.Logging.ILogger<{m.ClassFQN}>";

        public static string Emit(ServiceModel model)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated by Dragonfire.Logging.Generator");
            sb.AppendLine("// No runtime Dragonfire dependencies — uses ILogger<T> directly.");
            sb.AppendLine("// No serialisation — only [LogProperty]-marked fields are promoted to customDimensions.");
            sb.AppendLine("#nullable enable");
            // LogInformation / LogError / etc. are extension methods — need the namespace in scope
            sb.AppendLine("using Microsoft.Extensions.Logging;");
            sb.AppendLine();
            sb.AppendLine($"namespace {model.ProxyNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// Compile-time logging proxy for <see cref=\"{model.ClassName}\"/>.");
            sb.AppendLine($"    /// Depends only on <c>ILogger&lt;{model.ClassName}&gt;</c>.");
            sb.AppendLine($"    /// All configuration baked from compile-time attributes — zero runtime options lookup.");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"Dragonfire.Logging.Generator\", \"1.0.0\")]");
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

        // ── Fields & constructor ──────────────────────────────────────────────

        private const string OptionsFQN = "global::Dragonfire.Logging.Generated.DragonfireGeneratedLoggingOptions";

        private static void EmitFields(StringBuilder sb, ServiceModel model)
        {
            foreach (var iface in model.InterfacesFQN)
                sb.AppendLine($"        private readonly {iface} {InnerField(iface)};");
            sb.AppendLine($"        private readonly {LoggerFQN(model)} _logger;");
            sb.AppendLine($"        private readonly {OptionsFQN} _options;");
            sb.AppendLine();
        }

        private static void EmitConstructor(StringBuilder sb, ServiceModel model)
        {
            sb.AppendLine($"        public {model.ProxyClassName}(");
            sb.AppendLine($"            {model.PrimaryIfaceFQN} inner,");
            sb.AppendLine($"            {LoggerFQN(model)} logger,");
            sb.AppendLine($"            {OptionsFQN} options)");
            sb.AppendLine("        {");
            foreach (var iface in model.InterfacesFQN)
                sb.AppendLine($"            {InnerField(iface)} = ({iface})inner;");
            sb.AppendLine("            _logger  = logger  ?? throw new global::System.ArgumentNullException(nameof(logger));");
            sb.AppendLine("            _options = options ?? new {OptionsFQN}();".Replace("{OptionsFQN}", OptionsFQN));
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // ── Method dispatch ───────────────────────────────────────────────────

        private static void EmitMethod(StringBuilder sb, ServiceModel model, MethodModel method)
        {
            var inner      = InnerField(model.PrimaryIfaceFQN);
            var asyncKw    = method.Kind is MethodKind.Task or MethodKind.TaskOfT ? "async " : "";
            var typeParams = method.TypeParameters.IsEmpty ? ""
                : $"<{string.Join(", ", method.TypeParameters)}>";
            var paramList  = string.Join(", ", method.Parameters.Select(FormatParam));

            sb.AppendLine($"        public {asyncKw}{method.ReturnTypeFQN} {method.Name}{typeParams}({paramList})");
            sb.AppendLine("        {");

            // Generic methods and [LogIgnore] methods → plain pass-through
            if (method.HasLogIgnore || !method.TypeParameters.IsEmpty)
            {
                EmitPassThrough(sb, method, inner);
                sb.AppendLine("        }");
                sb.AppendLine();
                return;
            }

            switch (method.Kind)
            {
                case MethodKind.Void:       EmitVoid(sb, model, method, inner);       break;
                case MethodKind.SyncReturn: EmitSyncReturn(sb, model, method, inner); break;
                case MethodKind.Task:       EmitTask(sb, model, method, inner);       break;
                case MethodKind.TaskOfT:    EmitTaskOfT(sb, model, method, inner);    break;
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static void EmitPassThrough(StringBuilder sb, MethodModel method, string inner)
        {
            var awaitKw = method.Kind is MethodKind.Task or MethodKind.TaskOfT ? "await " : "";
            var retKw   = method.Kind == MethodKind.Void ? "" : "return ";
            sb.AppendLine($"            {retKw}{awaitKw}{inner}.{method.Name}({CallArgs(method.Parameters)});");
        }

        // ── Four interception shapes ──────────────────────────────────────────

        private static void EmitVoid(StringBuilder sb, ServiceModel m, MethodModel method, string inner)
        {
            EmitScopeAndLogProperties(sb, m, method, "            ");
            sb.AppendLine("            var __sw = global::System.Diagnostics.Stopwatch.StartNew();");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine($"                {inner}.{method.Name}({CallArgs(method.Parameters)});");
            EmitSuccessLog(sb, m, method, "                ");
            sb.AppendLine("            }");
            EmitErrorCatch(sb, m, method, "            ");
        }

        private static void EmitSyncReturn(StringBuilder sb, ServiceModel m, MethodModel method, string inner)
        {
            EmitScopeAndLogProperties(sb, m, method, "            ");
            sb.AppendLine($"            {method.ReturnTypeFQN} __result = default!;");
            sb.AppendLine("            var __sw = global::System.Diagnostics.Stopwatch.StartNew();");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine($"                __result = {inner}.{method.Name}({CallArgs(method.Parameters)});");
            EmitResultLogProperties(sb, method, "                ");
            EmitNullResponseCheck(sb, method, "                ");
            EmitSuccessLog(sb, m, method, "                ");
            sb.AppendLine("            }");
            EmitErrorCatch(sb, m, method, "            ");
            sb.AppendLine("            return __result;");
        }

        private static void EmitTask(StringBuilder sb, ServiceModel m, MethodModel method, string inner)
        {
            EmitScopeAndLogProperties(sb, m, method, "            ");
            sb.AppendLine("            var __sw = global::System.Diagnostics.Stopwatch.StartNew();");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine($"                await {inner}.{method.Name}({CallArgs(method.Parameters)}).ConfigureAwait(false);");
            EmitSuccessLog(sb, m, method, "                ");
            sb.AppendLine("            }");
            EmitErrorCatch(sb, m, method, "            ");
        }

        private static void EmitTaskOfT(StringBuilder sb, ServiceModel m, MethodModel method, string inner)
        {
            EmitScopeAndLogProperties(sb, m, method, "            ");
            sb.AppendLine($"            {method.TaskResultTypeFQN} __result = default!;");
            sb.AppendLine("            var __sw = global::System.Diagnostics.Stopwatch.StartNew();");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine($"                __result = await {inner}.{method.Name}({CallArgs(method.Parameters)}).ConfigureAwait(false);");
            EmitResultLogProperties(sb, method, "                ");
            EmitNullResponseCheck(sb, method, "                ");
            EmitSuccessLog(sb, m, method, "                ");
            sb.AppendLine("            }");
            EmitErrorCatch(sb, m, method, "            ");
            sb.AppendLine("            return __result;");
        }

        // ── Scope + explicit [LogProperty] promotion ──────────────────────────
        // Only [LogProperty]-annotated parameters and DTO properties are written
        // to the scope. Every assignment is a direct C# field/property access —
        // no reflection, no JSON, no serialisation of any kind.

        private static void EmitScopeAndLogProperties(StringBuilder sb, ServiceModel m,
            MethodModel method, string indent)
        {
            // __DragonfireScopeState wraps a Dictionary but overrides ToString() so the
            // local JSON console formatter shows a readable "Message" instead of the type name.
            sb.AppendLine($"{indent}var __scope = new global::Dragonfire.Logging.Generated.__DragonfireScopeState(\"[Dragonfire] {m.ClassName}.{method.Name}\");");
            sb.AppendLine($"{indent}__scope[\"Dragonfire.ServiceName\"] = \"{m.ClassName}\";");
            sb.AppendLine($"{indent}__scope[\"Dragonfire.MethodName\"]  = \"{method.Name}\";");

            // ── Parameter-level [LogProperty] — guarded by options at runtime ──
            var paramProps = method.Parameters
                .Where(p => (p.HasLogProperty || !p.DtoLogProperties.IsEmpty) && p.RefKind == RefKind.None)
                .ToList();

            if (paramProps.Count > 0)
            {
                sb.AppendLine($"{indent}if (_options.LogRequestProperties)");
                sb.AppendLine($"{indent}{{");

                foreach (var p in paramProps.Where(p => p.HasLogProperty))
                {
                    var key = p.LogPropertyName ?? p.Name;
                    sb.AppendLine($"{indent}    // [LogProperty(\"{key}\")] on parameter '{p.Name}'");
                    sb.AppendLine($"{indent}    if (!_options.IsExcluded(\"{key}\"))");
                    sb.AppendLine($"{indent}        __scope[\"Request.{key}\"] = {p.Name};");
                }

                foreach (var p in paramProps.Where(p => !p.DtoLogProperties.IsEmpty))
                {
                    sb.AppendLine($"{indent}    // [LogProperty] on {p.TypeFQN} properties");
                    sb.AppendLine($"{indent}    if ({p.Name} is not null)");
                    sb.AppendLine($"{indent}    {{");
                    foreach (var dtoProp in p.DtoLogProperties)
                    {
                        sb.AppendLine($"{indent}        // [LogProperty(\"{dtoProp.LogKey}\")] on {p.TypeFQN}.{dtoProp.PropertyName}");
                        sb.AppendLine($"{indent}        if (!_options.IsExcluded(\"{dtoProp.LogKey}\"))");
                        sb.AppendLine($"{indent}            __scope[\"Request.{dtoProp.LogKey}\"] = {p.Name}.{dtoProp.PropertyName};");
                    }
                    sb.AppendLine($"{indent}    }}");
                }

                sb.AppendLine($"{indent}}}");
            }
        }

        // ── Result [LogProperty] promotion — direct property reads ────────────
        // Emitted only when the return type has [LogProperty]-annotated properties.
        // No serialisation — only explicit, named property accesses.

        private static void EmitResultLogProperties(StringBuilder sb, MethodModel method, string indent)
        {
            if (method.ResultTypeLogProperties.IsEmpty) return;

            sb.AppendLine($"{indent}// [LogProperty] on result type — guarded by options at runtime");
            sb.AppendLine($"{indent}if (_options.LogResponseProperties && __result is not null)");
            sb.AppendLine($"{indent}{{");
            foreach (var rp in method.ResultTypeLogProperties)
            {
                sb.AppendLine($"{indent}    // [LogProperty(\"{rp.LogKey}\")] on result.{rp.PropertyName}");
                sb.AppendLine($"{indent}    if (!_options.IsExcluded(\"{rp.LogKey}\"))");
                sb.AppendLine($"{indent}        __scope[\"Response.{rp.LogKey}\"] = __result.{rp.PropertyName};");
            }
            sb.AppendLine($"{indent}}}");
        }

        // ── Null response warning ─────────────────────────────────────────────
        // Emitted only for methods with a [Log] attribute that return a value
        // (SyncReturn / TaskOfT). The guard is both a compile-time attribute check
        // (method.LogAttr is not null) and a runtime options flag.

        private static void EmitNullResponseCheck(StringBuilder sb, MethodModel method, string indent)
        {
            // Only meaningful on methods that have the [Log] attribute; void/Task never return a value.
            if (method.LogAttr is null) return;

            sb.AppendLine($"{indent}// Null-response warning — only fires when [Log] is present and options.LogNullResponse = true");
            sb.AppendLine($"{indent}if (_options.LogNullResponse && __result is null)");
            sb.AppendLine($"{indent}    __scope[\"Dragonfire.NullResponse\"] = true;");
        }

        // ── Log calls — native ILogger.BeginScope + LogXxx ───────────────────

        private static void EmitSuccessLog(StringBuilder sb, ServiceModel m, MethodModel method, string indent)
        {
            // Baked compile-time fallback level; overridden at runtime if _options.OverrideLevel is set
            var bakedLevel = LevelToLogLevelEnum(method.LogAttr?.Level ?? WellKnown.DefaultLogLevel);

            sb.AppendLine($"{indent}__sw.Stop();");
            sb.AppendLine($"{indent}__scope[\"Dragonfire.ElapsedMs\"] = __sw.Elapsed.TotalMilliseconds;");
            sb.AppendLine($"{indent}using (_logger.BeginScope(__scope))");
            sb.AppendLine($"{indent}    _logger.Log(");
            sb.AppendLine($"{indent}        _options.OverrideLevel ?? {bakedLevel},");
            sb.AppendLine($"{indent}        \"[Dragonfire] {{Dragonfire_ServiceName}}.{{Dragonfire_MethodName}} completed in {{Dragonfire_ElapsedMs}}ms\",");
            sb.AppendLine($"{indent}        \"{m.ClassName}\", \"{method.Name}\", __sw.Elapsed.TotalMilliseconds);");
        }

        private static void EmitErrorCatch(StringBuilder sb, ServiceModel m, MethodModel method, string indent)
        {
            sb.AppendLine($"{indent}catch (global::System.Exception __ex)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    __sw.Stop();");
            sb.AppendLine($"{indent}    __scope[\"Dragonfire.ElapsedMs\"]    = __sw.Elapsed.TotalMilliseconds;");
            sb.AppendLine($"{indent}    __scope[\"Dragonfire.IsError\"]      = true;");
            sb.AppendLine($"{indent}    __scope[\"Dragonfire.ErrorMessage\"] = __ex.Message;");
            sb.AppendLine($"{indent}    if (_options.LogStackTrace)");
            sb.AppendLine($"{indent}        __scope[\"Dragonfire.StackTrace\"] = __ex.StackTrace;");
            sb.AppendLine($"{indent}    using (_logger.BeginScope(__scope))");
            sb.AppendLine($"{indent}        _logger.Log(");
            sb.AppendLine($"{indent}            _options.OverrideLevel ?? global::Microsoft.Extensions.Logging.LogLevel.Error,");
            sb.AppendLine($"{indent}            __ex,");
            sb.AppendLine($"{indent}            \"[Dragonfire] {{Dragonfire_ServiceName}}.{{Dragonfire_MethodName}} FAILED in {{Dragonfire_ElapsedMs}}ms \\u2014 {{Dragonfire_ErrorMessage}}\",");
            sb.AppendLine($"{indent}            \"{m.ClassName}\", \"{method.Name}\", __sw.Elapsed.TotalMilliseconds, __ex.Message);");
            sb.AppendLine($"{indent}    throw;");
            sb.AppendLine($"{indent}}}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string LevelToLogLevelEnum(string level) => level switch
        {
            "Trace"    => "global::Microsoft.Extensions.Logging.LogLevel.Trace",
            "Debug"    => "global::Microsoft.Extensions.Logging.LogLevel.Debug",
            "Warning"  => "global::Microsoft.Extensions.Logging.LogLevel.Warning",
            "Error"    => "global::Microsoft.Extensions.Logging.LogLevel.Error",
            "Critical" => "global::Microsoft.Extensions.Logging.LogLevel.Critical",
            _          => "global::Microsoft.Extensions.Logging.LogLevel.Information"
        };

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
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // DI registration extension emitter
    //
    // Emits a single DragonfireGeneratedExtensions.g.cs containing:
    //   • DragonfireGeneratedLoggingOptions  — runtime options injected into every proxy
    //   • __DragonfireScopeState             — scope wrapper with readable ToString()
    //   • DragonfireGeneratedExtensions      — AddDragonfireGeneratedLogging() + helpers
    // ─────────────────────────────────────────────────────────────────────────────

    internal static class RegistrationEmitter
    {
        public static string Emit(ImmutableArray<ServiceModel> models)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated by Dragonfire.Logging.Generator");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine("using Microsoft.Extensions.Logging;");
            sb.AppendLine();
            sb.AppendLine("namespace Dragonfire.Logging.Generated");
            sb.AppendLine("{");

            // ── Options ───────────────────────────────────────────────────────
            sb.AppendLine("    /// <summary>Runtime options for all Dragonfire compile-time logging proxies.</summary>");
            sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Dragonfire.Logging.Generator\", \"1.0.0\")]");
            sb.AppendLine("    public sealed class DragonfireGeneratedLoggingOptions");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>Promote Request.* customDimensions from [LogProperty] on parameters. Default: true.</summary>");
            sb.AppendLine("        public bool LogRequestProperties { get; set; } = true;");
            sb.AppendLine("        /// <summary>Promote Response.* customDimensions from [LogProperty] on return type. Default: true.</summary>");
            sb.AppendLine("        public bool LogResponseProperties { get; set; } = true;");
            sb.AppendLine("        /// <summary>Include Dragonfire.StackTrace in error scope. Default: true.</summary>");
            sb.AppendLine("        public bool LogStackTrace { get; set; } = true;");
            sb.AppendLine("        /// <summary>Bare property names to suppress (case-insensitive). Matches both Request.X and Response.X.</summary>");
            sb.AppendLine("        public global::System.Collections.Generic.ISet<string> ExcludeProperties { get; set; }");
            sb.AppendLine("            = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.OrdinalIgnoreCase);");
            sb.AppendLine("        /// <summary>Override success log level for all methods. null = use [Log(Level=...)] per method.</summary>");
            sb.AppendLine("        public global::Microsoft.Extensions.Logging.LogLevel? OverrideLevel { get; set; }");
            sb.AppendLine("        /// <summary>Log a warning when a method decorated with [Log] returns null. Default: false.</summary>");
            sb.AppendLine("        public bool LogNullResponse { get; set; } = false;");
            sb.AppendLine("        internal bool IsExcluded(string bareKey) => ExcludeProperties.Contains(bareKey);");
            sb.AppendLine("    }");
            sb.AppendLine();

            // ── Scope state ───────────────────────────────────────────────────
            sb.AppendLine("    /// <summary>Scope bag with readable ToString() for JsonConsoleFormatter.</summary>");
            sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Dragonfire.Logging.Generator\", \"1.0.0\")]");
            sb.AppendLine("    [global::System.Diagnostics.DebuggerNonUserCode]");
            sb.AppendLine("    internal sealed class __DragonfireScopeState");
            sb.AppendLine("        : global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object?>>");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly global::System.Collections.Generic.Dictionary<string, object?> _data =");
            sb.AppendLine("            new global::System.Collections.Generic.Dictionary<string, object?>(global::System.StringComparer.OrdinalIgnoreCase);");
            sb.AppendLine("        private readonly string _message;");
            sb.AppendLine("        internal __DragonfireScopeState(string message) => _message = message;");
            sb.AppendLine("        internal object? this[string key] { get => _data.TryGetValue(key, out var v) ? v : null; set => _data[key] = value; }");
            sb.AppendLine("        public override string ToString() => _message;");
            sb.AppendLine("        public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<string, object?>> GetEnumerator() => _data.GetEnumerator();");
            sb.AppendLine("        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => _data.GetEnumerator();");
            sb.AppendLine("    }");
            sb.AppendLine();

            // ── DI extensions ─────────────────────────────────────────────────
            sb.AppendLine("    /// <summary>DI registration for all compile-time logging proxies.</summary>");
            sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Dragonfire.Logging.Generator\", \"1.0.0\")]");
            sb.AppendLine("    public static class DragonfireGeneratedExtensions");
            sb.AppendLine("    {");
            sb.AppendLine($"        public static {WellKnown.IServiceCollection} AddDragonfireGeneratedLogging(");
            sb.AppendLine($"            this {WellKnown.IServiceCollection} services,");
            sb.AppendLine("            global::System.Action<DragonfireGeneratedLoggingOptions>? configure = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            var __options = new DragonfireGeneratedLoggingOptions();");
            sb.AppendLine("            configure?.Invoke(__options);");
            sb.AppendLine("            services.AddSingleton(__options);");
            sb.AppendLine();

            foreach (var model in models)
            {
                var loggerFQN  = $"global::Microsoft.Extensions.Logging.ILogger<{model.ClassFQN}>";

                foreach (var iface in model.InterfacesFQN)
                {
                    sb.AppendLine($"            // {model.ClassName} \u2192 {model.ProxyClassName}");
                    sb.AppendLine($"            DecorateService(services, typeof({iface}),");
                    sb.AppendLine($"                (inner, sp) => new {model.ProxyFQN}(");
                    sb.AppendLine($"                    ({iface})inner,");
                    sb.AppendLine($"                    sp.GetRequiredService<{loggerFQN}>(),");
                    sb.AppendLine($"                    sp.GetRequiredService<DragonfireGeneratedLoggingOptions>()));");
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
