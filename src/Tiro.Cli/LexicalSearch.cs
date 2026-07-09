using System.Text.RegularExpressions;

namespace Tiro.Cli;

public static partial class LexicalSearch
{
    private static readonly HashSet<string> ProtectedSuffixWords = new(StringComparer.Ordinal)
    {
        "analysis", "billing", "building", "campus", "ceiling", "focus", "meeting", "plus", "process", "speed", "status", "virus", "warning"
    };

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "from", "how",
        "in", "into", "is", "it", "its", "of", "on", "or", "that", "the", "their",
        "then", "there", "this", "to", "was", "were", "what", "when", "where", "with"
    };

    public static IReadOnlyList<string> Tokenize(string query)
    {
        return TokenRegex().Matches(query ?? string.Empty)
            .Select(match => NormalizeToken(match.Value))
            .Where(token => token.Length >= 2 && !Stopwords.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static LexicalScore Score(string text, string metadata, IReadOnlyList<string> terms)
    {
        var textTokens = TokenizeField(text);
        var metadataTokens = TokenizeField(metadata);
        var textCounts = CountTokens(textTokens);
        var metadataCounts = CountTokens(metadataTokens);
        var details = new List<ScoringDetail>();
        var termScore = 0;
        var matchedTerms = new List<string>();

        foreach (var term in terms)
        {
            textCounts.TryGetValue(term, out var textHits);
            metadataCounts.TryGetValue(term, out var metadataHits);
            if (textHits == 0 && metadataHits == 0)
            {
                continue;
            }

            var perTermScore = Math.Min(textHits, 4) * 12 + Math.Min(metadataHits, 3) * 3;
            termScore += perTermScore;
            matchedTerms.Add(term);
            details.Add(new ScoringDetail(term, textHits, metadataHits, perTermScore));
        }

        if (matchedTerms.Count == 0)
        {
            return new LexicalScore(0, Array.Empty<string>(), Array.Empty<ScoringDetail>(), new ScoringSummary(0, 0, 0, 0, 0), false);
        }

        var score = termScore;
        var textMatchedTerms = terms.Where(term => textCounts.ContainsKey(term)).ToArray();
        var allTermsInText = terms.Count > 0 && textMatchedTerms.Length == terms.Count;
        var coverageBonus = matchedTerms.Count * 6;
        if (allTermsInText)
        {
            coverageBonus += 24;
        }

        score += coverageBonus;
        var phraseHit = ContainsOrderedPhrase(textTokens, terms);
        var phraseBonus = 0;
        if (phraseHit)
        {
            phraseBonus = 18;
            score += phraseBonus;
        }

        var matchedTextHits = details.Sum(detail => detail.TextHits);
        var densityBonus = CalculateDensityBonus(textTokens.Count, matchedTextHits, textMatchedTerms.Length, terms.Count);
        score += densityBonus;

        var summary = new ScoringSummary(termScore, coverageBonus, phraseBonus, densityBonus, matchedTextHits);
        return new LexicalScore(score, matchedTerms, details, summary, phraseHit);
    }

    public static string BuildSnippet(string text, IReadOnlyList<string> terms)
    {
        var normalized = (text ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ');

        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ");
        }

        var lowerTokens = TokenRegex().Matches(normalized)
            .Select(match => new
            {
                Token = NormalizeToken(match.Value),
                Index = match.Index
            })
            .ToArray();
        var firstHit = lowerTokens
            .Where(token => terms.Contains(token.Token, StringComparer.Ordinal))
            .Select(token => token.Index)
            .Where(index => index >= 0)
            .DefaultIfEmpty(0)
            .Min();

        var start = Math.Max(0, firstHit - 80);
        var length = Math.Min(260, normalized.Length - start);
        var snippet = normalized.Substring(start, length).Trim();

        if (start > 0)
        {
            snippet = "..." + snippet;
        }

        if (start + length < normalized.Length)
        {
            snippet += "...";
        }

        return snippet;
    }

    private static IReadOnlyList<string> TokenizeField(string value)
    {
        return TokenRegex().Matches(value ?? string.Empty)
            .Select(match => NormalizeToken(match.Value))
            .Where(token => token.Length >= 2)
            .ToArray();
    }

    private static Dictionary<string, int> CountTokens(IReadOnlyList<string> tokens)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            counts[token] = counts.TryGetValue(token, out var count) ? count + 1 : 1;
        }
        return counts;
    }

    private static bool ContainsOrderedPhrase(IReadOnlyList<string> textTokens, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0 || textTokens.Count < terms.Count)
        {
            return false;
        }

        for (var index = 0; index <= textTokens.Count - terms.Count; index++)
        {
            var matches = true;
            for (var offset = 0; offset < terms.Count; offset++)
            {
                if (!string.Equals(textTokens[index + offset], terms[offset], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static int CalculateDensityBonus(int tokenCount, int matchedTextHits, int textMatchedTermCount, int queryTermCount)
    {
        if (tokenCount == 0 || matchedTextHits == 0)
        {
            return 0;
        }

        var density = matchedTextHits / (double)tokenCount;
        var bonus = density switch
        {
            >= 0.18 => 18,
            >= 0.10 => 12,
            >= 0.05 => 6,
            _ => 0
        };

        if (queryTermCount > 0 && textMatchedTermCount == queryTermCount && tokenCount <= 80)
        {
            bonus += 8;
        }

        return bonus;
    }

    private static string NormalizeToken(string value)
    {
        var token = value
            .ToLowerInvariant()
            .Trim((char)39, '.', '-', '_');

        if (token.EndsWith("'s", StringComparison.Ordinal) && token.Length > 3)
        {
            token = token[..^2];
        }

        if (ProtectedSuffixWords.Contains(token))
        {
            return token;
        }

        if (token.Length > 4 && token.EndsWith("ies", StringComparison.Ordinal))
        {
            token = $"{token[..^3]}y";
        }
        else if (token.Length > 4 && EndsWithPluralEs(token))
        {
            token = token[..^2];
        }
        else if (token.Length > 5 && token.EndsWith("ing", StringComparison.Ordinal))
        {
            token = token[..^3];
        }
        else if (token.Length > 4 && token.EndsWith("ed", StringComparison.Ordinal))
        {
            token = token[..^2];
        }
        else if (token.Length > 3
            && token.EndsWith("s", StringComparison.Ordinal)
            && !token.EndsWith("ss", StringComparison.Ordinal)
            && !token.EndsWith("us", StringComparison.Ordinal)
            && !token.EndsWith("is", StringComparison.Ordinal))
        {
            token = token[..^1];
        }

        return token;
    }

    private static bool EndsWithPluralEs(string token)
    {
        return token.EndsWith("ses", StringComparison.Ordinal)
            || token.EndsWith("xes", StringComparison.Ordinal)
            || token.EndsWith("zes", StringComparison.Ordinal)
            || token.EndsWith("ches", StringComparison.Ordinal)
            || token.EndsWith("shes", StringComparison.Ordinal);
    }

    [GeneratedRegex("[A-Za-z0-9][A-Za-z0-9'.-]*", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();
}

public sealed record LexicalScore(
    int Score,
    IReadOnlyList<string> MatchedTerms,
    IReadOnlyList<ScoringDetail> Details,
    ScoringSummary Summary,
    bool PhraseHit);
