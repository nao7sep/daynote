// Copyright 2018 Twitter, Inc.
// Licensed under the Apache License, Version 2.0
// http://www.apache.org/licenses/LICENSE-2.0
//
// URL extraction ported from twitter-text 3.1.0 extractUrlsWithIndices.js and lib/idna.js.
// Modified to use .NET regex match groups, IdnMapping, and UTF-16 spans.
// https://github.com/twitter/twitter-text

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DayNote.Core.Text;

/// <summary>
/// A direct implementation of the documented twitter-text weighted-length configuration (v3
/// defaults). Most characters weigh 2; characters in the Latin and general-punctuation ranges
/// below weigh 1; the budget is 280. URLs are counted at a fixed transformed length of 23.
///
/// Like twitter-text, the input is NFC-normalized before counting, so a decomposed accent weighs the
/// same as its single precomposed code point.
///
/// URLs use twitter-text's TLD-based extraction and domain-label validation. Emoji sequences
/// recognized by twemoji-parser count as one unit weighing 2, matching the v3 configuration.
/// </summary>
public static class TwitterText
{
    public const int MaxWeightedLength = 280;

    private const int Scale = 100;
    private const int DefaultWeight = 200;
    private const int LightWeight = 100;
    private const int TransformedUrlWeight = 23 * Scale;

    // Inclusive code-point ranges that weigh 1 (LightWeight). Everything else weighs 2.
    private static readonly (int Start, int End)[] LightRanges =
    {
        (0x0000, 0x10FF),
        (0x2000, 0x200D),
        (0x2010, 0x201F),
        (0x2032, 0x2037),
    };

    /// <summary>Returns the weighted character length of <paramref name="text"/>.</summary>
    public static int WeightedLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        text = Normalize(text);
        var urls = ExtractUrls(text);
        var emoji = TwitterTextPatterns.Emoji.Matches(text);
        var emojiIndex = 0;
        var urlIndex = 0;
        var totalWeight = 0;
        var i = 0;

        while (i < text.Length)
        {
            while (urlIndex < urls.Count && urls[urlIndex].Start < i)
                urlIndex++;
            while (emojiIndex < emoji.Count && emoji[emojiIndex].Index < i)
                emojiIndex++;

            if (urlIndex < urls.Count && i == urls[urlIndex].Start)
            {
                totalWeight += TransformedUrlWeight;
                i += urls[urlIndex].Length;
                urlIndex++;
                continue;
            }

            if (emojiIndex < emoji.Count && i == emoji[emojiIndex].Index)
            {
                totalWeight += DefaultWeight;
                i += emoji[emojiIndex].Length;
                emojiIndex++;
                continue;
            }

            Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var consumed);
            totalWeight += WeightOf(rune.Value);
            i += consumed;
        }

        // All weights are multiples of Scale, so this division is exact.
        return totalWeight / Scale;
    }

    private readonly record struct TextSpan(int Start, int Length);

    private static List<TextSpan> ExtractUrls(string text)
    {
        var urls = new List<TextSpan>();
        if (!text.Contains('.'))
            return urls;

        foreach (Match match in TwitterTextPatterns.ExtractUrl.Matches(text))
        {
            var before = match.Groups[2].Value;
            var url = match.Groups[3].Value;
            var protocol = match.Groups[4].Value;
            var domain = match.Groups[5].Value;
            var path = match.Groups[7].Value;
            var start = match.Groups[3].Index;
            var end = start + url.Length;
            if (!IsValidUrl(url, protocol.Length > 0 ? protocol : "https://", domain))
                continue;

            if (protocol.Length == 0)
            {
                if (TwitterTextPatterns.InvalidUrlWithoutProtocolPrecedingChars.IsMatch(before))
                    continue;

                var lastUrl = -1;
                foreach (Match asciiDomain in TwitterTextPatterns.ValidAsciiDomain.Matches(domain))
                {
                    urls.Add(new TextSpan(start + asciiDomain.Index, asciiDomain.Length));
                    lastUrl = urls.Count - 1;
                }

                if (lastUrl >= 0 && path.Length > 0)
                {
                    // parseTweet advances by the extracted URL text's length, which replaces the
                    // full domain with the last ASCII match, rather than by the entity's end index.
                    urls[lastUrl] = new TextSpan(urls[lastUrl].Start,
                        urls[lastUrl].Length + url.Length - domain.Length);
                }
            }
            else
            {
                var tco = TwitterTextPatterns.ValidTcoUrl.Match(url);
                if (tco.Success)
                {
                    if (tco.Groups[1].Length > 40)
                        continue;
                    end = start + tco.Length;
                }

                urls.Add(new TextSpan(start, end - start));
            }
        }

        return urls;
    }

    private static bool IsValidUrl(string url, string protocol, string domain)
    {
        if (domain.StartsWith("xn--", StringComparison.Ordinal)
            && !TwitterTextPatterns.ValidAsciiDomain.IsMatch(domain))
            return false;

        var idn = new IdnMapping();
        foreach (var label in domain.Split('.'))
        {
            var asciiLabel = label;
            if (label.Any(c => c > 0x7F))
            {
                try { asciiLabel = idn.GetAscii(label); }
                catch (ArgumentException) { return false; }
            }

            if (asciiLabel.Length is < 1 or > 63)
                return false;
        }

        // The upstream extractor checks each encoded label but retains the original domain's
        // length for this bound; preserve that policy for reference conformance.
        return protocol.Length + url.Length <= 4096;
    }

    /// <summary>
    /// Returns the NFC-normalized form, the basis twitter-text counts. The common case (already-NFC
    /// text such as ASCII or CJK) is returned unchanged without allocating. Malformed UTF-16 — an
    /// unpaired surrogate — has no defined normal form; rather than throw on the live editor's text,
    /// it is counted as-is, matching the rest of this counter's never-throw behavior.
    /// </summary>
    private static string Normalize(string text)
    {
        try
        {
            return text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static int WeightOf(int codePoint)
    {
        foreach (var (start, end) in LightRanges)
        {
            if (codePoint >= start && codePoint <= end)
            {
                return LightWeight;
            }
        }

        return DefaultWeight;
    }
}
