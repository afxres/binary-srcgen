namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

#nullable enable

public class TupleConverterContext
{
    private readonly INamedTypeSymbol namedTypeSymbol;

    private readonly SourceProductionContext sourceProductionContext;

    private readonly SourceGeneratorContext sourceGeneratorContext;

    private readonly Dictionary<string, string> typeAliases = new Dictionary<string, string>();

    private int typeAliasIndex = 0;

    private readonly StringBuilder builder = new StringBuilder();

    private List<SymbolMemberInfo> members;

    private string typeAlias;

    private string GetTypeAlias(ITypeSymbol type)
    {
        var typeFullName = type.ToDisplayString(StaticExtensions.FullyQualifiedFormatNoSpecialTypes);
        if (this.typeAliases.TryGetValue(typeFullName, out var result))
            return result;
        var typeAlias = $"_T_{this.typeAliasIndex++}";
        this.typeAliases.Add(typeFullName, typeAlias);
        return typeAlias;
    }

    private void ThrowIfCancelled()
    {
        this.sourceProductionContext.CancellationToken.ThrowIfCancellationRequested();
    }

    public TupleConverterContext(SourceGeneratorContext sourceGeneratorContext, INamedTypeSymbol namedTypeSymbol, SourceProductionContext sourceProductionContext)
    {
        this.sourceGeneratorContext = sourceGeneratorContext ?? throw new ArgumentNullException(nameof(sourceGeneratorContext));
        this.namedTypeSymbol = namedTypeSymbol ?? throw new ArgumentNullException(nameof(namedTypeSymbol));
        this.sourceProductionContext = sourceProductionContext;
    }

    public void Invoke()
    {
        ThrowIfCancelled();

        var sharedIndex = new StrongBox<int> { Value = 0 };
        var members = this.namedTypeSymbol.GetMembers().Select(x => StaticExtensions.GetPublicFiledOrProperty(x, sharedIndex)).OfType<SymbolMemberInfo>().OrderBy(x => x.Index).ToList();
        foreach (var i in members)
            _ = GetTypeAlias(i.Type);
        this.members = members;
        this.typeAlias = GetTypeAlias(this.namedTypeSymbol);
        var builder = this.builder;
        var outputConverterName = $"{StaticExtensions.GetSafePartTypeName(this.namedTypeSymbol)}__Converter";
        builder.AppendIndent(0, $"namespace {this.sourceGeneratorContext.Namespace};");
        builder.AppendIndent(0);
        foreach (var i in this.typeAliases)
            builder.AppendIndent(0, $"using {i.Value} = {i.Key};");
        builder.AppendIndent(0);

        builder.AppendIndent(0, $"partial class {this.sourceGeneratorContext.Name}");
        builder.AppendIndent(0, $"{{");
        builder.AppendIndent(1, $"public class {outputConverterName} : {StaticExtensions.ConverterTypeName}<{this.typeAlias}>");
        builder.AppendIndent(1, $"{{");
        foreach (var i in members)
        {
            builder.AppendIndent(2, $"private readonly Mikodev.Binary.Converter<{GetTypeAlias(i.Type)}> _cvt_{i.Index};");
            builder.AppendIndent(2);
            ThrowIfCancelled();
        }

        builder.AppendIndent(2, $"public {outputConverterName}(");
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var tail = (i == members.Count - 1) ? ")" : ",";
            builder.AppendIndent(3, $"Mikodev.Binary.Converter<{GetTypeAlias(member.Type)}> _arg_{member.Index}{tail}");
            ThrowIfCancelled();
        }
        builder.AppendIndent(2, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            builder.AppendIndent(3, $"this._cvt_{i} = _arg_{i};");
            ThrowIfCancelled();
        }
        builder.AppendIndent(2, $"}}");
        builder.AppendIndent(2);

        AppendEncodeMethod(auto: false);
        AppendEncodeMethod(auto: true);
        AppendDecodeMethod(auto: false);
        AppendDecodeMethod(auto: true);

        builder.AppendIndent(1, $"}}");
        builder.AppendIndent(0, $"}}");
        var outputCode = builder.ToString();
        var outputFileName = StaticExtensions.GetSafeOutputFileName(this.namedTypeSymbol);
        this.sourceProductionContext.AddSource($"{outputFileName}.g.cs", outputCode);
    }

    private void AppendEncodeMethod(bool auto)
    {
        var builder = this.builder;
        var members = this.members;
        var methodName = auto ? "EncodeAuto" : "Encode";
        builder.AppendIndent(2, $"public override void {methodName}(ref {StaticExtensions.AllocatorTypeName} allocator, {this.typeAlias} item)");
        builder.AppendIndent(2, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            var last = (i == members.Count - 1);
            var member = members[i];
            var method = (auto || last is false) ? "EncodeAuto" : "Encode";
            builder.AppendIndent(3, $"this._cvt_{i}.{method}(ref allocator, item.{member.Name});");
            ThrowIfCancelled();
        }
        builder.AppendIndent(2, $"}}");
        builder.AppendIndent(2);
    }

    private void AppendDecodeMethod(bool auto)
    {
        var builder = this.builder;
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
            builder.AppendIndent(3, $"var _var_{i} = this._cvt_{i}.{method}({keyword} {bufferName});");
            ThrowIfCancelled();
        }
        builder.AppendIndent(3, $"var result = new {this.typeAlias}()");
        builder.AppendIndent(3, $"{{");
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            builder.AppendIndent(4, $"{member.Name} = _var_{i},");
            ThrowIfCancelled();
        }
        builder.AppendIndent(3, $"}};");
        builder.AppendIndent(3, $"return result;");
        builder.AppendIndent(2, $"}}");
        builder.AppendIndent(2);
    }
}
