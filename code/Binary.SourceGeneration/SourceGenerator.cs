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

    private static DiagnosticDescriptor ContextTypeMustBePartial { get; } = new DiagnosticDescriptor(
        id: "BINSRCGEN01",
        title: "Context Classes Must Be Partial!",
        messageFormat: "Require 'partial' keyword for source generation context type '{0}'.",
        category: "Mikodev.Binary.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private string? ContextTypeName;

    private string? ContextNamespace;

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
        this.ContextTypeName = null;
        this.ContextNamespace = null;
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
            ContextTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            ContextNamespace = @namespace.IsGlobalNamespace ? string.Empty : @namespace.ToDisplayString();
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
        if (ContextTypeName is null)
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
                var builder = new StringBuilder();
                var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                var typeFullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (string.IsNullOrEmpty(ContextNamespace) is false)
                    _ = builder.AppendLine($"namespace {ContextNamespace};");
                _ = builder.AppendLine();
                _ = builder.AppendLine($"partial class {ContextTypeName}");
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
    }
}
