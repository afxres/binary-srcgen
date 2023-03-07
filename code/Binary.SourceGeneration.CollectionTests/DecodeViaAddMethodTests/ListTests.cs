namespace Mikodev.Binary.SourceGeneration.CollectionTests.DecodeViaAddMethodTests;

using Mikodev.Binary.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

[SourceGeneratorContext]
[SourceGeneratorInclude<List<int>>]
[SourceGeneratorInclude<List<string>>]
public partial class ListSourceGeneratorContext { }

public class ListTests
{
    public static IEnumerable<object[]> ListData()
    {
        var a = Enumerable.Range(0, 20).ToList();
        var b = Enumerable.Range(0, 100).ToList();
        var c = a.Select(x => x.ToString()).ToList();
        var d = b.Select(x => x.ToString()).ToList();
        yield return new object[] { a };
        yield return new object[] { b };
        yield return new object[] { c };
        yield return new object[] { d };
    }

    [Theory(DisplayName = "Encode")]
    [MemberData(nameof(ListData))]
    public void Encode<E>(List<E> source)
    {
        var builder = Generator.CreateDefaultBuilder();
        foreach (var i in ListSourceGeneratorContext.ConverterCreators)
            _ = builder.AddConverterCreator(i);
        var generator = builder.Build();
        var converter = generator.GetConverter<List<E>>();
        Assert.True(converter.GetType().Assembly == typeof(ListSourceGeneratorContext).Assembly);
        var expectedSource = new ReadOnlyMemory<E>(source.ToArray());
        var buffer = converter.Encode(source);
        var expectedBuffer = generator.Encode(expectedSource);
        Assert.Equal(expectedBuffer, buffer);
    }
}
