namespace Mikodev.Binary.SourceGeneration;

using System;
using System.Text;

#nullable enable

public static class SystemExtensions
{
    public static void AppendIndent(this StringBuilder builder, int indent)
    {
        _ = builder.AppendLine();
    }

    public static void AppendIndent(this StringBuilder builder, int indent, string line)
    {
        const int MaxLoop = 16;
        if ((uint)indent > MaxLoop)
            throw new ArgumentOutOfRangeException(nameof(indent));
        var current = new StringBuilder();
        for (var i = 0; i < indent; i++)
            _ = current.Append("    ");
        _ = current.Append(line);
        _ = builder.Append(current.ToString());
        _ = builder.AppendLine();
    }
}
