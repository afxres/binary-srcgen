namespace Mikodev.Binary.SourceGeneration;

using Microsoft.CodeAnalysis;
using System.Collections.Generic;

#nullable enable

public class SourceGeneratorContext
{
    private readonly List<string> creators = new List<string>();

    public string Name { get; }

    public string Namespace { get; }

    public Compilation Compilation { get; }

    public SourceProductionContext SourceProductionContext { get; }

    public IEnumerable<string> ConverterCreators => this.creators;

    public SourceGeneratorContext(string name, string @namespace, Compilation compilation, SourceProductionContext sourceProductionContext)
    {
        Name = name;
        Namespace = @namespace;
        Compilation = compilation;
        SourceProductionContext = sourceProductionContext;
    }

    public void AddConverterCreator(string creator)
    {
        this.creators.Add(creator);
    }
}
