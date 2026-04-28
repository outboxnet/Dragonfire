namespace Dragonfire.ApiClientGen.Schema;

/// <summary>
/// Describes a C# model type. May be a named class (<see cref="Name"/> set,
/// <see cref="Properties"/> populated) or a leaf reference to a primitive /
/// collection type (<see cref="CSharpType"/> populated, <see cref="Properties"/>
/// empty).
/// </summary>
public sealed class TypeIR
{
    /// <summary>Class name, e.g. "Tenant". Empty when <see cref="IsClass"/> is false.</summary>
    public string Name { get; set; } = "";

    /// <summary>The fully-qualified C# type as it appears in source, e.g. "string", "List&lt;Tenant&gt;", "DateTimeOffset?".</summary>
    public string CSharpType { get; set; } = "";

    /// <summary>True when this type is an emitted class with properties; false for primitive / collection leaves.</summary>
    public bool IsClass { get; set; }

    public List<PropertyIR> Properties { get; set; } = new();

    /// <summary>
    /// When base-class promotion fires, the lifted base class is referenced
    /// here and the lifted properties are removed from <see cref="Properties"/>.
    /// </summary>
    public string? BaseClassName { get; set; }

    /// <summary>True for emitted abstract base classes; concrete models stay false.</summary>
    public bool IsAbstract { get; set; }

    /// <summary>True if this type was a stub (no example body available, no override).</summary>
    public bool IsStub { get; set; }

    /// <summary>
    /// Group this type belongs to for base-class promotion purposes. Request
    /// DTOs and response DTOs are promoted independently — sharing accidental
    /// scalar fields across the request/response boundary is rarely useful and
    /// produces awkward base classes.
    /// </summary>
    public TypeRole Role { get; set; } = TypeRole.Other;

    public static TypeIR Primitive(string csharpType) => new() { CSharpType = csharpType, IsClass = false };

    public static TypeIR Class(string name) => new() { Name = name, CSharpType = name, IsClass = true };
}

public enum TypeRole
{
    Other,
    Request,
    Response,
}

public sealed class PropertyIR
{
    /// <summary>C# property name (PascalCase, identifier-safe).</summary>
    public string Name { get; set; } = "";

    /// <summary>Original JSON property name — emitted as <c>[JsonPropertyName(...)]</c>.</summary>
    public string JsonName { get; set; } = "";

    /// <summary>Fully-rendered C# type, e.g. "string", "List&lt;Tenant&gt;", "DateTimeOffset?".</summary>
    public string CSharpType { get; set; } = "";

    public bool IsNullable { get; set; }

    /// <summary>True when the property is a string (used by emitter to add <c>= ""</c> initialiser).</summary>
    public bool IsString { get; set; }

    /// <summary>True when the property is a generic collection (used to add <c>= new()</c> initialiser).</summary>
    public bool IsCollection { get; set; }

    /// <summary>
    /// True when the property represents a file upload field on a multipart
    /// form body. The emitter renders the property as <c>Stream?</c> and wraps
    /// it in <c>StreamContent</c> at send time.
    /// </summary>
    public bool IsFile { get; set; }
}
