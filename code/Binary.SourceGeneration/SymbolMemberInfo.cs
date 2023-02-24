namespace Mikodev.Binary.SourceGeneration;

using System.Diagnostics;

public class SymbolMemberInfo
{
    public string Name { get; }

    public bool IsReadOnly { get; }

    public SymbolMemberType MemberType { get; }

    public SymbolMemberInfo(SymbolMemberType memberType, string name, bool @readonly)
    {
        Debug.Assert(string.IsNullOrEmpty(name) is false);
        Debug.Assert(memberType is SymbolMemberType.Field or SymbolMemberType.Property);
        Name = name;
        IsReadOnly = @readonly;
        MemberType = memberType;
    }
}
