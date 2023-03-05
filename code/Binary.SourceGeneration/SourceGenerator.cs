namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

#nullable enable

[Generator]
public sealed class SourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(x => x.AddSource("__SourceGeneratorContextAttribute.g.cs", StaticExtensions.SourceGeneratorContextAttribute));
        context.RegisterPostInitializationOutput(x => x.AddSource("__SourceGeneratorIncludeAttribute.g.cs", StaticExtensions.SourceGeneratorIncludeAttribute));

        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            StaticExtensions.SourceGeneratorContextAttributeTypeName,
            (node, _) => node is TypeDeclarationSyntax,
            (context, _) => (TypeDeclarationSyntax)context.TargetNode);
        var provider = context.CompilationProvider.Combine(declarations.Collect());
        context.RegisterSourceOutput(provider, (context, source) => Invoke(source.Left, source.Right, context));
    }

    private void Invoke(Compilation compilation, ImmutableArray<TypeDeclarationSyntax> nodes, SourceProductionContext context)
    {
        if (nodes.IsDefaultOrEmpty)
            return;
        var contextAttributeSymbol = compilation.GetTypeByMetadataName(StaticExtensions.SourceGeneratorContextAttributeTypeName);
        var includeAttributeSymbol = compilation.GetTypeByMetadataName(StaticExtensions.SourceGeneratorIncludeAttributeTypeName)?.ConstructUnboundGenericType();
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
                var location = StaticExtensions.GetLocationOrNone(typeSymbol);
                context.ReportDiagnostic(Diagnostic.Create(StaticExtensions.ContextTypeMustBePartial, location, new[] { typeSymbol.Name }));
                continue;
            }
            var @namespace = typeSymbol.ContainingNamespace;
            if (@namespace.IsGlobalNamespace)
            {
                var location = StaticExtensions.GetLocationOrNone(typeSymbol);
                context.ReportDiagnostic(Diagnostic.Create(StaticExtensions.ContextTypeMustHaveNamespace, location, new[] { typeSymbol.Name }));
                continue;
            }
            var contextTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var contextTypeNamespace = @namespace.ToDisplayString();

            var includedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var attributes = typeSymbol.GetAttributes();
            foreach (var i in attributes)
            {
                var attribute = i.AttributeClass;
                if (attribute is null || attribute.IsGenericType is false)
                    continue;
                var definitions = attribute.ConstructUnboundGenericType();
                if (SymbolEqualityComparer.Default.Equals(definitions, includeAttributeSymbol) is false)
                    continue;
                if (attribute.TypeArguments.Single() is not INamedTypeSymbol includedType)
                    continue;
                if (includedTypes.Add(includedType))
                    continue;
                var location = StaticExtensions.GetLocationOrNone(i);
                context.ReportDiagnostic(Diagnostic.Create(StaticExtensions.IncludeTypeDuplicated, location, new[] { includedType.Name }));
            }

            var sourceGeneratorContext = new SourceGeneratorContext(contextTypeName, contextTypeNamespace, compilation, context);
            var tupleObjectSymbol = compilation.GetTypeByMetadataName(StaticExtensions.TupleObjectAttributeTypeName);
            foreach (var type in includedTypes)
            {
                if (type.GetAttributes().Any(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, tupleObjectSymbol)))
                {
                    TupleConverterContext.Invoke(sourceGeneratorContext, type);
                }
            }

            var builder = new StringBuilder();
            const string CreatorsCollectionType = $"System.Collections.Generic.List<{StaticExtensions.IConverterCreatorTypeName}>";
            const string CreatorsCollectionInterfaceType = $"System.Collections.Generic.IEnumerable<{StaticExtensions.IConverterCreatorTypeName}>";
            builder.AppendIndent(0, $"namespace {contextTypeNamespace};");
            builder.AppendIndent(0);
            builder.AppendIndent(0, $"partial class {contextTypeName}");
            builder.AppendIndent(0, $"{{");
            builder.AppendIndent(1, $"public static {CreatorsCollectionInterfaceType} ConverterCreators {{ get; }} = new {CreatorsCollectionType}()");
            builder.AppendIndent(1, $"{{");
            foreach (var i in sourceGeneratorContext.ConverterCreators)
            {
                builder.AppendIndent(2, $"new {i}(),");
                context.CancellationToken.ThrowIfCancellationRequested();
            }
            builder.AppendIndent(1, $"}};");
            builder.AppendIndent(0, $"}}");
            builder.AppendIndent(0);
            var output = builder.ToString();
            var fileName = StaticExtensions.GetSafeOutputFileName(typeSymbol);
            context.AddSource($"{fileName}.g.cs", output);
        }
    }
}
