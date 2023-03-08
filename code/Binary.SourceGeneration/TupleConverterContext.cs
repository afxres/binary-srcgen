namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

#nullable enable

public class TupleConverterContext
{
    private static readonly ImmutableArray<string> SystemTupleMemberNames = ImmutableArray.Create(new[] { "Item1", "Item2", "Item3", "Item4", "Item5", "Item6", "Item7", "Rest" });

    private static ImmutableList<INamedTypeSymbol>? SystemTupleTypes;

    private readonly INamedTypeSymbol namedTypeSymbol;

    private readonly SourceProductionContext productionContext;

    private readonly SourceGeneratorContext generatorContext;

    private readonly SymbolTypeAliases typeAliases;

    private readonly ImmutableArray<SymbolMemberInfo> members;

    private readonly ImmutableArray<SymbolMemberInfo> constructorParameters;

    private readonly string typeAlias;

    private readonly string outputConverterName;

    private void ThrowIfCancelled()
    {
        this.productionContext.CancellationToken.ThrowIfCancellationRequested();
    }

    private static SymbolMemberInfo? GetCustomTupleMember(SourceGeneratorContext context, ISymbol member)
    {
        if (member.DeclaredAccessibility is not Accessibility.Public)
            return null;
        var attributes = member.GetAttributes();
        var attribute = attributes.FirstOrDefault(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, context.TupleKeyAttributeTypeSymbol));
        if (attribute is null || attribute.ConstructorArguments.FirstOrDefault().Value is not int index)
            return null;
        if (member is IFieldSymbol fieldSymbol)
            return new SymbolMemberInfo(SymbolMemberType.Field, fieldSymbol.Name, fieldSymbol.IsReadOnly, fieldSymbol.Type, index);
        if (member is IPropertySymbol propertySymbol)
            return new SymbolMemberInfo(SymbolMemberType.Property, propertySymbol.Name, propertySymbol.IsReadOnly, propertySymbol.Type, index);
        return null;
    }

    private static ImmutableArray<SymbolMemberInfo> GetCustomTupleMembers(SourceGeneratorContext context, INamedTypeSymbol symbol)
    {
        return symbol.GetMembers().Select(x => GetCustomTupleMember(context, x)).OfType<SymbolMemberInfo>().OrderBy(x => x.Index).ToImmutableArray();
    }

    private static ImmutableArray<SymbolMemberInfo> GetSystemTupleMembers(INamedTypeSymbol symbol)
    {
        var members = symbol.GetMembers();
        var result = ImmutableArray.CreateBuilder<SymbolMemberInfo>();
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
        return result.ToImmutable();
    }

    private static ImmutableArray<SymbolMemberInfo> GetConstructorParameters(SourceGeneratorContext context, INamedTypeSymbol symbol, ImmutableArray<SymbolMemberInfo> members)
    {
        static string Select(string? text) =>
            text?.ToUpperInvariant()
            ?? string.Empty;

        var constructors = symbol.InstanceConstructors;
        var hasDefaultConstructor = symbol.IsValueType || constructors.Any(x => x.Parameters.Length is 0);
        if (hasDefaultConstructor && members.All(x => x.IsReadOnly is false))
            return ImmutableArray.Create<SymbolMemberInfo>();

        var selector = new Func<SymbolMemberInfo, string>(x => Select(x.Name));
        if (members.Select(selector).Distinct().Count() != members.Length)
            return default;

        // select constructor with most parameters
        var dictionary = members.ToDictionary(selector);
        foreach (var i in constructors.OrderByDescending(x => x.Parameters.Length))
        {
            var parameters = i.Parameters;
            var result = parameters
                .Select(x => dictionary.TryGetValue(Select(x.Name), out var member) && SymbolEqualityComparer.Default.Equals(member.Type, x.Type) ? member : null)
                .OfType<SymbolMemberInfo>()
                .ToImmutableArray();
            if (result.Length is 0 || result.Length != parameters.Length)
                continue;
            var except = members.Except(result).ToImmutableArray();
            if (except.Any(x => x.IsReadOnly))
                continue;
            return result;
        }
        return default;
    }

    private TupleConverterContext(SourceGeneratorContext context, INamedTypeSymbol symbol, bool systemTuple)
    {
        var typeAliases = new SymbolTypeAliases(symbol);
        var targetName = StaticExtensions.GetSafeTargetTypeName(symbol);
        var members = systemTuple ? GetSystemTupleMembers(symbol) : GetCustomTupleMembers(context, symbol);
        foreach (var i in members)
            typeAliases.Add(i.Type);
        this.namedTypeSymbol = symbol;
        this.generatorContext = context;
        this.productionContext = context.SourceProductionContext;
        this.members = members;
        this.typeAlias = typeAliases.GetAlias(symbol);
        this.typeAliases = typeAliases;
        this.outputConverterName = $"{targetName}_Converter";
        this.constructorParameters = GetConstructorParameters(context, symbol, members);
    }

    private void AppendDecodeConstructor(StringBuilder builder, ImmutableArray<SymbolMemberInfo> constructorParameters)
    {
        var members = this.members;
        if (constructorParameters.Length is 0)
        {
            builder.AppendIndent(3, $"var result = new {this.typeAlias}()");
        }
        else
        {
            builder.AppendIndent(3, $"var result = new {this.typeAlias}(");
            for (var i = 0; i < constructorParameters.Length; i++)
            {
                var tail = (i == constructorParameters.Length - 1) ? ")" : ",";
                builder.AppendIndent(4, $"var{constructorParameters[i].Index}{tail}");
                ThrowIfCancelled();
            }
        }

        builder.AppendIndent(3, $"{{");
        foreach (var i in members)
        {
            if (constructorParameters.Contains(i))
                continue;
            builder.AppendIndent(4, $"{i.Name} = var{i.Index},");
            ThrowIfCancelled();
        }
        builder.AppendIndent(3, $"}};");
        builder.AppendIndent(3, $"return result;");
    }

    private void AppendConstructor(StringBuilder builder)
    {
        var members = this.members;
        builder.AppendIndent(2, $"public {this.outputConverterName}(");
        for (var i = 0; i < members.Length; i++)
        {
            var last = (i == members.Length - 1);
            var tail = last ? ")" : ",";
            var member = members[i];
            builder.AppendIndent(3, $"{StaticExtensions.ConverterTypeName}<{this.typeAliases.GetAlias(member.Type)}> arg{member.Index}{tail}");
            ThrowIfCancelled();
        }
        builder.AppendIndent(2, $"{{");
        for (var i = 0; i < members.Length; i++)
        {
            builder.AppendIndent(3, $"this.cvt{i} = arg{i};");
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
        if (this.namedTypeSymbol.IsValueType is false)
        {
            builder.AppendIndent(3, $"System.ArgumentNullException.ThrowIfNull(item);");
            ThrowIfCancelled();
        }

        for (var i = 0; i < members.Length; i++)
        {
            var last = (i == members.Length - 1);
            var member = members[i];
            var method = (auto || last is false) ? "EncodeAuto" : "Encode";
            builder.AppendIndent(3, $"this.cvt{i}.{method}(ref allocator, item.{member.Name});");
            ThrowIfCancelled();
        }
        builder.AppendIndent(2, $"}}");
    }

    private void AppendDecodeMethod(StringBuilder builder, bool auto)
    {
        var modifier = auto ? "ref" : "in";
        var methodName = auto ? "DecodeAuto" : "Decode";
        var constructorParameters = this.constructorParameters;
        if (constructorParameters.IsDefault)
        {
            builder.AppendIndent(2, $"public override {this.typeAlias} {methodName}({modifier} System.ReadOnlySpan<byte> span) => throw new System.NotSupportedException();");
            ThrowIfCancelled();
            return;
        }

        var members = this.members;
        builder.AppendIndent(2, $"public override {this.typeAlias} {methodName}({modifier} System.ReadOnlySpan<byte> span)");
        builder.AppendIndent(2, $"{{");
        if (auto is false)
            builder.AppendIndent(3, $"var body = span;");
        var bufferName = auto ? "span" : "body";
        for (var i = 0; i < members.Length; i++)
        {
            var last = (i == members.Length - 1);
            var method = (auto || last is false) ? "DecodeAuto" : "Decode";
            var keyword = (auto is false && last) ? "in" : "ref";
            builder.AppendIndent(3, $"var var{i} = this.cvt{i}.{method}({keyword} {bufferName});");
            ThrowIfCancelled();
        }
        AppendDecodeConstructor(builder, constructorParameters);
        builder.AppendIndent(2, $"}}");
    }

    private void AppendConverter()
    {
        var builder = new StringBuilder();
        var members = this.members;
        builder.AppendIndent(0, $"namespace {this.generatorContext.Namespace};");
        builder.AppendIndent(0);
        this.typeAliases.AppendAliases(builder);
        builder.AppendIndent(0);

        builder.AppendIndent(0, $"partial class {this.generatorContext.Name}");
        builder.AppendIndent(0, $"{{");
        builder.AppendIndent(1, $"private sealed class {this.outputConverterName} : {StaticExtensions.ConverterTypeName}<{this.typeAlias}>");
        builder.AppendIndent(1, $"{{");
        foreach (var i in members)
        {
            builder.AppendIndent(2, $"private readonly {StaticExtensions.ConverterTypeName}<{this.typeAliases.GetAlias(i.Type)}> cvt{i.Index};");
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
        foreach (var i in members)
        {
            var alias = this.typeAliases.GetAlias(i.Type);
            builder.AppendIndent(3, $"var cvt{i.Index} = ({StaticExtensions.ConverterTypeName}<{alias}>)context.GetConverter(typeof({alias}));");
            ThrowIfCancelled();
        }

        builder.AppendIndent(3, $"var converter = new {this.outputConverterName}(");
        for (var i = 0; i < members.Length; i++)
        {
            var last = (i == members.Length - 1);
            var tail = last ? ");" : ",";
            builder.AppendIndent(4, $"cvt{i}{tail}");
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
        bool IsTupleType()
        {
            if (symbol.IsGenericType is false)
                return false;
            var systemTupleTypes = (SystemTupleTypes ??= Enumerable.Range(1, 8)
                .Select(x => context.Compilation.GetTypeByMetadataName($"System.Tuple`{x}")?.ConstructUnboundGenericType())
                .OfType<INamedTypeSymbol>()
                .ToImmutableList());
            return systemTupleTypes.Any(x => SymbolEqualityComparer.Default.Equals(x, symbol.ConstructUnboundGenericType()));
        }

        var isTupleOrValueType = symbol.IsTupleType || IsTupleType();
        if (isTupleOrValueType is false &&
            symbol.GetAttributes().Any(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, context.TupleObjectAttributeTypeSymbol)) is false)
            return false;
        var closure = new TupleConverterContext(context, symbol, isTupleOrValueType);
        closure.AppendConverter();
        closure.AppendConverterCreator();
        return true;
    }
}
