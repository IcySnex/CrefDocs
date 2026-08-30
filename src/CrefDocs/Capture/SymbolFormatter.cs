using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace CrefDocs.Capture;

internal static class SymbolFormatter
{
    private static readonly SymbolDisplayFormat MemberDeclarationFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions:
            SymbolDisplayGenericsOptions.IncludeTypeParameters |
            SymbolDisplayGenericsOptions.IncludeTypeConstraints,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeAccessibility |
            SymbolDisplayMemberOptions.IncludeModifiers |
            SymbolDisplayMemberOptions.IncludeType |
            SymbolDisplayMemberOptions.IncludeParameters |
            SymbolDisplayMemberOptions.IncludeExplicitInterface,
        delegateStyle: SymbolDisplayDelegateStyle.NameAndSignature,
        extensionMethodStyle: SymbolDisplayExtensionMethodStyle.StaticMethod,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeExtensionThis |
            SymbolDisplayParameterOptions.IncludeParamsRefOut |
            SymbolDisplayParameterOptions.IncludeType |
            SymbolDisplayParameterOptions.IncludeName |
            SymbolDisplayParameterOptions.IncludeDefaultValue,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly SymbolDisplayFormat ReferenceFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static string FormatTypeDeclaration(INamedTypeSymbol symbol)
    {
        var declaration = symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<MemberDeclarationSyntax>()
            .OrderByDescending(syntax => string.Equals(
                Path.GetFileName(syntax.SyntaxTree.FilePath),
                $"{symbol.Name}.cs",
                StringComparison.OrdinalIgnoreCase))
            .ThenBy(syntax => syntax.SyntaxTree.FilePath, StringComparer.Ordinal)
            .FirstOrDefault();

        if (declaration is not null)
        {
            var end = declaration switch
            {
                BaseTypeDeclarationSyntax type
                    when !type.OpenBraceToken.IsKind(SyntaxKind.None) && !type.OpenBraceToken.IsMissing =>
                    type.OpenBraceToken.SpanStart,
                BaseTypeDeclarationSyntax type
                    when !type.SemicolonToken.IsKind(SyntaxKind.None) && !type.SemicolonToken.IsMissing =>
                    type.SemicolonToken.SpanStart,
                DelegateDeclarationSyntax @delegate => @delegate.SemicolonToken.SpanStart,
                _ => declaration.Span.End,
            };
            var start = declaration.GetFirstToken().SpanStart;
            var header = declaration.SyntaxTree.GetText().ToString(new Microsoft.CodeAnalysis.Text.TextSpan(start, end - start));
            header = Regex.Replace(header, @"\bpartial\s+", string.Empty, RegexOptions.CultureInvariant);
            return NormalizeDeclaration(header);
        }

        return $"{FormatAccessibility(symbol.DeclaredAccessibility)} {symbol.TypeKind.ToString().ToLowerInvariant()} {symbol.Name}";
    }

    public static string FormatMemberDeclaration(ISymbol symbol)
    {
        var declaration = symbol.ToDisplayString(MemberDeclarationFormat);
        if (symbol is IEventSymbol @event)
        {
            var type = FormatReference(@event.Type);
            var typeIndex = declaration.IndexOf(type, StringComparison.Ordinal);
            if (typeIndex >= 0)
            {
                declaration = declaration.Insert(typeIndex, "event ");
            }
        }

        return declaration;
    }

    public static string FormatExtensionMemberDeclaration(ISymbol symbol, INamedTypeSymbol extension)
    {
        var syntax = extension.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<ExtensionBlockDeclarationSyntax>()
            .FirstOrDefault();
        if (syntax is null)
        {
            return FormatMemberDeclaration(symbol);
        }

        var start = syntax.GetFirstToken().SpanStart;
        var end = syntax.OpenBraceToken.SpanStart;
        var header = syntax.SyntaxTree.GetText()
            .ToString(new Microsoft.CodeAnalysis.Text.TextSpan(start, end - start));
        header = NormalizeDeclaration(header);
        return $"{header}{Environment.NewLine}{{{Environment.NewLine}    {FormatMemberDeclaration(symbol)}{Environment.NewLine}}}";
    }

    public static string FormatReference(ITypeSymbol symbol)
    {
        return symbol.ToDisplayString(ReferenceFormat);
    }

    public static ImmutableArray<SymbolDisplayPart> FormatReferenceParts(ITypeSymbol symbol)
    {
        return symbol.ToDisplayParts(ReferenceFormat);
    }

    public static string? FormatKeywordConstraints(ITypeParameterSymbol parameter)
    {
        var constraints = new List<string>();

        if (parameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("unmanaged");
        }
        else if (parameter.HasValueTypeConstraint)
        {
            constraints.Add("struct");
        }
        else if (parameter.HasReferenceTypeConstraint)
        {
            constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                ? "class?"
                : "class");
        }
        else if (parameter.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }

        if (parameter.HasConstructorConstraint)
        {
            constraints.Add("new()");
        }

        return constraints.Count == 0 ? null : string.Join(", ", constraints);
    }

    public static string FormatMemberName(ISymbol symbol)
    {
        var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        return syntax switch
        {
            OperatorDeclarationSyntax @operator => $"operator {@operator.OperatorToken.Text}",
            ConversionOperatorDeclarationSyntax conversion =>
                $"{conversion.ImplicitOrExplicitKeyword.Text} operator {conversion.Type}",
            _ => symbol.Name,
        };
    }

    private static string FormatAccessibility(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => string.Empty,
    };

    private static string NormalizeDeclaration(string declaration)
    {
        return Regex.Replace(declaration, @"\s+", " ", RegexOptions.CultureInvariant)
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal)
            .Replace(" ,", ",", StringComparison.Ordinal)
            .Trim();
    }
}
