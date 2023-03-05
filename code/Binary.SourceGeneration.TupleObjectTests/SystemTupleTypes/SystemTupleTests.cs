namespace Mikodev.Binary.SourceGeneration.TupleObjectTests.SystemTupleTypes;

using Mikodev.Binary.Attributes;
using System;
using System.Collections.Generic;
using Xunit;

[SourceGeneratorContext]
[SourceGeneratorInclude<ValueTuple<int, int>>]
[SourceGeneratorInclude<ValueTuple<int, string>>]
[SourceGeneratorInclude<ValueTuple<string, int, double>>]
public partial class SystemTupleSourceGeneratorContext { }

public class SystemTupleTests
{
    public static IEnumerable<object[]> GetValueTupleData()
    {
        yield return new object[] { (1, 2) };
        yield return new object[] { (4096, "String") };
        yield return new object[] { ("First", 2, 3.0) };
    }

    [Theory(DisplayName = "System Tuple Test")]
    [MemberData(nameof(GetValueTupleData))]
    public void ValueTupleTest<T>(T source)
    {
        var builder = Generator.CreateDefaultBuilder();
        foreach (var i in SystemTupleSourceGeneratorContext.ConverterCreators)
            _ = builder.AddConverterCreator(i);
        var generator = builder.Build();
        var converter = generator.GetConverter<T>();
        Assert.Equal(typeof(SystemTupleSourceGeneratorContext).Assembly, converter.GetType().Assembly);
        var buffer = converter.Encode(source);
        var result = converter.Decode(buffer);
        Assert.Equal(source, result);

        var allocator = new Allocator();
        converter.EncodeAuto(ref allocator, source);
        var intent = new ReadOnlySpan<byte>(allocator.ToArray());
        var decode = converter.DecodeAuto(ref intent);
        Assert.Equal(source, decode);
        Assert.Equal(0, intent.Length);
    }
}
