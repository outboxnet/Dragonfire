namespace Dragonfire.ApiClientGen.Schema;

public sealed class OperationIR
{
    /// <summary>PascalCased C# method base name without the <c>Async</c> suffix, e.g. "CreateTenant".</summary>
    public string Name { get; set; } = "";

    /// <summary>Uppercase HTTP verb: "GET", "POST", "PUT", "DELETE", "PATCH".</summary>
    public string HttpMethod { get; set; } = "GET";

    /// <summary>Path template with <c>{name}</c> placeholders, e.g. <c>/tenants/{id}</c>.</summary>
    public string PathTemplate { get; set; } = "";

    public List<PathParamIR> PathParams { get; set; } = new();

    public List<QueryParamIR> QueryParams { get; set; } = new();

    /// <summary>Request-specific headers — common headers are filtered out.</summary>
    public List<HeaderIR> ExtraHeaders { get; set; } = new();

    /// <summary>Null for GET / DELETE or when no body present.</summary>
    public TypeIR? RequestBody { get; set; }

    /// <summary>How the request body should be encoded on the wire.</summary>
    public BodyKind BodyKind { get; set; } = BodyKind.None;

    /// <summary>Null when no example body and no override entry.</summary>
    public TypeIR? ResponseBody { get; set; }

    /// <summary>Status code of the example response we picked, if any.</summary>
    public int? ExampleStatusCode { get; set; }
}

public sealed class PathParamIR
{
    public string Name { get; set; } = "";       // e.g. "id"
    public string CSharpType { get; set; } = "string";
}

public sealed class QueryParamIR
{
    public string Name { get; set; } = "";        // C# parameter name
    public string JsonName { get; set; } = "";    // wire name (often = Name)
    public string CSharpType { get; set; } = "string?";
    public bool IsNullable { get; set; } = true;
}

public sealed class HeaderIR
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsCommon { get; set; }
}

/// <summary>
/// Body encoding for a request. <see cref="None"/> covers GET / DELETE and any
/// other operation without a body. <see cref="FormUrlEncoded"/> emits
/// <c>application/x-www-form-urlencoded</c>; <see cref="FormData"/> emits
/// <c>multipart/form-data</c> and supports file fields.
/// </summary>
public enum BodyKind
{
    None,
    Json,
    FormUrlEncoded,
    FormData,
}
