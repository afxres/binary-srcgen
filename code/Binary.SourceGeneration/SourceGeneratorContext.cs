namespace Mikodev.Binary.SourceGeneration;

using System.Collections.Generic;

#nullable enable

public class SourceGeneratorContext
{
    private readonly List<string> creators = new List<string>();

    public string Name { get; }

    public string Namespace { get; }

    public IEnumerable<string> ConverterCreators => this.creators;

    public SourceGeneratorContext(string name, string @namespace)
    {
        this.Name = name;
        this.Namespace = @namespace;
    }

    public void AddConverterCreator(string creator)
    {
        this.creators.Add(creator);
    }
}
