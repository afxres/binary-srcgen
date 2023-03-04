namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;

#nullable enable

[Generator]
public sealed class SourceGenerator : IIncrementalGenerator
{
    private SourceGeneratorContext? sourceGeneratorContext;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(x => x.AddSource("__SourceGeneratorContextAttribute.g.cs", StaticExtensions.SourceGeneratorContextAttribute));
        Register(context, StaticExtensions.SourceGeneratorContextAttributeTypeName, GetOutputContextType);
        Register(context, StaticExtensions.TupleObjectAttributeTypeName, GetTupleObjectConverters);
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
        if (nodes.IsDefaultOrEmpty)
            return;
        var contextAttributeSymbol = compilation.GetTypeByMetadataName(StaticExtensions.SourceGeneratorContextAttributeTypeName);
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
                context.ReportDiagnostic(Diagnostic.Create(StaticExtensions.ContextTypeMustBePartial, location, new[] { typeSymbol.Name }));
                continue;
            }
            var @namespace = typeSymbol.ContainingNamespace;
            if (@namespace.IsGlobalNamespace)
            {
                var location = typeSymbol.Locations.Length is 0 ? Location.None : typeSymbol.Locations.First();
                context.ReportDiagnostic(Diagnostic.Create(StaticExtensions.ContextTypeMustHaveNamespace, location, new[] { typeSymbol.Name }));
                continue;
            }
            var contextTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var contextTypeNamespace = @namespace.ToDisplayString();
            var output =
                $$"""
                namespace {{contextTypeNamespace}};

                partial class {{contextTypeName}}
                {
                    private readonly System.Collections.Concurrent.ConcurrentDictionary<System.Type, Mikodev.Binary.IConverterCreator> creators = new();
                }

                """;
            var fileName = StaticExtensions.GetSafeOutputFileName(typeSymbol);
            context.AddSource($"{fileName}.g.cs", output);
            this.sourceGeneratorContext = new SourceGeneratorContext { Name = contextTypeName, Namespace = contextTypeNamespace };
        }
    }

    public void GetTupleObjectConverters(Compilation compilation, ImmutableArray<TypeDeclarationSyntax> nodes, SourceProductionContext context)
    {
        if (this.sourceGeneratorContext is null)
            return;
        if (nodes.IsDefaultOrEmpty)
            return;
        var tupleObjectSymbol = compilation.GetTypeByMetadataName(StaticExtensions.TupleObjectAttributeTypeName);
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
                new TupleConverterContext(this.sourceGeneratorContext, typeSymbol, context).Invoke();
            }
        }
    }
}
