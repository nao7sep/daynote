using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DayNote.Core.Text;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace DayNote.Tests.Text;

/// <summary>
/// Proves <see cref="TwitterText.WeightedLength"/> against Twitter's official twitter-text conformance
/// vectors from the v3 <c>WeightedTweetsWithDiscountedEmojiCounterTest</c> suite,
/// vendored verbatim under <c>Text/Conformance</c>.
/// </summary>
public sealed class TwitterTextConformanceTests
{
    private const string Section = "WeightedTweetsWithDiscountedEmojiCounterTest";

    public static TheoryData<string> ConformantVectors()
    {
        var data = new TheoryData<string>();
        foreach (var description in Vectors.Keys)
            data.Add(description);
        return data;
    }

    [Theory]
    [MemberData(nameof(ConformantVectors))]
    public void Weighted_length_matches_the_official_vector(string description)
    {
        var vector = Vectors[description];
        Assert.Equal(vector.WeightedLength, TwitterText.WeightedLength(vector.Text));
    }

    private static readonly IReadOnlyDictionary<string, Vector> Vectors = Load();

    private static IReadOnlyDictionary<string, Vector> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Text", "Conformance", "twitter-text-validate.yml");
        using var reader = new StreamReader(path);
        var stream = new YamlStream();
        stream.Load(reader);

        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var tests = (YamlMappingNode)root["tests"];
        var cases = (YamlSequenceNode)tests[Section];

        var result = new Dictionary<string, Vector>();
        foreach (var node in cases.Cast<YamlMappingNode>())
        {
            var description = Scalar(node, "description");
            var text = Scalar(node, "text");
            var expected = (YamlMappingNode)node["expected"];
            var weightedLength = int.Parse(Scalar(expected, "weightedLength"), CultureInfo.InvariantCulture);
            result.Add(description, new Vector(text, weightedLength));
        }

        return result;
    }

    private static string Scalar(YamlMappingNode node, string key) => ((YamlScalarNode)node[key]).Value!;

    private sealed record Vector(string Text, int WeightedLength);
}
