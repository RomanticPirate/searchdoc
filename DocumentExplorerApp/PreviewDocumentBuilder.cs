namespace DocumentExplorerApp;

public sealed record PreviewMatch(int MatchIndex, int MatchLength, int MatchNumber, int TotalMatches, int LineNumber, string Snippet);

public sealed record PreviewRenderData(
    string Title,
    string Meta,
    string TypeLabel,
    string Body,
    IReadOnlyList<PreviewMatch> Matches,
    int SelectedMatchIndex,
    bool HighlightKeyword,
    bool IsTruncated);

public static class PreviewDocumentBuilder
{
    private const int MaxPreviewChars = 2400;
    private const int FullPreviewThresholdChars = 40000;

    public const string SelectedMatchStartMarker = "\uE000";
    public const string SelectedMatchEndMarker = "\uE001";

    public static PreviewRenderData Build(
        SearchResult result,
        SearchTarget searchTarget,
        DocumentIndexData? currentIndex,
        string keyword,
        int requestedMatchIndex,
        bool showFullDocument)
    {
        var extension = Path.GetExtension(result.Path).ToLowerInvariant();
        var typeLabel = GetTypeLabel(extension);
        var meta = $"경로 : {result.DirectoryPath}";

        if (searchTarget == SearchTarget.FileName)
        {
            return new PreviewRenderData(
                result.FileName,
                meta,
                typeLabel,
                string.Empty,
                [],
                0,
                false,
                false);
        }

        var entry = currentIndex?.Entries.FirstOrDefault(item =>
            string.Equals(item.Path, result.Path, StringComparison.OrdinalIgnoreCase));

        var content = entry?.Content ?? result.Snippet;
        if (result.Status == "실패")
        {
            return new PreviewRenderData(
                result.FileName,
                meta,
                typeLabel,
                content,
                [],
                0,
                false,
                false);
        }

        var matches = CreateMatches(content, keyword);
        var normalizedIndex = matches.Count == 0
            ? 0
            : Math.Clamp(requestedMatchIndex, 0, matches.Count - 1);

        var shouldShowFullBody = showFullDocument || content.Length <= FullPreviewThresholdChars;
        var previewSource = shouldShowFullBody
            ? CreateFullPreviewBody(content, matches, normalizedIndex)
            : CreateSnippetPreviewBody(content, matches, normalizedIndex);

        return new PreviewRenderData(
            result.FileName,
            meta,
            typeLabel,
            FormatBody(extension, previewSource),
            matches,
            normalizedIndex,
            !string.IsNullOrWhiteSpace(keyword),
            !shouldShowFullBody);
    }

    private static string CreateFullPreviewBody(string content, IReadOnlyList<PreviewMatch> matches, int selectedMatchIndex)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (matches.Count == 0)
        {
            return content;
        }

        var selectedMatch = matches[selectedMatchIndex];
        return InsertSelectedMatchMarkers(content, selectedMatch.MatchIndex, selectedMatch.MatchLength);
    }

    private static string CreateSnippetPreviewBody(string content, IReadOnlyList<PreviewMatch> matches, int selectedMatchIndex)
    {
        if (matches.Count == 0)
        {
            return CreateDefaultSnippet(content);
        }

        var selectedMatch = matches[selectedMatchIndex];
        return CreateSnippetAroundMatch(content, selectedMatch.MatchIndex, selectedMatch.MatchLength, true);
    }

    private static string CreateDefaultSnippet(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (content.Length <= MaxPreviewChars)
        {
            return content;
        }

        return content[..MaxPreviewChars] + Environment.NewLine + "...";
    }

    private static List<PreviewMatch> CreateMatches(string content, string keyword)
    {
        var matches = new List<PreviewMatch>();
        if (string.IsNullOrEmpty(content) || string.IsNullOrWhiteSpace(keyword))
        {
            return matches;
        }

        var indices = new List<int>();
        var searchIndex = 0;
        while (searchIndex < content.Length)
        {
            var foundIndex = content.IndexOf(keyword, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (foundIndex < 0)
            {
                break;
            }

            indices.Add(foundIndex);
            searchIndex = foundIndex + Math.Max(1, keyword.Length);
        }

        for (var i = 0; i < indices.Count; i++)
        {
            var matchIndex = indices[i];
            matches.Add(new PreviewMatch(
                matchIndex,
                keyword.Length,
                i + 1,
                indices.Count,
                GetLineNumber(content, matchIndex),
                CreateSnippetAroundMatch(content, matchIndex, keyword.Length, false)));
        }

        return matches;
    }

    private static int GetLineNumber(string content, int charIndex)
    {
        var lineNumber = 1;
        for (var i = 0; i < Math.Min(content.Length, charIndex); i++)
        {
            if (content[i] == '\n')
            {
                lineNumber++;
            }
        }

        return lineNumber;
    }

    private static string InsertSelectedMatchMarkers(string content, int matchIndex, int matchLength)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var safeIndex = Math.Max(0, Math.Min(content.Length, matchIndex));
        var safeLength = Math.Min(matchLength, Math.Max(0, content.Length - safeIndex));
        if (safeLength <= 0)
        {
            return content;
        }

        return content.Insert(safeIndex + safeLength, SelectedMatchEndMarker)
            .Insert(safeIndex, SelectedMatchStartMarker);
    }

    private static string CreateSnippetAroundMatch(string content, int matchIndex, int matchLength, bool markSelectedMatch)
    {
        var lines = content.Split('\n');
        var cumulativeLength = 0;
        var matchLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var lineLength = lines[i].Length + 1;
            if (matchIndex < cumulativeLength + lineLength)
            {
                matchLine = i;
                break;
            }

            cumulativeLength += lineLength;
        }

        var startLine = Math.Max(0, matchLine - 6);
        var endLine = Math.Min(lines.Length - 1, matchLine + 16);
        var snippetLines = lines[startLine..(endLine + 1)].ToArray();

        if (markSelectedMatch)
        {
            var localLineIndex = matchLine - startLine;
            var lineStartIndex = cumulativeLength;
            var matchIndexInLine = Math.Max(0, Math.Min(snippetLines[localLineIndex].Length, matchIndex - lineStartIndex));
            var safeLength = Math.Min(matchLength, Math.Max(0, snippetLines[localLineIndex].Length - matchIndexInLine));

            if (safeLength > 0)
            {
                snippetLines[localLineIndex] =
                    snippetLines[localLineIndex].Insert(matchIndexInLine + safeLength, SelectedMatchEndMarker)
                        .Insert(matchIndexInLine, SelectedMatchStartMarker);
            }
        }

        var snippet = string.Join(Environment.NewLine, snippetLines);

        if (startLine > 0)
        {
            snippet = "..." + Environment.NewLine + snippet;
        }

        if (endLine < lines.Length - 1)
        {
            snippet += Environment.NewLine + "...";
        }

        var maxChars = Math.Max(MaxPreviewChars, matchLength + 400);
        if (snippet.Length > maxChars)
        {
            snippet = snippet[..maxChars];
        }

        return snippet;
    }

    private static string FormatBody(string extension, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        if (extension is ".xlsx" or ".xls" or ".csv" or ".tsv")
        {
            return FormatTablePreview(body);
        }

        if (extension is ".pptx" or ".ppt")
        {
            return FormatPresentationPreview(body);
        }

        if (extension is ".docx" or ".doc" or ".hwp" or ".hwpx")
        {
            return FormatDocumentPreview(body);
        }

        return body;
    }

    private static string FormatDocumentPreview(string body)
    {
        var lines = body.Split('\n');
        var blocks = new List<string>();
        var paragraph = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(blocks, paragraph);
                continue;
            }

            if (line == "..." || line.StartsWith("[", StringComparison.Ordinal))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(line);
                continue;
            }

            if (LooksLikeHeading(line))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(line.ToUpperInvariant());
                continue;
            }

            paragraph.Add(line);
        }

        FlushParagraph(blocks, paragraph);
        return string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }

    private static string FormatPresentationPreview(string body)
    {
        var lines = body.Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var sections = new List<string>();
        const int chunkSize = 5;
        for (var i = 0; i < lines.Count; i += chunkSize)
        {
            var chunk = lines.Skip(i).Take(chunkSize).ToList();
            var builder = new List<string> { $"[슬라이드 {sections.Count + 1}]" };
            builder.AddRange(chunk.Select(static line => $"• {line}"));
            sections.Add(string.Join(Environment.NewLine, builder));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string FormatTablePreview(string body)
    {
        var sections = body.Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var formattedSections = new List<string>();

        foreach (var section in sections)
        {
            var lines = section.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim())
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            var formattedLines = new List<string>();
            var startIndex = 0;

            if (lines[0].StartsWith("[Sheet]", StringComparison.OrdinalIgnoreCase))
            {
                formattedLines.Add(lines[0]);
                startIndex = 1;
            }

            var rows = lines.Skip(startIndex)
                .Select(static line => line.Split(" | ", StringSplitOptions.None)
                    .Select(static cell => cell.Trim())
                    .ToArray())
                .Where(static row => row.Length > 0)
                .ToList();

            if (rows.Count == 0)
            {
                formattedSections.Add(string.Join(Environment.NewLine, formattedLines));
                continue;
            }

            var columnCount = rows.Max(static row => row.Length);
            var widths = new int[columnCount];
            foreach (var row in rows)
            {
                for (var i = 0; i < row.Length; i++)
                {
                    widths[i] = Math.Max(widths[i], row[i].Length);
                }
            }

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                var paddedCells = new List<string>();
                for (var i = 0; i < columnCount; i++)
                {
                    var value = i < row.Length ? row[i] : string.Empty;
                    paddedCells.Add(value.PadRight(widths[i]));
                }

                formattedLines.Add("| " + string.Join(" | ", paddedCells) + " |");
                if (rowIndex == 0)
                {
                    formattedLines.Add("|-" + string.Join("-|-", widths.Select(static width => new string('-', Math.Max(1, width)))) + "-|");
                }
            }

            formattedSections.Add(string.Join(Environment.NewLine, formattedLines));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, formattedSections);
    }

    private static void FlushParagraph(List<string> blocks, List<string> paragraph)
    {
        if (paragraph.Count == 0)
        {
            return;
        }

        blocks.Add(string.Join(Environment.NewLine, paragraph));
        paragraph.Clear();
    }

    private static bool LooksLikeHeading(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > 42)
        {
            return false;
        }

        if (line.EndsWith(":", StringComparison.Ordinal) || line.EndsWith("]", StringComparison.Ordinal))
        {
            return true;
        }

        if (char.IsDigit(line[0]))
        {
            return true;
        }

        return line.StartsWith("•", StringComparison.Ordinal) ||
               line.StartsWith("-", StringComparison.Ordinal) ||
               line.StartsWith("*", StringComparison.Ordinal);
    }

    private static string GetTypeLabel(string extension)
    {
        return extension switch
        {
            ".docx" or ".doc" => "워드 문서",
            ".xlsx" or ".xls" => "엑셀 문서",
            ".pptx" or ".ppt" => "파워포인트 문서",
            ".hwp" or ".hwpx" => "한글 문서",
            ".csv" or ".tsv" => "데이터 문서",
            ".txt" or ".md" => "텍스트 문서",
            ".json" or ".xml" or ".yaml" or ".yml" => "구조화 텍스트",
            _ => $"문서 {extension}"
        };
    }
}
