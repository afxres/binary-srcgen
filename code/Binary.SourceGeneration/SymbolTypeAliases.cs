namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class SymbolTypeAliases
{
    private readonly Dictionary<ITypeSymbol, (string Alias, string Full)> aliases;

    private int index;

    public SymbolTypeAliases(ITypeSymbol self)
    {
        var aliases = new Dictionary<ITypeSymbol, (string, string)>(SymbolEqualityComparer.Default);
        aliases.Add(self, ("_TSelf", StaticExtensions.GetFullName(self)));
        this.index = 0;
        this.aliases = aliases;
    }

    public void Add(ITypeSymbol symbol)
    {
        var aliases = this.aliases;
        if (aliases.ContainsKey(symbol))
            return;
        var index = this.index++;
        aliases.Add(symbol, ($"_T{index}", StaticExtensions.GetFullName(symbol)));
    }

    public string GetAlias(ITypeSymbol symbol)
    {
        return this.aliases[symbol].Alias;
    }

    public void AppendAliases(StringBuilder builder)
    {
        foreach (var (alias, full) in this.aliases.Values.OrderBy(x => x.Alias))
            _ = builder.AppendLine($"using {alias} = {full};");
        return;
    }
}
