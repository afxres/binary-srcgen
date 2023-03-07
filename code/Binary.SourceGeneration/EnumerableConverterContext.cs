namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using System.Linq;
using System.Text;

#nullable enable

public class EnumerableConverterContext
{
    private readonly INamedTypeSymbol namedTypeSymbol;

    private readonly SourceProductionContext productionContext;

    private readonly SourceGeneratorContext generatorContext;

    private readonly SymbolTypeAliases typeAliases;

    private readonly string typeAlias;

    private readonly string elementAlias;

    private readonly string outputConverterName;

    private void ThrowIfCancelled()
    {
        this.productionContext.CancellationToken.ThrowIfCancellationRequested();
    }

    private EnumerableConverterContext(SourceGeneratorContext context, INamedTypeSymbol symbol, ITypeSymbol elementTypeSymbol)
    {
        var typeAliases = new SymbolTypeAliases(symbol);
        typeAliases.Add(elementTypeSymbol);
        var targetName = StaticExtensions.GetSafeTargetTypeName(symbol);
        this.namedTypeSymbol = symbol;
        this.generatorContext = context;
        this.productionContext = context.SourceProductionContext;
        this.typeAlias = typeAliases.GetAlias(symbol);
        this.elementAlias = typeAliases.GetAlias(elementTypeSymbol);
        this.typeAliases = typeAliases;
        this.outputConverterName = $"{targetName}_Converter";
    }

    private void AppendConstructor(StringBuilder builder)
    {
        builder.AppendIndent(2, $"public {this.outputConverterName}(");
        builder.AppendIndent(3, $"{StaticExtensions.ConverterTypeName}<{this.elementAlias}> arg0)");
        builder.AppendIndent(2, $"{{");
        builder.AppendIndent(3, $"this.cvt0 = arg0;");
        builder.AppendIndent(2, $"}}");
    }

    private void AppendEncodeMethod(StringBuilder builder)
    {
        builder.AppendIndent(2, $"public override void Encode(ref {StaticExtensions.AllocatorTypeName} allocator, {this.typeAlias} item)");
        builder.AppendIndent(2, $"{{");
        if (this.namedTypeSymbol.IsValueType is false)
        {
            builder.AppendIndent(3, $"if (item is null)");
            builder.AppendIndent(4, $"return;");
            ThrowIfCancelled();
        }
        builder.AppendIndent(3, $"var cvt0 = this.cvt0;");
        builder.AppendIndent(3, $"foreach (var i in item)");
        builder.AppendIndent(4, $"cvt0.EncodeAuto(ref allocator, i);");
        builder.AppendIndent(3, $"return;");
        builder.AppendIndent(2, $"}}");
    }

    private void AppendDecodeMethod(StringBuilder builder)
    {
        builder.AppendIndent(2, $"public override {this.typeAlias} Decode(in System.ReadOnlySpan<byte> span) => throw new System.NotImplementedException();");
    }

    private void AppendConverter()
    {
        var builder = new StringBuilder();
        builder.AppendIndent(0, $"namespace {this.generatorContext.Namespace};");
        builder.AppendIndent(0);
        this.typeAliases.AppendAliases(builder);
        builder.AppendIndent(0);

        builder.AppendIndent(0, $"partial class {this.generatorContext.Name}");
        builder.AppendIndent(0, $"{{");
        builder.AppendIndent(1, $"private sealed class {this.outputConverterName} : {StaticExtensions.ConverterTypeName}<{this.typeAlias}>");
        builder.AppendIndent(1, $"{{");

        builder.AppendIndent(2, $"private readonly {StaticExtensions.ConverterTypeName}<{this.elementAlias}> cvt0;");
        builder.AppendIndent(2);

        AppendConstructor(builder);
        builder.AppendIndent(2);
        AppendEncodeMethod(builder);
        builder.AppendIndent(2);
        AppendDecodeMethod(builder);

        builder.AppendIndent(1, $"}}");
        builder.AppendIndent(0, $"}}");
        var outputCode = builder.ToString();
        var outputFileName = $"{this.outputConverterName}.g.cs";
        this.productionContext.AddSource(outputFileName, outputCode);
    }

    private void AppendConverterCreator()
    {
        var builder = new StringBuilder();
        builder.AppendIndent(0, $"namespace {this.generatorContext.Namespace};");
        builder.AppendIndent(0);
        this.typeAliases.AppendAliases(builder);
        builder.AppendIndent(0);

        var outputConverterCreatorName = $"{this.outputConverterName}Creator";
        builder.AppendIndent(0, $"partial class {this.generatorContext.Name}");
        builder.AppendIndent(0, $"{{");
        builder.AppendIndent(1, $"private sealed class {outputConverterCreatorName} : {StaticExtensions.IConverterCreatorTypeName}");
        builder.AppendIndent(1, $"{{");

        builder.AppendIndent(2, $"public {StaticExtensions.IConverterTypeName} GetConverter({StaticExtensions.IGeneratorContextTypeName} context, System.Type type)");
        builder.AppendIndent(2, $"{{");
        builder.AppendIndent(3, $"if (type != typeof({this.typeAlias}))");
        builder.AppendIndent(4, $"return null;");


        builder.AppendIndent(3, $"var cvt0 = ({StaticExtensions.ConverterTypeName}<{this.elementAlias}>)context.GetConverter(typeof({this.elementAlias}));");
        ThrowIfCancelled();

        builder.AppendIndent(3, $"var converter = new {this.outputConverterName}(cvt0);");
        builder.AppendIndent(3, $"return ({StaticExtensions.IConverterTypeName})converter;");
        builder.AppendIndent(2, $"}}");

        builder.AppendIndent(1, $"}}");
        builder.AppendIndent(0, $"}}");
        var outputCode = builder.ToString();
        var outputFileName = $"{this.outputConverterName}Creator.g.cs";
        this.productionContext.AddSource(outputFileName, outputCode);
        this.generatorContext.AddConverterCreator(outputConverterCreatorName);
    }

    public static bool Invoke(SourceGeneratorContext context, INamedTypeSymbol symbol)
    {
        var enumerable = context.Compilation.GetTypeByMetadataName(StaticExtensions.IEnumerableTypeName)?.ConstructUnboundGenericType();
        var interfaces = symbol.AllInterfaces.Where(x => x.IsGenericType && SymbolEqualityComparer.Default.Equals(enumerable, x.ConstructUnboundGenericType())).ToList();
        if (interfaces.Count is not 1)
            return false;
        var closure = new EnumerableConverterContext(context, symbol, interfaces.Single().TypeArguments.Single());
        closure.AppendConverter();
        closure.AppendConverterCreator();
        return true;
    }
}
