namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

[Generator]
public sealed class SourceGenerator : IIncrementalGenerator
{
    private const string TupleObjectAttributeTypeName = "Mikodev.Binary.Attributes.TupleObjectAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            TupleObjectAttributeTypeName,
            (node, _) => node is TypeDeclarationSyntax,
            (context, _) => (TypeDeclarationSyntax)context.TargetNode);
        var provider = context.CompilationProvider.Combine(declarations.Collect());
        context.RegisterSourceOutput(provider, (context, source) => Generate(source.Left, source.Right, context));
    }

    private static void Generate(Compilation compilation, ImmutableArray<TypeDeclarationSyntax> nodes, SourceProductionContext context)
    {
        if (nodes.IsDefaultOrEmpty)
            return;
        var tupleObjectSymbol = compilation.GetTypeByMetadataName(TupleObjectAttributeTypeName);
        foreach (var node in nodes)
        {
            var syntaxTree = node.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var typeSymbol = semanticModel.GetDeclaredSymbol(node);
            var attributes = typeSymbol.GetAttributes();
            if (attributes.Any(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, tupleObjectSymbol)))
            {
                var builder = new StringBuilder();
                var @namespace = typeSymbol.ContainingNamespace;
                var typeFullNameBuilder = new StringBuilder(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                var invalidChars = new[] { ':', '<', '>' };
                foreach (var @char in invalidChars)
                    _ = typeFullNameBuilder.Replace(@char, '_');
                var typeFullName = typeFullNameBuilder.ToString();
                if (@namespace.IsGlobalNamespace is false)
                    _ = builder.AppendLine($"namespace {@namespace};");
                context.AddSource($"{typeFullName}.Converter.g.cs", builder.ToString());
            }
        }
    }
}
