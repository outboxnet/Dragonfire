namespace Dragonfire.Torch.Emit;

/// <summary>
/// Plain DTOs handed to Scriban. Property casing follows C# (PascalCase);
/// Scriban's <c>StandardMemberRenamer</c> exposes them as snake_case to
/// templates (so <c>BaseUrl</c> becomes <c>base_url</c>).
/// </summary>
public sealed class CsprojModel
{
    public string Namespace { get; set; } = "";
    public string TargetFramework { get; set; } = "net8.0";
}

public sealed class ConstantsModel
{
    public string Namespace { get; set; } = "";
    public List<HeaderConstant> Headers { get; set; } = new();
}

public sealed class HeaderConstant
{
    public string Identifier { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class EndpointsModel
{
    public string Namespace { get; set; } = "";
    public List<EndpointConstant> Operations { get; set; } = new();
}

public sealed class EndpointConstant
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class OptionsModel
{
    public string Namespace { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public List<HeaderConstant> Headers { get; set; } = new();
}

public sealed class PrefixedNamespaceModel
{
    public string Namespace { get; set; } = "";
    public string Prefix { get; set; } = "";
}

public sealed class ModelFileModel
{
    public string Namespace { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsAbstract { get; set; }
    public bool IsStub { get; set; }
    public string? BaseClassName { get; set; }
    public List<ModelProperty> Properties { get; set; } = new();
}

public sealed class ModelProperty
{
    public string JsonName { get; set; } = "";
    public string CsharpType { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initializer { get; set; } = "";
}

public sealed class ClientInterfaceModel
{
    public string Namespace { get; set; } = "";
    public string Prefix { get; set; } = "";
    public List<InterfaceOperation> Operations { get; set; } = new();
}

public sealed class InterfaceOperation
{
    public string MethodName { get; set; } = "";
    public string ResponseType { get; set; } = "Unit";
    public List<MethodParameter> Parameters { get; set; } = new();
}

public sealed class MethodParameter
{
    public string CsharpType { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class ClientImplModel
{
    public string Namespace { get; set; } = "";
    public string Prefix { get; set; } = "";
    public List<ImplOperation> Operations { get; set; } = new();
}

public sealed class ImplOperation
{
    public string Name { get; set; } = "";              // Endpoints.<this>
    public string MethodName { get; set; } = "";        // CreateTenantAsync
    public string PathTemplate { get; set; } = "";
    public string HttpMethod { get; set; } = "GET";
    public string HttpMethodPascal { get; set; } = "Get";
    public string ResponseType { get; set; } = "Unit";
    public string RequestType { get; set; } = "object";
    public bool HasBody { get; set; }

    /// <summary>
    /// Lower-case body kind tag ("none" / "json" / "form_url_encoded" /
    /// "form_data") consumed by the client template to pick which
    /// <c>HttpContent</c> shape to emit.
    /// </summary>
    public string BodyKind { get; set; } = "none";

    public bool HasPathParams { get; set; }
    public bool HasQueryParams { get; set; }
    public List<MethodParameter> Parameters { get; set; } = new();
    public List<ImplPathParam> PathParams { get; set; } = new();
    public List<ImplQueryParam> QueryParams { get; set; } = new();

    /// <summary>
    /// Populated for form-encoded (urlencoded / multipart) bodies. Empty for
    /// JSON or no-body operations.
    /// </summary>
    public List<ImplFormField> FormFields { get; set; } = new();
}

public sealed class ImplFormField
{
    public string Name { get; set; } = "";          // body.<Name> getter
    public string JsonName { get; set; } = "";      // wire field name
    public bool IsFile { get; set; }
    public bool IsString { get; set; }              // true for string properties (no .ToString())
    public bool IsNullable { get; set; }
}

public sealed class ImplPathParam
{
    public string Token { get; set; } = "";          // "id" — without braces
    public string CsharpName { get; set; } = "";     // C# parameter name
    public string ToStringSuffix { get; set; } = ""; // ".ToString()" if not string
}

public sealed class ImplQueryParam
{
    public string Name { get; set; } = "";
    public string JsonName { get; set; } = "";
    public string FormatExpr { get; set; } = "";
}
