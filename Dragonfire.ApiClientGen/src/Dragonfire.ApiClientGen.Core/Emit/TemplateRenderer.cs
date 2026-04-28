using System.Reflection;
using Scriban;
using Scriban.Runtime;

namespace Dragonfire.ApiClientGen.Emit;

/// <summary>
/// Loads Scriban templates from embedded resources and renders them with a
/// supplied model object. Templates live under
/// <c>Dragonfire.ApiClientGen.Core/Templates/*.sbn</c> and are embedded via
/// the project's <c>EmbeddedResource</c> include.
/// </summary>
public sealed class TemplateRenderer
{
    private readonly Assembly _assembly;
    private readonly string _resourcePrefix;
    private readonly Dictionary<string, Template> _cache = new(StringComparer.Ordinal);

    public TemplateRenderer()
    {
        _assembly = typeof(TemplateRenderer).Assembly;
        // .NET uses the project's <RootNamespace> as the resource prefix
        // (NOT the assembly name). The Core project sets RootNamespace to
        // "Dragonfire.ApiClientGen" so templates show up as
        // "Dragonfire.ApiClientGen.Templates.<File>.sbn".
        _resourcePrefix = "Dragonfire.ApiClientGen.Templates.";
    }

    public string Render(string templateName, object model)
    {
        var template = LoadTemplate(templateName);

        var so = new ScriptObject();
        so.Import(model, renamer: m => StandardMemberRenamer.Rename(m));
        var ctx = new TemplateContext { MemberRenamer = m => StandardMemberRenamer.Rename(m) };
        ctx.PushGlobal(so);

        var rendered = template.Render(ctx);
        return NormaliseLineEndings(rendered);
    }

    private Template LoadTemplate(string templateName)
    {
        if (_cache.TryGetValue(templateName, out var cached)) return cached;

        var resource = _resourcePrefix + templateName + ".sbn";
        using var stream = _assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded template '{resource}' not found.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        var template = Template.Parse(text, templateName);
        if (template.HasErrors)
            throw new InvalidOperationException(
                $"Template '{templateName}' has errors:{Environment.NewLine}" +
                string.Join(Environment.NewLine, template.Messages));

        _cache[templateName] = template;
        return template;
    }

    private static string NormaliseLineEndings(string s)
    {
        // Scriban produces \n on Windows. Normalise to platform native to keep
        // generated files consistent regardless of where the tool runs.
        var lf = s.Replace("\r\n", "\n");
        return Environment.NewLine == "\n" ? lf : lf.Replace("\n", Environment.NewLine);
    }
}
