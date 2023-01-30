namespace Mikodev.Binary.SourceGeneration.TupleObjectTests;

using Mikodev.Binary.Attributes;
using Xunit;

public class BasicTests
{
    [TupleObject]
    public class Person
    {
        [TupleKey(0)]
        public required int Id { get; init; }

        [TupleKey(1)]
        public required string Name { get; init; }
    }

    [Fact(DisplayName = "Main Test")]
    public void MainTest()
    {
        // nothing for now
    }
}
