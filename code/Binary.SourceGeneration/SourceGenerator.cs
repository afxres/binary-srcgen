namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
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
        var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var target = new StringBuilder(fullName);
        if (fullName.StartsWith(GlobalPrefix))
            _ = target.Remove(0, GlobalPrefix.Length);
        var invalidChars = new[] { '<', '>' };
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

    private SymbolMemberInfo? GetPublicFiledOrProperty(ISymbol symbol)
    {
        if (symbol.DeclaredAccessibility is Accessibility.Public)
        {
            switch (symbol)
            {
                case IFieldSymbol fieldSymbol:
                    return new SymbolMemberInfo(SymbolMemberType.Field, fieldSymbol.Name, fieldSymbol.IsReadOnly);
                case IPropertySymbol propertySymbol:
                    return new SymbolMemberInfo(SymbolMemberType.Property, propertySymbol.Name, propertySymbol.IsReadOnly);
            }
        }
        return null;
    }

    private void GetTupleObjectConverter(INamedTypeSymbol typeSymbol, SourceProductionContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        var members = typeSymbol.GetMembers();
        var publicMembers = members.Select(GetPublicFiledOrProperty).OfType<SymbolMemberInfo>().ToList();

        var builder = new StringBuilder();
        var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var typeFullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        _ = builder.AppendLine($"namespace {this.contextTypeNamespace};");
        _ = builder.AppendLine();
        _ = builder.AppendLine($"partial class {this.contextTypeName}");
        _ = builder.AppendLine("{");
        _ = builder.AppendLine($"    public class {typeName}Converter : {ConverterTypeName}<{typeFullName}>");
        _ = builder.AppendLine("    {");
        _ = builder.AppendLine($"        public override void Encode(ref Mikodev.Binary.Allocator allocator, {typeFullName} item) => throw new System.NotImplementedException();");
        _ = builder.AppendLine();
        _ = builder.AppendLine($"        public override {typeFullName} Decode(in System.ReadOnlySpan<byte> span) => throw new System.NotImplementedException();");
        _ = builder.AppendLine("    }");
        _ = builder.AppendLine("}");
        var generated = builder.ToString();
        var fileName = GetSafeOutputFileName(typeSymbol);
        context.AddSource($"{fileName}.g.cs", generated);
    }
}
