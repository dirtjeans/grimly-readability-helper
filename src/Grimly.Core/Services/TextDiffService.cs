using Grimly.Models;

namespace Grimly.Services;

public interface ITextDiffService
{
    List<TextDiff> ComputeDiff(string original, string corrected);
    List<ReviewSegment> GroupIntoSegments(List<TextDiff> diffs);
}

public sealed class TextDiffService : ITextDiffService
{
    public List<TextDiff> ComputeDiff(string original, string corrected)
    {
        var origWords = Tokenize(original);
        var corrWords = Tokenize(corrected);
        var lcs = ComputeLcs(origWords, corrWords);
        var result = new List<TextDiff>();

        int oi = 0, ci = 0, li = 0;

        while (oi < origWords.Length || ci < corrWords.Length)
        {
            if (li < lcs.Count && oi < origWords.Length && ci < corrWords.Length
                && origWords[oi] == lcs[li] && corrWords[ci] == lcs[li])
            {
                result.Add(new TextDiff { Text = origWords[oi], Type = DiffType.Unchanged });
                oi++; ci++; li++;
            }
            else
            {
                while (oi < origWords.Length && (li >= lcs.Count || origWords[oi] != lcs[li]))
                {
                    result.Add(new TextDiff { Text = origWords[oi], Type = DiffType.Removed });
                    oi++;
                }
                while (ci < corrWords.Length && (li >= lcs.Count || corrWords[ci] != lcs[li]))
                {
                    result.Add(new TextDiff { Text = corrWords[ci], Type = DiffType.Added });
                    ci++;
                }
            }
        }

        return result;
    }

    public List<ReviewSegment> GroupIntoSegments(List<TextDiff> diffs)
    {
        var segments = new List<ReviewSegment>();
        int id = 0;
        int i = 0;

        while (i < diffs.Count)
        {
            if (diffs[i].Type == DiffType.Unchanged)
            {
                // Collect consecutive unchanged tokens
                var text = "";
                while (i < diffs.Count && diffs[i].Type == DiffType.Unchanged)
                {
                    text += diffs[i].Text;
                    i++;
                }
                segments.Add(new ReviewSegment
                {
                    Id = id++,
                    IsChange = false,
                    UnchangedText = text
                });
            }
            else
            {
                // Collect a change group: consecutive Removed then consecutive Added
                var removed = "";
                var added = "";

                while (i < diffs.Count && diffs[i].Type == DiffType.Removed)
                {
                    removed += diffs[i].Text;
                    i++;
                }
                while (i < diffs.Count && diffs[i].Type == DiffType.Added)
                {
                    added += diffs[i].Text;
                    i++;
                }

                segments.Add(new ReviewSegment
                {
                    Id = id++,
                    IsChange = true,
                    RemovedText = removed,
                    AddedText = added
                });
            }
        }

        return segments;
    }

    private static string[] Tokenize(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                int start = i;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                tokens.Add(text[start..i]);
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                tokens.Add(text[start..i]);
            }
        }
        return tokens.ToArray();
    }

    private static List<string> ComputeLcs(string[] a, string[] b)
    {
        int m = a.Length, n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                dp[i, j] = a[i - 1] == b[j - 1]
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        var lcs = new List<string>();
        int x = m, y = n;
        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1])
            {
                lcs.Add(a[x - 1]);
                x--; y--;
            }
            else if (dp[x - 1, y] > dp[x, y - 1])
                x--;
            else
                y--;
        }
        lcs.Reverse();
        return lcs;
    }
}
