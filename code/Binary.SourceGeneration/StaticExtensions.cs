namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

#nullable enable

public static class StaticExtensions
{
    public const string SourceGeneratorContextAttribute =
        """
        namespace Mikodev.Binary.Attributes;

        [System.AttributeUsage(System.AttributeTargets.All, Inherited = false, AllowMultiple = false)]
        internal sealed class SourceGeneratorContextAttribute : System.Attribute { }

        """;

    public const string SourceGeneratorContextAttributeTypeName = "Mikodev.Binary.Attributes.SourceGeneratorContextAttribute";

    public const string TupleObjectAttributeTypeName = "Mikodev.Binary.Attributes.TupleObjectAttribute";

    public const string AllocatorTypeName = "Mikodev.Binary.Allocator";

    public const string ConverterTypeName = "Mikodev.Binary.Converter";

    public const string DiagnosticCategory = "Mikodev.Binary.SourceGeneration";

    public static DiagnosticDescriptor ContextTypeMustBePartial { get; } = new DiagnosticDescriptor(
        id: "BINSRCGEN01",
        title: "Context Type Must Be Partial!",
        messageFormat: "Require 'partial' keyword for source generation context type '{0}'.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static DiagnosticDescriptor ContextTypeMustHaveNamespace { get; } = new DiagnosticDescriptor(
        id: "BINSRCGEN02",
        title: "Context Type Must Have Namespace!",
        messageFormat: "Require not global namespace for source generation context type '{0}'.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static string GetSafeOutputFileName(INamedTypeSymbol symbol)
    {
        const string GlobalPrefix = "global::";
        var fullName = symbol.ToDisplayString(FullyQualifiedFormatNoSpecialTypes);
        var target = new StringBuilder(fullName);
        if (fullName.StartsWith(GlobalPrefix))
            _ = target.Remove(0, GlobalPrefix.Length);
        var invalidChars = new[] { '<', '>' };
        foreach (var @char in invalidChars)
            _ = target.Replace(@char, '_');
        var result = target.ToString();
        return result;
    }

    public static string GetSafePartTypeName(INamedTypeSymbol symbol)
    {
        const string GlobalPrefix = "global::";
        var fullName = symbol.ToDisplayString(FullyQualifiedFormatNoSpecialTypes);
        var target = new StringBuilder(fullName);
        if (fullName.StartsWith(GlobalPrefix))
            _ = target.Remove(0, GlobalPrefix.Length);
        var invalidChars = new[] { '<', '>', '.' };
        foreach (var @char in invalidChars)
            _ = target.Replace(@char, '_');
        var result = target.ToString();
        return result;
    }

    public static SymbolMemberInfo? GetPublicFiledOrProperty(ISymbol symbol, StrongBox<int> index)
    {
        if (symbol.DeclaredAccessibility is Accessibility.Public)
        {
            switch (symbol)
            {
                case IFieldSymbol fieldSymbol:
                    return new SymbolMemberInfo(SymbolMemberType.Field, fieldSymbol.Name, fieldSymbol.IsReadOnly, fieldSymbol.Type, index.Value++);
                case IPropertySymbol propertySymbol:
                    return new SymbolMemberInfo(SymbolMemberType.Property, propertySymbol.Name, propertySymbol.IsReadOnly, propertySymbol.Type, index.Value++);
            }
        }
        return null;
    }

    public static SymbolDisplayFormat FullyQualifiedFormatNoSpecialTypes { get; } =
        new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);
}
