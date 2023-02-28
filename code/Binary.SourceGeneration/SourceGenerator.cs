namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

#nullable enable

[Generator]
public sealed class SourceGenerator : IIncrementalGenerator
{
    private const string SourceGenerationContextAttribute =
        """
        namespace Mikodev.Binary.Attributes;

        [System.AttributeUsage(System.AttributeTargets.All, Inherited = false, AllowMultiple = false)]
        internal sealed class SourceGenerationContextAttribute : System.Attribute { }

        """;

    private const string SourceGenerationContextAttributeTypeName = "Mikodev.Binary.Attributes.SourceGenerationContextAttribute";

    private const string TupleObjectAttributeTypeName = "Mikodev.Binary.Attributes.TupleObjectAttribute";

    private const string ConverterTypeName = "Mikodev.Binary.Converter";

    private const string DiagnosticCategory = "Mikodev.Binary.SourceGeneration";

    private static DiagnosticDescriptor ContextTypeMustBePartial { get; } = new DiagnosticDescriptor(
        id: "BINSRCGEN01",
        title: "Context Type Must Be Partial!",
        messageFormat: "Require 'partial' keyword for source generation context type '{0}'.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static DiagnosticDescriptor ContextTypeMustHaveNamespace { get; } = new DiagnosticDescriptor(
        id: "BINSRCGEN02",
        title: "Context Type Must Have Namespace!",
        messageFormat: "Require not global namespace for source generation context type '{0}'.",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private string? contextTypeName;

    private string? contextTypeNamespace;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(x => x.AddSource("__SourceGenerationContextAttribute.g.cs", SourceGenerationContextAttribute));
        Register(context, SourceGenerationContextAttributeTypeName, GetOutputContextType);
        Register(context, TupleObjectAttributeTypeName, GetTupleObjectConverters);
    }

    private void Register(IncrementalGeneratorInitializationContext context, string attributeName, Action<Compilation, ImmutableArray<TypeDeclarationSyntax>, SourceProductionContext> func)
    {
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeName,
            (node, _) => node is TypeDeclarationSyntax,
            (context, _) => (TypeDeclarationSyntax)context.TargetNode);
        var provider = context.CompilationProvider.Combine(declarations.Collect());
        context.RegisterSourceOutput(provider, (context, source) => func.Invoke(source.Left, source.Right, context));
    }

    private void GetOutputContextType(Compilation compilation, ImmutableArray<TypeDeclarationSyntax> nodes, SourceProductionContext context)
    {
        this.contextTypeName = null;
        this.contextTypeNamespace = null;
        if (nodes.IsDefaultOrEmpty)
            return;
        var contextAttributeSymbol = compilation.GetTypeByMetadataName(SourceGenerationContextAttributeTypeName);
        foreach (var node in nodes)
        {
            var syntaxTree = node.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var typeSymbol = semanticModel.GetDeclaredSymbol(node);
            if (typeSymbol is null)
                continue;
            var partial = node.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword));
            if (partial is false)
            {
                var location = typeSymbol.Locations.Length is 0 ? Location.None : typeSymbol.Locations.First();
                context.ReportDiagnostic(Diagnostic.Create(ContextTypeMustBePartial, location, new[] { typeSymbol.Name }));
                continue;
            }
            var @namespace = typeSymbol.ContainingNamespace;
            if (@namespace.IsGlobalNamespace)
            {
                var location = typeSymbol.Locations.Length is 0 ? Location.None : typeSymbol.Locations.First();
                context.ReportDiagnostic(Diagnostic.Create(ContextTypeMustHaveNamespace, location, new[] { typeSymbol.Name }));
                continue;
            }
            this.contextTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            this.contextTypeNamespace = @namespace.ToDisplayString();
            var output =
                $$"""
                namespace {{this.contextTypeNamespace}};

                partial class {{this.contextTypeName}}
                {
                    private readonly System.Collections.Concurrent.ConcurrentDictionary<System.Type, Mikodev.Binary.IConverterCreator> creators = new();
                }

                """;
            var fileName = GetSafeOutputFileName(typeSymbol);
            context.AddSource($"{fileName}.g.cs", output);
        }
    }

    private static string GetSafeOutputFileName(INamedTypeSymbol symbol)
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

    private static string GetSafePartTypeName(INamedTypeSymbol symbol)
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

    private void GetTupleObjectConverters(Compilation compilation, ImmutableArray<TypeDeclarationSyntax> nodes, SourceProductionContext context)
    {
        if (this.contextTypeName is null)
            return;
        if (nodes.IsDefaultOrEmpty)
            return;
        var tupleObjectSymbol = compilation.GetTypeByMetadataName(TupleObjectAttributeTypeName);
        foreach (var node in nodes)
        {
            var syntaxTree = node.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var typeSymbol = semanticModel.GetDeclaredSymbol(node);
            if (typeSymbol is null)
                continue;
            var attributes = typeSymbol.GetAttributes();
            if (attributes.Any(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, tupleObjectSymbol)))
            {
                GetTupleObjectConverter(typeSymbol, context);
            }
        }
    }

    private SymbolMemberInfo? GetPublicFiledOrProperty(ISymbol symbol, StrongBox<int> index)
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

    private void GetTupleObjectConverter(INamedTypeSymbol typeSymbol, SourceProductionContext context)
    {
        var cancellation = context.CancellationToken;
        cancellation.ThrowIfCancellationRequested();

        var typeAliasIndex = 0;
        var typeAliases = new Dictionary<string, string>();

        string GetTypeAlias(ITypeSymbol type)
        {
            var typeFullName = type.ToDisplayString(FullyQualifiedFormatNoSpecialTypes);
            if (typeAliases.TryGetValue(typeFullName, out var result))
                return result;
            var typeAlias = $"_T_{typeAliasIndex++}";
            typeAliases.Add(typeFullName, typeAlias);
            return typeAlias;
        }

        var sharedIndex = new StrongBox<int> { Value = 0 };
        var members = typeSymbol.GetMembers().Select(x => GetPublicFiledOrProperty(x, sharedIndex)).OfType<SymbolMemberInfo>().OrderBy(x => x.Index).ToList();
        foreach (var i in members)
            _ = GetTypeAlias(i.Type);
        var typeAlias = GetTypeAlias(typeSymbol);
        var builder = new StringBuilder();
        var outputConverterName = $"{GetSafePartTypeName(typeSymbol)}__Converter";
        builder.AppendIndent(0, $"namespace {this.contextTypeNamespace};");
        builder.AppendIndent(0);
        foreach (var i in typeAliases)
            builder.AppendIndent(0, $"using {i.Value} = {i.Key};");
        builder.AppendIndent(0);

        builder.AppendIndent(0, $"partial class {this.contextTypeName}");
        builder.AppendIndent(0, $"{{");
        builder.AppendIndent(1, $"public class {outputConverterName} : {ConverterTypeName}<{typeAlias}>");
        builder.AppendIndent(1, $"{{");
        foreach (var i in members)
        {
            builder.AppendIndent(2, $"private readonly Mikodev.Binary.Converter<{GetTypeAlias(i.Type)}> _cvt_{i.Index};");
            builder.AppendIndent(2);
            cancellation.ThrowIfCancellationRequested();
        }

        builder.AppendIndent(2, $"public {outputConverterName}(");
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var tail = (i == members.Count - 1) ? ")" : ",";
            builder.AppendIndent(3, $"Mikodev.Binary.Converter<{GetTypeAlias(member.Type)}> _arg_{member.Index}{tail}");
            cancellation.ThrowIfCancellationRequested();
        }
        builder.AppendIndent(2, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            builder.AppendIndent(3, $"this._cvt_{i} = _arg_{i};");
            cancellation.ThrowIfCancellationRequested();
        }
        builder.AppendIndent(2, $"}}");
        builder.AppendIndent(2);

        builder.AppendIndent(2, $"public override void Encode(ref Mikodev.Binary.Allocator allocator, {typeAlias} item)");
        builder.AppendIndent(2, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var method = (i == members.Count - 1) ? "Encode" : "EncodeAuto";
            builder.AppendIndent(3, $"this._cvt_{i}.{method}(ref allocator, item.{member.Name});");
            cancellation.ThrowIfCancellationRequested();
        }
        builder.AppendIndent(2, $"}}");
        builder.AppendIndent(2);

        builder.AppendIndent(2, $"public override void EncodeAuto(ref Mikodev.Binary.Allocator allocator, {typeAlias} item)");
        builder.AppendIndent(2, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            builder.AppendIndent(3, $"this._cvt_{i}.EncodeAuto(ref allocator, item.{member.Name});");
            cancellation.ThrowIfCancellationRequested();
        }
        builder.AppendIndent(2, $"}}");
        builder.AppendIndent(2);

        builder.AppendIndent(2, $"public override {typeAlias} Decode(in System.ReadOnlySpan<byte> span)");
        builder.AppendIndent(2, $"{{");
        builder.AppendIndent(3, $"var body = span;");
        for (var i = 0; i < members.Count; i++)
        {
            var last = (i == members.Count - 1);
            var method = last ? "Decode" : "DecodeAuto";
            var keyword = last ? "in" : "ref";
            builder.AppendIndent(3, $"var _var_{i} = this._cvt_{i}.{method}({keyword} body);");
            cancellation.ThrowIfCancellationRequested();
        }
        builder.AppendIndent(3, $"var result = new {typeAlias}()");
        builder.AppendIndent(3, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            builder.AppendIndent(4, $"{member.Name} = _var_{i},");
            cancellation.ThrowIfCancellationRequested();
        }
        builder.AppendIndent(3, $"}};");
        builder.AppendIndent(3, $"return result;");
        builder.AppendIndent(2, $"}}");
        builder.AppendIndent(2);

        builder.AppendIndent(2, $"public override {typeAlias} DecodeAuto(ref System.ReadOnlySpan<byte> span)");
        builder.AppendIndent(2, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            builder.AppendIndent(3, $"var _var_{i} = this._cvt_{i}.DecodeAuto(ref span);");
            cancellation.ThrowIfCancellationRequested();
        }
        builder.AppendIndent(3, $"var result = new {typeAlias}()");
        builder.AppendIndent(3, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            builder.AppendIndent(4, $"{member.Name} = _var_{i},");
            cancellation.ThrowIfCancellationRequested();
        }
        builder.AppendIndent(3, $"}};");
        builder.AppendIndent(3, $"return result;");
        builder.AppendIndent(2, $"}}");

        builder.AppendIndent(1, $"}}");
        builder.AppendIndent(0, $"}}");
        var outputCode = builder.ToString();
        var outputFileName = GetSafeOutputFileName(typeSymbol);
        context.AddSource($"{outputFileName}.g.cs", outputCode);
    }
}
