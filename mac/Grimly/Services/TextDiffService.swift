import Foundation

class TextDiffService {
    func computeDiff(original: String, corrected: String) -> [TextDiff] {
        let origWords = tokenize(original)
        let corrWords = tokenize(corrected)
        let lcs = computeLCS(origWords, corrWords)
        var result: [TextDiff] = []

        var oi = 0, ci = 0, li = 0

        while oi < origWords.count || ci < corrWords.count {
            if li < lcs.count && oi < origWords.count && ci < corrWords.count
                && origWords[oi] == lcs[li] && corrWords[ci] == lcs[li]
            {
                result.append(TextDiff(text: origWords[oi], type: .unchanged))
                oi += 1; ci += 1; li += 1
            } else {
                while oi < origWords.count && (li >= lcs.count || origWords[oi] != lcs[li]) {
                    result.append(TextDiff(text: origWords[oi], type: .removed))
                    oi += 1
                }
                while ci < corrWords.count && (li >= lcs.count || corrWords[ci] != lcs[li]) {
                    result.append(TextDiff(text: corrWords[ci], type: .added))
                    ci += 1
                }
            }
        }

        return result
    }

    func groupIntoSegments(_ diffs: [TextDiff]) -> [ReviewSegment] {
        var segments: [ReviewSegment] = []
        var segId = 0
        var i = 0

        while i < diffs.count {
            if diffs[i].type == .unchanged {
                var text = ""
                while i < diffs.count && diffs[i].type == .unchanged {
                    text += diffs[i].text
                    i += 1
                }
                segments.append(ReviewSegment(id: segId, isChange: false, unchangedText: text))
                segId += 1
            } else {
                var removed = ""
                var added = ""

                while i < diffs.count && diffs[i].type == .removed {
                    removed += diffs[i].text
                    i += 1
                }
                while i < diffs.count && diffs[i].type == .added {
                    added += diffs[i].text
                    i += 1
                }

                segments.append(ReviewSegment(id: segId, isChange: true, removedText: removed, addedText: added))
                segId += 1
            }
        }

        return segments
    }

    private func tokenize(_ text: String) -> [String] {
        var tokens: [String] = []
        var i = text.startIndex

        while i < text.endIndex {
            if text[i].isWhitespace {
                let start = i
                while i < text.endIndex && text[i].isWhitespace { i = text.index(after: i) }
                tokens.append(String(text[start..<i]))
            } else {
                let start = i
                while i < text.endIndex && !text[i].isWhitespace { i = text.index(after: i) }
                tokens.append(String(text[start..<i]))
            }
        }

        return tokens
    }

    private func computeLCS(_ a: [String], _ b: [String]) -> [String] {
        let m = a.count, n = b.count
        var dp = Array(repeating: Array(repeating: 0, count: n + 1), count: m + 1)

        for i in 1...m {
            for j in 1...n {
                if a[i - 1] == b[j - 1] {
                    dp[i][j] = dp[i - 1][j - 1] + 1
                } else {
                    dp[i][j] = max(dp[i - 1][j], dp[i][j - 1])
                }
            }
        }

        var lcs: [String] = []
        var x = m, y = n
        while x > 0 && y > 0 {
            if a[x - 1] == b[y - 1] {
                lcs.append(a[x - 1])
                x -= 1; y -= 1
            } else if dp[x - 1][y] > dp[x][y - 1] {
                x -= 1
            } else {
                y -= 1
            }
        }
        lcs.reverse()
        return lcs
    }
}
