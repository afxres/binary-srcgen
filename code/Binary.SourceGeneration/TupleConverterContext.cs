namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#nullable enable

public class TupleConverterContext
{
    private readonly INamedTypeSymbol namedTypeSymbol;

    private readonly SourceProductionContext sourceProductionContext;

    private readonly SourceGeneratorContext sourceGeneratorContext;

    private readonly SortedDictionary<string, string> typeAliases = new SortedDictionary<string, string>(StringComparer.Ordinal);

    private int typeAliasIndex = 0;

    private List<SymbolMemberInfo> members;

    private string typeAlias;

    private string outputConverterName;

    private string outputConverterFileNamePrefix;

    private INamedTypeSymbol? tupleKeyAttributeSymbol;

    private string GetTypeAlias(ITypeSymbol type)
    {
        var typeFullName = type.ToDisplayString(StaticExtensions.FullyQualifiedFormatNoSpecialTypes);
        if (this.typeAliases.TryGetValue(typeFullName, out var result))
            return result;
        var typeAlias = SymbolEqualityComparer.Default.Equals(type, this.namedTypeSymbol)
            ? $"_TSelf"
            : $"_T{this.typeAliasIndex++}";
        this.typeAliases.Add(typeFullName, typeAlias);
        return typeAlias;
    }

    private void ThrowIfCancelled()
    {
        this.sourceProductionContext.CancellationToken.ThrowIfCancellationRequested();
    }

    private SymbolMemberInfo? GetPublicFiledOrProperty(ISymbol symbol)
    {
        if (symbol.DeclaredAccessibility is not Accessibility.Public)
            return null;
        var attributes = symbol.GetAttributes();
        var attribute = attributes.FirstOrDefault(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, this.tupleKeyAttributeSymbol));
        if (attribute is null || attribute.ConstructorArguments.FirstOrDefault().Value is not int index)
            return null;
        if (symbol is IFieldSymbol fieldSymbol)
            return new SymbolMemberInfo(SymbolMemberType.Field, fieldSymbol.Name, fieldSymbol.IsReadOnly, fieldSymbol.Type, index);
        if (symbol is IPropertySymbol propertySymbol)
            return new SymbolMemberInfo(SymbolMemberType.Property, propertySymbol.Name, propertySymbol.IsReadOnly, propertySymbol.Type, index);
        return null;
    }

    public TupleConverterContext(SourceGeneratorContext sourceGeneratorContext, INamedTypeSymbol namedTypeSymbol)
    {
        this.sourceGeneratorContext = sourceGeneratorContext ?? throw new ArgumentNullException(nameof(sourceGeneratorContext));
        this.namedTypeSymbol = namedTypeSymbol ?? throw new ArgumentNullException(nameof(namedTypeSymbol));
        this.sourceProductionContext = sourceGeneratorContext.SourceProductionContext;
    }

    public void Invoke()
    {
        ThrowIfCancelled();
        this.tupleKeyAttributeSymbol = this.sourceGeneratorContext.Compilation.GetTypeByMetadataName(StaticExtensions.TupleKeyAttributeTypeName);
        var members = this.namedTypeSymbol.GetMembers().Select(GetPublicFiledOrProperty).OfType<SymbolMemberInfo>().OrderBy(x => x.Index).ToList();
        foreach (var i in members)
            _ = GetTypeAlias(i.Type);
        this.members = members;
        this.typeAlias = GetTypeAlias(this.namedTypeSymbol);
        this.outputConverterName = $"{StaticExtensions.GetSafePartTypeName(this.namedTypeSymbol)}__Converter";
        this.outputConverterFileNamePrefix = $"{StaticExtensions.GetSafeOutputFileName(this.namedTypeSymbol)}Converter";
        AppendConverter();
        AppendConverterCreator();
    }

    private void AppendConstructor(StringBuilder builder)
    {
        var members = this.members;
        builder.AppendIndent(2, $"public {this.outputConverterName}(");
        for (var i = 0; i < members.Count; i++)
        {
            var last = (i == members.Count - 1);
            var tail = last ? ")" : ",";
            var member = members[i];
            builder.AppendIndent(3, $"{StaticExtensions.ConverterTypeName}<{GetTypeAlias(member.Type)}> _arg{member.Index}{tail}");
            ThrowIfCancelled();
        }
        builder.AppendIndent(2, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            builder.AppendIndent(3, $"this._cvt{i} = _arg{i};");
            ThrowIfCancelled();
        }
        builder.AppendIndent(2, $"}}");
    }

    private void AppendEncodeMethod(StringBuilder builder, bool auto)
    {
        var members = this.members;
        var methodName = auto ? "EncodeAuto" : "Encode";
        builder.AppendIndent(2, $"public override void {methodName}(ref {StaticExtensions.AllocatorTypeName} allocator, {this.typeAlias} item)");
        builder.AppendIndent(2, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            var last = (i == members.Count - 1);
            var member = members[i];
            var method = (auto || last is false) ? "EncodeAuto" : "Encode";
            builder.AppendIndent(3, $"this._cvt{i}.{method}(ref allocator, item.{member.Name});");
            ThrowIfCancelled();
        }
        builder.AppendIndent(2, $"}}");
    }

    private void AppendDecodeMethod(StringBuilder builder, bool auto)
    {
        var members = this.members;
        var modifier = auto ? "ref" : "in";
        var methodName = auto ? "DecodeAuto" : "Decode";
        builder.AppendIndent(2, $"public override {this.typeAlias} {methodName}({modifier} System.ReadOnlySpan<byte> span)");
        builder.AppendIndent(2, $"{{");
        if (auto is false)
            builder.AppendIndent(3, $"var body = span;");
        var bufferName = auto ? "span" : "body";
        for (var i = 0; i < members.Count; i++)
        {
            var last = (i == members.Count - 1);
            var method = (auto || last is false) ? "DecodeAuto" : "Decode";
            var keyword = (auto is false && last) ? "in" : "ref";
            builder.AppendIndent(3, $"var _var{i} = this._cvt{i}.{method}({keyword} {bufferName});");
            ThrowIfCancelled();
        }
        builder.AppendIndent(3, $"var result = new {this.typeAlias}()");
        builder.AppendIndent(3, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            builder.AppendIndent(4, $"{member.Name} = _var{i},");
            ThrowIfCancelled();
        }
        builder.AppendIndent(3, $"}};");
        builder.AppendIndent(3, $"return result;");
        builder.AppendIndent(2, $"}}");
    }

    private void AppendConverter()
    {
        var builder = new StringBuilder();
        var members = this.members;
        builder.AppendIndent(0, $"namespace {this.sourceGeneratorContext.Namespace};");
        builder.AppendIndent(0);
        foreach (var i in this.typeAliases)
            builder.AppendIndent(0, $"using {i.Value} = {i.Key};");
        builder.AppendIndent(0);

        builder.AppendIndent(0, $"partial class {this.sourceGeneratorContext.Name}");
        builder.AppendIndent(0, $"{{");
        builder.AppendIndent(1, $"public class {this.outputConverterName} : {StaticExtensions.ConverterTypeName}<{this.typeAlias}>");
        builder.AppendIndent(1, $"{{");
        foreach (var i in members)
        {
            builder.AppendIndent(2, $"private readonly {StaticExtensions.ConverterTypeName}<{GetTypeAlias(i.Type)}> _cvt{i.Index};");
            builder.AppendIndent(2);
            ThrowIfCancelled();
        }

        AppendConstructor(builder);
        builder.AppendIndent(2);
        AppendEncodeMethod(builder, auto: false);
        builder.AppendIndent(2);
        AppendEncodeMethod(builder, auto: true);
        builder.AppendIndent(2);
        AppendDecodeMethod(builder, auto: false);
        builder.AppendIndent(2);
        AppendDecodeMethod(builder, auto: true);

        builder.AppendIndent(1, $"}}");
        builder.AppendIndent(0, $"}}");
        var outputCode = builder.ToString();
        var outputFileName = $"{this.outputConverterFileNamePrefix}.g.cs";
        this.sourceProductionContext.AddSource(outputFileName, outputCode);
    }

    private void AppendConverterCreator()
    {
        var builder = new StringBuilder();
        var members = this.members;
        builder.AppendIndent(0, $"namespace {this.sourceGeneratorContext.Namespace};");
        builder.AppendIndent(0);
        foreach (var i in this.typeAliases)
            builder.AppendIndent(0, $"using {i.Value} = {i.Key};");
        builder.AppendIndent(0);

        var outputConverterCreatorName = $"{this.outputConverterName}Creator";
        builder.AppendIndent(0, $"partial class {this.sourceGeneratorContext.Name}");
        builder.AppendIndent(0, $"{{");
        builder.AppendIndent(1, $"public class {outputConverterCreatorName} : {StaticExtensions.IConverterCreatorTypeName}");
        builder.AppendIndent(1, $"{{");

        builder.AppendIndent(2, $"public {StaticExtensions.IConverterTypeName} GetConverter({StaticExtensions.IGeneratorContextTypeName} context, System.Type type)");
        builder.AppendIndent(2, $"{{");
        builder.AppendIndent(3, $"if (type != typeof({this.typeAlias}))");
        builder.AppendIndent(4, $"return null;");
        foreach (var i in members)
        {
            var alias = GetTypeAlias(i.Type);
            builder.AppendIndent(3, $"var _cvt{i.Index} = ({StaticExtensions.ConverterTypeName}<{alias}>)context.GetConverter(typeof({alias}));");
            ThrowIfCancelled();
        }

        builder.AppendIndent(3, $"var converter = new {this.outputConverterName}(");
        for (var i = 0; i < members.Count; i++)
        {
            var last = (i == members.Count - 1);
            var tail = last ? ");" : ",";
            builder.AppendIndent(4, $"_cvt{i}{tail}");
        }
        builder.AppendIndent(3, $"return ({StaticExtensions.IConverterTypeName})converter;");
        builder.AppendIndent(2, $"}}");

        builder.AppendIndent(1, $"}}");
        builder.AppendIndent(0, $"}}");
        var outputCode = builder.ToString();
        var outputFileName = $"{this.outputConverterFileNamePrefix}Creator.g.cs";
        this.sourceProductionContext.AddSource(outputFileName, outputCode);
        this.sourceGeneratorContext.AddConverterCreator(outputConverterCreatorName);
    }
}
