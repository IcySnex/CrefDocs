namespace CrefDocs.Snapshot;

internal sealed record ApiSnapshot(
    int SchemaVersion,
    string ToolVersion,
    ApiPackage Package,
    IReadOnlyList<ApiType> Types)
{
    public const int CurrentSchemaVersion = 1;
}

internal sealed record ApiPackage(
    string Id,
    string Version,
    string AssemblyName,
    string TargetFramework);

internal sealed record ApiType(
    string Id,
    string Name,
    string Namespace,
    ApiTypeKind Kind,
    string Declaration,
    string? SourcePath,
    string? ContainingTypeId,
    ApiReference? BaseType,
    IReadOnlyList<ApiReference> Interfaces,
    ApiDocumentation Documentation,
    IReadOnlyList<ApiTypeParameter> TypeParameters,
    IReadOnlyList<ApiMember> Members);

internal enum ApiTypeKind
{
    Class,
    Struct,
    RecordClass,
    RecordStruct,
    Interface,
    Enum,
    Delegate,
}

internal sealed record ApiMember(
    string Id,
    string Name,
    ApiMemberKind Kind,
    string Declaration,
    ApiReference? Type,
    ApiDocumentation Documentation,
    IReadOnlyList<ApiTypeParameter> TypeParameters,
    IReadOnlyList<ApiParameter> Parameters,
    IReadOnlyList<ApiException> Exceptions);

internal enum ApiMemberKind
{
    Constructor,
    Method,
    Property,
    Indexer,
    Field,
    EnumValue,
    Event,
    Operator,
}

internal sealed record ApiReference(
    string DisplayName,
    string? DocumentationId);

internal sealed record ApiTypeParameter(
    string Name,
    string? Constraints,
    string? Description);

internal sealed record ApiParameter(
    string Name,
    ApiReference Type,
    string? DefaultValue,
    string? Description);

internal sealed record ApiException(
    ApiReference Type,
    string? Description);

internal sealed record ApiDocumentation(
    string? Summary,
    string? Remarks,
    string? Returns,
    string? Example)
{
    public static ApiDocumentation Empty { get; } = new(null, null, null, null);
}

