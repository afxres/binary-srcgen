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

    public const string IEnumerableTypeName = "System.Collections.Generic.IEnumerable`1";

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

    public static SymbolDisplayFormat FullyQualifiedFormat { get; } =
        new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static string GetSafeTargetTypeName(INamedTypeSymbol symbol)
    {
        const string GlobalPrefix = "global::";
        var fullName = GetFullName(symbol);
        var target = new StringBuilder(fullName);
        if (fullName.StartsWith(GlobalPrefix))
            _ = target.Remove(0, GlobalPrefix.Length);
        _ = target.Replace(GlobalPrefix, "g_");
        _ = target.Replace(".", "_");
        _ = target.Replace(",", "_c_");
        _ = target.Replace(" ", "_s_");
        _ = target.Replace("<", "_l_");
        _ = target.Replace(">", "_r_");
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

    public static string GetFullName(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol symbol || symbol.IsGenericType is false)
            return typeSymbol.ToDisplayString(FullyQualifiedFormat);
        var @namespace = symbol.ContainingNamespace.ToDisplayString(FullyQualifiedFormat);
        var builder = new StringBuilder(@namespace);
        _ = builder.Append(".");
        _ = builder.Append(typeSymbol.Name);
        _ = builder.Append("<");
        var arguments = symbol.TypeArguments;
        for (var i = 0; i < arguments.Length; i++)
        {
            _ = builder.Append(GetFullName(arguments[i]));
            if (i == arguments.Length - 1)
                break;
            _ = builder.Append(", ");
        }
        _ = builder.Append(">");
        var result = builder.ToString();
        return result;
    }
}
