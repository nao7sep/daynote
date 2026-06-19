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
/// vectors — the <c>WeightedTweetsCounterTest</c> suite, vendored verbatim under <c>Text/Conformance</c>.
/// DayNote's implementation is a deliberate subset of the full engine: it weighs each code point by the
/// v3 ranges and counts any scheme/<c>www.</c> token as a fixed-23 URL, but it does not validate URL
/// domains. The few vectors that hinge on that domain validation are listed as known divergences and
/// asserted to <em>still</em> diverge, so the exclusion list cannot silently rot if the code changes.
/// </summary>
public sealed class TwitterTextConformanceTests
{
    private const string Section = "WeightedTweetsCounterTest";

    /// <summary>
    /// Vectors whose expected length depends on URL domain validation the subset omits on purpose:
    /// twitter-text rejects an over-long domain label and counts the URL as plain text, whereas DayNote's
    /// simpler matcher still counts any scheme-prefixed token as a 23-weight URL. Keyed by the vector's
    /// description in the official file.
    /// </summary>
    private static readonly IReadOnlySet<string> KnownDivergences = new HashSet<string>
    {
        "Handle long url with invalid domain labels and short url",
        "Do not allow > 140 CJK characters by virtue of CJK chars greater than 63 punycode encoded chars in the host",
    };

    public static TheoryData<string> ConformantVectors() => Descriptions(d => !KnownDivergences.Contains(d));

    public static TheoryData<string> DivergentVectors() => Descriptions(KnownDivergences.Contains);

    [Theory]
    [MemberData(nameof(ConformantVectors))]
    public void Weighted_length_matches_the_official_vector(string description)
    {
        var vector = Vectors[description];
        Assert.Equal(vector.WeightedLength, TwitterText.WeightedLength(vector.Text));
    }

    [Theory]
    [MemberData(nameof(DivergentVectors))]
    public void Known_divergences_still_diverge(string description)
    {
        // If the implementation ever becomes conformant on one of these, this fails on purpose so the
        // entry is removed from KnownDivergences rather than quietly masking a now-passing vector.
        var vector = Vectors[description];
        Assert.NotEqual(vector.WeightedLength, TwitterText.WeightedLength(vector.Text));
    }

    private static TheoryData<string> Descriptions(Func<string, bool> predicate)
    {
        var data = new TheoryData<string>();
        foreach (var description in Vectors.Keys.Where(predicate))
        {
            data.Add(description);
        }

        return data;
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
