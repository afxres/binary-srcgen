namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using System.Linq;
using System.Text;

#nullable enable

public static class StaticExtensions
{
    public const string SourceGeneratorContextAttribute =
        """
        namespace Mikodev.Binary.Attributes;

        using System;

        [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
        internal sealed class SourceGeneratorContextAttribute : Attribute { }
        
        """;

    public const string SourceGeneratorIncludeAttribute =
        """
        namespace Mikodev.Binary.Attributes;

        using System;

        [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
        internal sealed class SourceGeneratorIncludeAttribute<T> : Attribute { }
        
        """;

    public const string SourceGeneratorContextAttributeTypeName = "Mikodev.Binary.Attributes.SourceGeneratorContextAttribute";

    public const string SourceGeneratorIncludeAttributeTypeName = "Mikodev.Binary.Attributes.SourceGeneratorIncludeAttribute`1";

    public const string TupleObjectAttributeTypeName = "Mikodev.Binary.Attributes.TupleObjectAttribute";

    public const string TupleKeyAttributeTypeName = "Mikodev.Binary.Attributes.TupleKeyAttribute";

    public const string AllocatorTypeName = "Mikodev.Binary.Allocator";

    public const string ConverterTypeName = "Mikodev.Binary.Converter";

    public const string IConverterTypeName = "Mikodev.Binary.IConverter";

    public const string IConverterCreatorTypeName = "Mikodev.Binary.IConverterCreator";

    public const string IGeneratorContextTypeName = "Mikodev.Binary.IGeneratorContext";

    public const string DiagnosticCategory = "SourceGeneration";

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

    public static DiagnosticDescriptor IncludeTypeDuplicated { get; } = new DiagnosticDescriptor(
        id: "BINSRCGEN03",
        title: "Include Type Duplicated.",
        messageFormat: "Please remove duplicated 'SourceGeneratorIncludeAttribute' for type '{0}'.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static SymbolDisplayFormat FullyQualifiedFormatNoSpecialTypes { get; } =
        new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

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

    public static Location GetLocationOrNone(ISymbol symbol)
    {
        if (symbol.Locations.Length is 0)
            return Location.None;
        return symbol.Locations.First();
    }

    public static Location GetLocationOrNone(AttributeData attribute)
    {
        var reference = attribute.ApplicationSyntaxReference;
        if (reference is not null)
            return Location.Create(reference.SyntaxTree, reference.Span);
        return Location.None;
    }
}
