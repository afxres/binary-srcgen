namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using System.Diagnostics;

public class SymbolMemberInfo
{
    public string Name { get; }

    public bool IsReadOnly { get; }

    public SymbolMemberType MemberType { get; }

    public ITypeSymbol Type { get; }

    public int Index { get; }

    public SymbolMemberInfo(SymbolMemberType memberType, string name, bool @readonly, ITypeSymbol type, int index)
    {
        Debug.Assert(string.IsNullOrEmpty(name) is false);
        Debug.Assert(memberType is SymbolMemberType.Field or SymbolMemberType.Property);
        Name = name;
        IsReadOnly = @readonly;
        MemberType = memberType;
        Type = type;
        Index = index;
    }
}
