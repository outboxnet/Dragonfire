using Dragonfire.Torch.Naming;
using Dragonfire.Torch.Schema;

namespace Dragonfire.Torch.Emit;

/// <summary>
/// Maps the parser's <see cref="ClientIR"/> into the per-template DTOs that
/// <see cref="TemplateRenderer"/> binds against. Keeping this stage pure (no
/// IO) makes the emit pipeline easy to test by snapshotting the produced
/// models.
/// </summary>
public static class ModelShaper
{
    public static ConstantsModel Constants(ClientIR ir) => new()
    {
        Namespace = ir.Namespace,
        Headers = ir.CommonHeaders.Select(h => new HeaderConstant
        {
            Identifier = HeaderIdentifier(h.Key),
            Key = h.Key,
            Value = h.Value,
        }).ToList(),
    };

    public static EndpointsModel Endpoints(ClientIR ir) => new()
    {
        Namespace = ir.Namespace,
        Operations = ir.Operations.Select(o => new EndpointConstant
        {
            Name = o.Name,
            Path = o.PathTemplate,
        }).ToList(),
    };

    public static OptionsModel ClientOptions(ClientIR ir) => new()
    {
        Namespace = ir.Namespace,
        Prefix = ir.ClientName,
        BaseUrl = ir.BaseUrl,
        Headers = ir.CommonHeaders.Select(h => new HeaderConstant
        {
            Identifier = HeaderIdentifier(h.Key),
            Key = h.Key,
            Value = h.Value,
        }).ToList(),
    };

    public static PrefixedNamespaceModel Prefixed(ClientIR ir) => new()
    {
        Namespace = ir.Namespace,
        Prefix = ir.ClientName,
    };

    public static CsprojModel Csproj(ClientIR ir, string targetFramework) => new()
    {
        Namespace = ir.Namespace,
        TargetFramework = targetFramework,
    };

    public static ModelFileModel ModelFile(ClientIR ir, TypeIR type) => new()
    {
        Namespace = ir.Namespace,
        Name = type.Name,
        IsAbstract = type.IsAbstract,
        IsStub = type.IsStub,
        BaseClassName = type.BaseClassName,
        Properties = type.Properties.Select(p => new ModelProperty
        {
            JsonName = p.JsonName,
            CsharpType = p.CSharpType,
            Name = p.Name,
            Initializer = ChooseInitializer(p),
        }).ToList(),
    };

    public static ClientInterfaceModel ClientInterface(ClientIR ir) => new()
    {
        Namespace = ir.Namespace,
        Prefix = ir.ClientName,
        Operations = ir.Operations.Select(BuildInterfaceOp).ToList(),
    };

    public static ClientImplModel ClientImpl(ClientIR ir) => new()
    {
        Namespace = ir.Namespace,
        Prefix = ir.ClientName,
        Operations = ir.Operations.Select(BuildImplOp).ToList(),
    };

    private static InterfaceOperation BuildInterfaceOp(OperationIR op) => new()
    {
        MethodName = op.Name + "Async",
        ResponseType = ResolveResponseType(op),
        Parameters = BuildSignatureParameters(op),
    };

    private static ImplOperation BuildImplOp(OperationIR op)
    {
        var requestType = op.RequestBody?.CSharpType ?? "object";
        var hasPath = op.PathParams.Count > 0;
        var hasQuery = op.QueryParams.Count > 0;
        var hasBody = op.RequestBody is not null;

        var formFields = new List<ImplFormField>();
        if (op.BodyKind is BodyKind.FormUrlEncoded or BodyKind.FormData && op.RequestBody is not null)
        {
            formFields.AddRange(op.RequestBody.Properties.Select(p => new ImplFormField
            {
                Name = p.Name,
                JsonName = p.JsonName,
                IsFile = p.IsFile,
                IsString = p.IsString,
                IsNullable = p.IsNullable,
            }));
        }

        return new ImplOperation
        {
            Name = op.Name,
            MethodName = op.Name + "Async",
            PathTemplate = op.PathTemplate,
            HttpMethod = op.HttpMethod,
            HttpMethodPascal = ToPascal(op.HttpMethod),
            ResponseType = ResolveResponseType(op),
            RequestType = requestType,
            HasBody = hasBody,
            BodyKind = BodyKindTag(op.BodyKind),
            HasPathParams = hasPath,
            HasQueryParams = hasQuery,
            Parameters = BuildSignatureParameters(op),
            PathParams = op.PathParams.Select(p => new ImplPathParam
            {
                Token = p.Name,
                CsharpName = IdentifierSanitizer.CamelCase(p.Name),
                ToStringSuffix = p.CSharpType == "string" ? "" : ".ToString()",
            }).ToList(),
            QueryParams = op.QueryParams.Select(q => new ImplQueryParam
            {
                Name = q.Name,
                JsonName = q.JsonName,
                FormatExpr = q.Name,
            }).ToList(),
            FormFields = formFields,
        };
    }

    private static string BodyKindTag(BodyKind kind) => kind switch
    {
        BodyKind.Json => "json",
        BodyKind.FormUrlEncoded => "form_url_encoded",
        BodyKind.FormData => "form_data",
        _ => "none",
    };

    private static List<MethodParameter> BuildSignatureParameters(OperationIR op)
    {
        var ps = new List<MethodParameter>();
        foreach (var pp in op.PathParams)
            ps.Add(new MethodParameter { CsharpType = pp.CSharpType, Name = IdentifierSanitizer.CamelCase(pp.Name) });

        if (op.RequestBody is not null)
            ps.Add(new MethodParameter { CsharpType = op.RequestBody.CSharpType, Name = "body" });

        foreach (var qp in op.QueryParams)
            ps.Add(new MethodParameter { CsharpType = qp.CSharpType, Name = qp.Name });

        return ps;
    }

    private static string ResolveResponseType(OperationIR op)
        => op.ResponseBody?.CSharpType ?? "Unit";

    private static string ChooseInitializer(PropertyIR p)
    {
        if (p.IsCollection) return " = new();";
        if (p.IsString && !p.IsNullable) return " = \"\";";
        return "";
    }

    /// <summary>
    /// Turns "X-Api-Version" into "ApiVersion", "Authorization" into "Authorization".
    /// Leading "X-" prefixes are stripped because the constant lives on a class
    /// already named <c>Headers</c>.
    /// </summary>
    private static string HeaderIdentifier(string key)
    {
        var trimmed = key;
        if (trimmed.StartsWith("X-", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[2..];
        return IdentifierSanitizer.PascalCase(trimmed);
    }

    private static string ToPascal(string verb) => verb.ToUpperInvariant() switch
    {
        "GET" => "Get",
        "POST" => "Post",
        "PUT" => "Put",
        "DELETE" => "Delete",
        "PATCH" => "Patch",
        "HEAD" => "Head",
        "OPTIONS" => "Options",
        _ => "Get",
    };
}
