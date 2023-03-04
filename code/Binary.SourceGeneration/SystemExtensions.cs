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

    public static void AppendIndent(this StringBuilder builder, int indent, string line, int newLines = 1)
    {
        const int MaxLoop = 16;
        if ((uint)indent > MaxLoop)
            throw new ArgumentOutOfRangeException(nameof(indent));
        if ((uint)newLines > MaxLoop)
            throw new ArgumentOutOfRangeException(nameof(newLines));
        var current = new StringBuilder();
        for (var i = 0; i < indent; i++)
            _ = current.Append("    ");
        _ = current.Append(line);
        _ = builder.Append(current.ToString());
        for (var i = 0; i < newLines; i++)
            _ = builder.AppendLine();
        return;
    }
}
