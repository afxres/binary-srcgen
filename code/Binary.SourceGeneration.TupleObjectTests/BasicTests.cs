namespace Mikodev.Binary.SourceGeneration.TupleObjectTests;

using Mikodev.Binary.Attributes;
using Xunit;

[SourceGenerationContext]
public partial class TestSourceGenerationContext { }

[TupleObject]
public class Person
{
    [TupleKey(0)]
    public required int Id { get; init; }

    [TupleKey(1)]
    public required string Name { get; init; }
}

public class BasicTests
{
    [Fact(DisplayName = "Main Test")]
    public void MainTest()
    {
        // nothing for now
    }
}
