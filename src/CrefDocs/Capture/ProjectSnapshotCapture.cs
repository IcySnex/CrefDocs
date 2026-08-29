using CrefDocs.Snapshot;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace CrefDocs.Capture;

internal sealed class ProjectSnapshotCapture
{
    private readonly DocumentationReader _documentation = new();

    public async Task<ApiSnapshot> CaptureAsync(
        CaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var projectPath = Path.GetFullPath(options.ProjectPath);
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("The project file does not exist.", projectPath);
        }

        EnsureMSBuildRegistered();

        var workspaceDiagnostics = new List<string>();
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = options.Configuration,
            ["TargetFramework"] = options.TargetFramework,
        };

        using var workspace = MSBuildWorkspace.Create(properties);
        workspace.LoadMetadataForReferencedProjects = true;
        workspace.RegisterWorkspaceFailedHandler(
            args =>
            {
                if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                {
                    workspaceDiagnostics.Add(args.Diagnostic.Message);
                }
            });

        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
        var compilation = await project.GetCompilationAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Could not compile {projectPath}.");

        var errors = compilation.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(20)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                $"The project contains compilation errors:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }

        if (workspaceDiagnostics.Count > 0)
        {
            var failures = string.Join(Environment.NewLine, workspaceDiagnostics.Distinct(StringComparer.Ordinal));
            throw new InvalidOperationException($"MSBuild could not load the project cleanly:{Environment.NewLine}{failures}");
        }

        var sourceRoot = Path.GetFullPath(options.SourceRoot ?? Path.GetDirectoryName(projectPath)!);
        var types = EnumerateTypes(compilation.Assembly.GlobalNamespace)
            .Where(IsPublicApiType)
            .Select(type => CaptureType(type, sourceRoot, cancellationToken))
            .OrderBy(type => type.Id, StringComparer.Ordinal)
            .ToArray();

        var toolVersion = typeof(ProjectSnapshotCapture).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new ApiSnapshot(
            ApiSnapshot.CurrentSchemaVersion,
            toolVersion,
            new ApiPackage(
                options.PackageId,
                options.PackageVersion,
                compilation.AssemblyName ?? project.AssemblyName ?? project.Name,
                options.TargetFramework),
            types);
    }

    private ApiType CaptureType(
        INamedTypeSymbol symbol,
        string sourceRoot,
        CancellationToken cancellationToken)
    {
        var documentation = _documentation.Read(symbol, cancellationToken);
        var regularMembers = symbol.GetMembers()
            .Where(IsPublicApiMember)
            .Select(member => CaptureMember(member, null, cancellationToken));
        var extensionMembers = symbol.GetTypeMembers()
            .Where(type => type.TypeKind == TypeKind.Extension)
            .SelectMany(extension => extension.GetMembers()
                .Where(IsPublicApiMember)
                .Select(member => CaptureMember(member, extension, cancellationToken)));
        var members = regularMembers
            .Concat(extensionMembers)
            .OrderBy(member => member.Kind)
            .ThenBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(member => member.Id, StringComparer.Ordinal)
            .ToArray();

        return new ApiType(
            GetRequiredDocumentationId(symbol),
            GetTypeName(symbol),
            symbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : symbol.ContainingNamespace.ToDisplayString(),
            GetTypeKind(symbol),
            SymbolFormatter.FormatTypeDeclaration(symbol),
            GetSourcePath(symbol, sourceRoot),
            symbol.ContainingType?.GetDocumentationCommentId(),
            GetBaseType(symbol),
            symbol.Interfaces.Select(CreateReference).ToArray(),
            documentation.Documentation,
            CaptureTypeParameters(symbol.TypeParameters, documentation),
            members);
    }

    private ApiMember CaptureMember(
        ISymbol symbol,
        INamedTypeSymbol? extension,
        CancellationToken cancellationToken)
    {
        var documentation = _documentation.Read(symbol, cancellationToken);
        var extensionDocumentation = extension is null
            ? DocumentationResult.Empty
            : _documentation.Read(extension, cancellationToken);
        var memberParameters = symbol switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol property when property.IsIndexer => property.Parameters,
            _ => [],
        };
        var parameters = extension?.ExtensionParameter is { } receiver
            ? new[] { receiver }.Concat(memberParameters)
            : memberParameters;
        var memberTypeParameters = symbol is IMethodSymbol genericMethod
            ? genericMethod.TypeParameters
            : [];
        var typeParameters = extension is null
            ? memberTypeParameters
            : extension.TypeParameters.Concat(memberTypeParameters);

        return new ApiMember(
            GetRequiredDocumentationId(symbol),
            GetMemberName(symbol),
            GetMemberKind(symbol),
            extension is null
                ? SymbolFormatter.FormatMemberDeclaration(symbol)
                : SymbolFormatter.FormatExtensionMemberDeclaration(symbol, extension),
            GetMemberType(symbol),
            documentation.Documentation,
            CaptureTypeParameters(typeParameters, documentation.FillMissingFrom(extensionDocumentation)),
            parameters.Select(parameter => new ApiParameter(
                parameter.Name,
                CreateReference(parameter.Type),
                parameter.HasExplicitDefaultValue ? FormatDefaultValue(parameter.ExplicitDefaultValue) : null,
                documentation.Parameters.GetValueOrDefault(parameter.Name) ??
                    extensionDocumentation.Parameters.GetValueOrDefault(parameter.Name))).ToArray(),
            documentation.Exceptions.Select(exception => new ApiException(
                CreateReference(exception.DocumentationId),
                exception.Description)).ToArray());
    }

    private static IReadOnlyList<ApiTypeParameter> CaptureTypeParameters(
        IEnumerable<ITypeParameterSymbol> parameters,
        DocumentationResult documentation)
    {
        return parameters.Select(parameter => new ApiTypeParameter(
            parameter.Name,
            SymbolFormatter.FormatConstraints(parameter),
            documentation.TypeParameters.GetValueOrDefault(parameter.Name))).ToArray();
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            foreach (var nested in EnumerateTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var type in EnumerateTypes(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var child in type.GetTypeMembers())
        {
            foreach (var nested in EnumerateTypes(child))
            {
                yield return nested;
            }
        }
    }

    private static bool IsPublicApiType(INamedTypeSymbol symbol)
    {
        return symbol.TypeKind != TypeKind.Extension &&
            !symbol.IsImplicitlyDeclared &&
            IsPublicApiAccessibility(symbol.DeclaredAccessibility) &&
            (symbol.ContainingType is null || IsPublicApiType(symbol.ContainingType));
    }

    private static bool IsPublicApiMember(ISymbol symbol)
    {
        if (symbol.IsImplicitlyDeclared &&
            !(symbol is IPropertySymbol { ContainingType.IsRecord: true }))
        {
            return false;
        }

        if (!IsPublicApiAccessibility(symbol.DeclaredAccessibility) &&
            !HasExplicitInterfaceImplementation(symbol))
        {
            return false;
        }

        return symbol switch
        {
            IMethodSymbol method => method.MethodKind is
                MethodKind.Constructor or
                MethodKind.Ordinary or
                MethodKind.UserDefinedOperator or
                MethodKind.Conversion,
            IPropertySymbol => true,
            IFieldSymbol field => !field.IsImplicitlyDeclared,
            IEventSymbol => true,
            _ => false,
        };
    }

    private static bool HasExplicitInterfaceImplementation(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => method.ExplicitInterfaceImplementations.Length > 0,
            IPropertySymbol property => property.ExplicitInterfaceImplementations.Length > 0,
            IEventSymbol @event => @event.ExplicitInterfaceImplementations.Length > 0,
            _ => false,
        };
    }

    private static bool IsPublicApiAccessibility(Accessibility accessibility)
    {
        return accessibility is
            Accessibility.Public or
            Accessibility.Protected or
            Accessibility.ProtectedOrInternal;
    }

    private static ApiTypeKind GetTypeKind(INamedTypeSymbol symbol)
    {
        if (symbol.IsRecord)
        {
            return symbol.IsValueType ? ApiTypeKind.RecordStruct : ApiTypeKind.RecordClass;
        }

        return symbol.TypeKind switch
        {
            TypeKind.Class => ApiTypeKind.Class,
            TypeKind.Struct => ApiTypeKind.Struct,
            TypeKind.Interface => ApiTypeKind.Interface,
            TypeKind.Enum => ApiTypeKind.Enum,
            TypeKind.Delegate => ApiTypeKind.Delegate,
            _ => throw new InvalidOperationException($"Unsupported public type kind {symbol.TypeKind}."),
        };
    }

    private static ApiMemberKind GetMemberKind(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } => ApiMemberKind.Constructor,
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion } => ApiMemberKind.Operator,
            IMethodSymbol => ApiMemberKind.Method,
            IPropertySymbol { IsIndexer: true } => ApiMemberKind.Indexer,
            IPropertySymbol => ApiMemberKind.Property,
            IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => ApiMemberKind.EnumValue,
            IFieldSymbol => ApiMemberKind.Field,
            IEventSymbol => ApiMemberKind.Event,
            _ => throw new InvalidOperationException($"Unsupported public member kind {symbol.Kind}."),
        };
    }

    private static string GetMemberName(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } => symbol.ContainingType.Name,
            IPropertySymbol { IsIndexer: true } => "this",
            _ => SymbolFormatter.FormatMemberName(symbol),
        };
    }

    private static ApiReference? GetBaseType(INamedTypeSymbol symbol)
    {
        if (symbol.TypeKind is TypeKind.Interface or TypeKind.Enum || symbol.BaseType is null)
        {
            return null;
        }

        if (symbol.BaseType.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType)
        {
            return null;
        }

        return CreateReference(symbol.BaseType);
    }

    private static ApiReference? GetMemberType(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } => null,
            IMethodSymbol method => CreateReference(method.ReturnType),
            IPropertySymbol property => CreateReference(property.Type),
            IFieldSymbol field => CreateReference(field.Type),
            IEventSymbol @event => CreateReference(@event.Type),
            _ => null,
        };
    }

    private static ApiReference CreateReference(ITypeSymbol symbol)
    {
        var identity = symbol switch
        {
            IArrayTypeSymbol array => array.ElementType,
            IPointerTypeSymbol pointer => pointer.PointedAtType,
            INamedTypeSymbol named => named.OriginalDefinition,
            _ => symbol,
        };

        return new ApiReference(
            SymbolFormatter.FormatReference(symbol),
            identity.GetDocumentationCommentId());
    }

    private static ApiReference CreateReference(string? documentationId)
    {
        if (string.IsNullOrWhiteSpace(documentationId))
        {
            return new ApiReference("unknown", null);
        }

        var separator = documentationId.IndexOf(':');
        var displayName = separator >= 0 ? documentationId[(separator + 1)..] : documentationId;
        var lastDot = displayName.LastIndexOf('.');
        if (lastDot >= 0)
        {
            displayName = displayName[(lastDot + 1)..];
        }

        return new ApiReference(displayName.Replace('`', '<'), documentationId);
    }

    private static string GetRequiredDocumentationId(ISymbol symbol)
    {
        return symbol.GetDocumentationCommentId()
            ?? throw new InvalidOperationException($"Could not create a documentation ID for {symbol}.");
    }

    private static string GetTypeName(INamedTypeSymbol symbol)
    {
        return symbol.TypeParameters.Length == 0
            ? symbol.Name
            : $"{symbol.Name}<{string.Join(", ", symbol.TypeParameters.Select(parameter => parameter.Name))}>";
    }

    private static string? GetSourcePath(INamedTypeSymbol symbol, string sourceRoot)
    {
        var paths = symbol.DeclaringSyntaxReferences
            .Select(reference => reference.SyntaxTree.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => Path.GetRelativePath(sourceRoot, path) is var relative &&
                relative != ".." &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderByDescending(path => string.Equals(
                Path.GetFileName(path),
                $"{symbol.Name}.cs",
                StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path.Count(character => character == '/'))
            .ThenBy(path => path.Contains('.', StringComparison.Ordinal))
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return paths.FirstOrDefault();
    }

    private static string? FormatDefaultValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            char character => $"'{character}'",
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private static void EnsureMSBuildRegistered()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }
}
