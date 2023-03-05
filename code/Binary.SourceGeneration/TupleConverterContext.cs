namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

#nullable enable

public class TupleConverterContext
{
    private static readonly ImmutableArray<string> SystemTupleMemberNames = ImmutableArray.Create(new[] { "Item1", "Item2", "Item3", "Item4", "Item5", "Item6", "Item7", "Rest" });

    private readonly INamedTypeSymbol namedTypeSymbol;

    private readonly SourceProductionContext productionContext;

    private readonly SourceGeneratorContext generatorContext;

    private readonly SortedDictionary<string, string> typeAliases = new SortedDictionary<string, string>(StringComparer.Ordinal);

    private int typeAliasIndex = 0;

    private readonly List<SymbolMemberInfo> members;

    private readonly string typeAlias;

    private readonly string outputConverterName;

    private string GetTypeAlias(ITypeSymbol type)
    {
        var typeFullName = StaticExtensions.GetFullName(type);
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
        this.productionContext.CancellationToken.ThrowIfCancellationRequested();
    }

    private SymbolMemberInfo? GetMember(ISymbol member)
    {
        if (member.DeclaredAccessibility is not Accessibility.Public)
            return null;
        var attributes = member.GetAttributes();
        var attribute = attributes.FirstOrDefault(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, this.generatorContext.TupleKeyAttributeTypeSymbol));
        if (attribute is null || attribute.ConstructorArguments.FirstOrDefault().Value is not int index)
            return null;
        if (member is IFieldSymbol fieldSymbol)
            return new SymbolMemberInfo(SymbolMemberType.Field, fieldSymbol.Name, fieldSymbol.IsReadOnly, fieldSymbol.Type, index);
        if (member is IPropertySymbol propertySymbol)
            return new SymbolMemberInfo(SymbolMemberType.Property, propertySymbol.Name, propertySymbol.IsReadOnly, propertySymbol.Type, index);
        return null;
    }

    private List<SymbolMemberInfo> GetCustomTupleMembers(INamedTypeSymbol symbol)
    {
        return symbol.GetMembers().Select(GetMember).OfType<SymbolMemberInfo>().OrderBy(x => x.Index).ToList();
    }

    private List<SymbolMemberInfo> GetSystemTupleMembers(INamedTypeSymbol symbol)
    {
        var members = symbol.GetMembers();
        var result = new List<SymbolMemberInfo>();
        foreach (var member in members)
        {
            var index = SystemTupleMemberNames.IndexOf(member.Name);
            if (index is -1)
                continue;
            if (member is IFieldSymbol fieldSymbol)
                result.Add(new SymbolMemberInfo(SymbolMemberType.Field, fieldSymbol.Name, fieldSymbol.IsReadOnly, fieldSymbol.Type, index));
            if (member is IPropertySymbol propertySymbol)
                result.Add(new SymbolMemberInfo(SymbolMemberType.Property, propertySymbol.Name, propertySymbol.IsReadOnly, propertySymbol.Type, index));
        }
        return result;
    }

    private TupleConverterContext(SourceGeneratorContext context, INamedTypeSymbol symbol, bool systemTuple)
    {
        this.generatorContext = context ?? throw new ArgumentNullException(nameof(context));
        this.namedTypeSymbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        this.productionContext = context.SourceProductionContext;
        var targetName = StaticExtensions.GetSafeTargetTypeName(this.namedTypeSymbol);
        var members = systemTuple ? GetSystemTupleMembers(symbol) : GetCustomTupleMembers(symbol);
        foreach (var i in members)
            _ = GetTypeAlias(i.Type);
        this.members = members;
        this.typeAlias = GetTypeAlias(this.namedTypeSymbol);
        this.outputConverterName = $"{targetName}_Converter";
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
        builder.AppendIndent(0, $"namespace {this.generatorContext.Namespace};");
        builder.AppendIndent(0);
        foreach (var i in this.typeAliases)
            builder.AppendIndent(0, $"using {i.Value} = {i.Key};");
        builder.AppendIndent(0);

        builder.AppendIndent(0, $"partial class {this.generatorContext.Name}");
        builder.AppendIndent(0, $"{{");
        builder.AppendIndent(1, $"private sealed class {this.outputConverterName} : {StaticExtensions.ConverterTypeName}<{this.typeAlias}>");
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
        var outputFileName = $"{this.outputConverterName}.g.cs";
        this.productionContext.AddSource(outputFileName, outputCode);
    }

    private void AppendConverterCreator()
    {
        var builder = new StringBuilder();
        var members = this.members;
        builder.AppendIndent(0, $"namespace {this.generatorContext.Namespace};");
        builder.AppendIndent(0);
        foreach (var i in this.typeAliases)
            builder.AppendIndent(0, $"using {i.Value} = {i.Key};");
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
        var outputFileName = $"{this.outputConverterName}Creator.g.cs";
        this.productionContext.AddSource(outputFileName, outputCode);
        this.generatorContext.AddConverterCreator(outputConverterCreatorName);
    }

    public static bool Invoke(SourceGeneratorContext context, INamedTypeSymbol symbol)
    {
        if (symbol.IsTupleType is false &&
            symbol.GetAttributes().Any(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, context.TupleObjectAttributeTypeSymbol)) is false)
            return false;
        var closure = new TupleConverterContext(context, symbol, symbol.IsTupleType);
        closure.AppendConverter();
        closure.AppendConverterCreator();
        return true;
    }
}
