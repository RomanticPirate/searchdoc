using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DocumentExplorerApp;

public sealed class DocumentSearcher
{
    public const string DefaultPatterns =
        "docx, doc, xlsx, xls, pptx, ppt, hwpx, hwp, txt, md, csv, tsv, json, xml, log, ini, cfg, yaml, yml, html, htm, css, js, ts, py, cs, java, sql";
    private const int MaxPreviewChars = 800;

    private readonly Dictionary<string, Func<string, ExtractionResult>> _extractors;

    public DocumentSearcher()
    {
        _extractors = new Dictionary<string, Func<string, ExtractionResult>>(StringComparer.OrdinalIgnoreCase)
        {
            [".docx"] = ExtractDocx,
            [".xlsx"] = ExtractXlsx,
            [".pptx"] = ExtractPptx,
            [".hwpx"] = ExtractHwpx,
            [".doc"] = ExtractWithWord,
            [".xls"] = ExtractWithExcel,
            [".ppt"] = ExtractWithPowerPoint,
            [".hwp"] = ExtractWithHwp,
            [".txt"] = ExtractTextFile,
            [".md"] = ExtractTextFile,
            [".csv"] = ExtractTextFile,
            [".tsv"] = ExtractTextFile,
            [".json"] = ExtractTextFile,
            [".xml"] = ExtractTextFile,
            [".log"] = ExtractTextFile,
            [".ini"] = ExtractTextFile,
            [".cfg"] = ExtractTextFile,
            [".yaml"] = ExtractTextFile,
            [".yml"] = ExtractTextFile,
            [".html"] = ExtractTextFile,
            [".htm"] = ExtractTextFile,
            [".css"] = ExtractTextFile,
            [".js"] = ExtractTextFile,
            [".ts"] = ExtractTextFile,
            [".py"] = ExtractTextFile,
            [".cs"] = ExtractTextFile,
            [".java"] = ExtractTextFile,
            [".sql"] = ExtractTextFile,
        };
    }

    public void Search(
        string rootFolder,
        string keyword,
        IReadOnlyList<string> patterns,
        SearchTarget searchTarget,
        IProgress<SearchProgress> progress,
        CancellationToken cancellationToken)
    {
        var files = FindFiles(rootFolder, patterns);
        progress.Report(new SearchProgress(0, files.Count, string.Empty, null, false));

        var trimmedKeyword = keyword.Trim();

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[i];
            SearchResult? result = null;
            var fileName = Path.GetFileName(path);

            if (searchTarget == SearchTarget.FileName)
            {
                if (string.IsNullOrWhiteSpace(trimmedKeyword) ||
                    fileName.Contains(trimmedKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    result = new SearchResult(
                        path,
                        $"파일명 일치: {fileName}{Environment.NewLine}경로: {Path.GetDirectoryName(path)}",
                        "성공");
                }

                progress.Report(new SearchProgress(i + 1, files.Count, path, result, false));
                continue;
            }

            if (_extractors.TryGetValue(Path.GetExtension(path), out var extractor))
            {
                try
                {
                    var extraction = extractor(path);
                    if (string.IsNullOrWhiteSpace(trimmedKeyword) ||
                        extraction.Text.Contains(trimmedKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        result = new SearchResult(
                            path,
                            CreateSnippet(extraction.Text, trimmedKeyword),
                            "성공");
                    }
                }
                catch (Exception ex)
                {
                    result = new SearchResult(path, ex.Message, "실패");
                }
            }

            progress.Report(new SearchProgress(i + 1, files.Count, path, result, false));
        }

        progress.Report(new SearchProgress(files.Count, files.Count, string.Empty, null, true));
    }

    private static List<string> FindFiles(string rootFolder, IReadOnlyList<string> patterns)
    {
        var loweredPatterns = patterns
            .Select(NormalizePatternToken)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        var matches = new List<string>();
        var pending = new Stack<string>();
        pending.Push(rootFolder);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    var name = Path.GetFileName(directory);
                    if (name is ".git" or ".venv" or "node_modules" or "__pycache__")
                    {
                        continue;
                    }

                    pending.Push(directory);
                }

                foreach (var file in Directory.EnumerateFiles(current))
                {
                    var name = Path.GetFileName(file).ToLowerInvariant();
                    if (loweredPatterns.Any(pattern => WildcardMatches(name, pattern)))
                    {
                        matches.Add(file);
                    }
                }
            }
            catch
            {
                // Skip unreadable directories.
            }
        }

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return matches;
    }

    private static string NormalizeText(string value)
    {
        var text = value.Replace("\r", "\n");
        text = Regex.Replace(text, @"[ \t\f\v]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static bool WildcardMatches(string fileName, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase);
    }

    private static string NormalizePatternToken(string pattern)
    {
        var token = pattern.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        if (token.StartsWith("*."))
        {
            return token;
        }

        if (token.StartsWith("."))
        {
            return $"*{token}";
        }

        if (token.Contains('*') || token.Contains('?'))
        {
            return token;
        }

        return $"*.{token}";
    }

    private static string CreateSnippet(string text, string keyword)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return text[..Math.Min(text.Length, MaxPreviewChars)];
        }

        var index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return text[..Math.Min(text.Length, MaxPreviewChars)];
        }

        var lines = text.Split('\n');
        var cumulativeLength = 0;
        var matchLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var lineLength = lines[i].Length + 1;
            if (index < cumulativeLength + lineLength)
            {
                matchLine = i;
                break;
            }

            cumulativeLength += lineLength;
        }

        var startLine = Math.Max(0, matchLine - 3);
        var endLine = Math.Min(lines.Length - 1, matchLine + 6);
        var snippetLines = lines[startLine..(endLine + 1)];
        var snippet = string.Join(Environment.NewLine, snippetLines);

        if (startLine > 0)
        {
            snippet = "..." + Environment.NewLine + snippet;
        }

        if (endLine < lines.Length - 1)
        {
            snippet += Environment.NewLine + "...";
        }

        if (snippet.Length > MaxPreviewChars)
        {
            snippet = snippet[..MaxPreviewChars];
        }

        return snippet;
    }

    private static ExtractionResult ExtractDocx(string path)
    {
        return ExtractZipXml(path, static entry =>
            entry.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            !entry.FullName.EndsWith("styles.xml", StringComparison.OrdinalIgnoreCase) &&
            !entry.FullName.EndsWith("settings.xml", StringComparison.OrdinalIgnoreCase),
            "zip-xml");
    }

    private static ExtractionResult ExtractPptx(string path)
    {
        return ExtractZipXml(path, static entry =>
            entry.FullName.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            (entry.FullName.Contains("/slides/", StringComparison.OrdinalIgnoreCase) ||
             entry.FullName.Contains("/notesSlides/", StringComparison.OrdinalIgnoreCase)),
            "zip-xml");
    }

    private static ExtractionResult ExtractHwpx(string path)
    {
        return ExtractZipXml(path, static entry =>
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            (entry.FullName.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) ||
             entry.FullName.StartsWith("Preview/", StringComparison.OrdinalIgnoreCase)),
            "zip-xml");
    }

    private static ExtractionResult ExtractZipXml(string path, Func<ZipArchiveEntry, bool> includeEntry, string engine)
    {
        var parts = new List<string>();

        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries.OrderBy(static item => item.FullName, StringComparer.OrdinalIgnoreCase))
        {
            if (!includeEntry(entry))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8, true);
            var xml = reader.ReadToEnd();
            var text = ExtractTextFromXml(xml);
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return new ExtractionResult(NormalizeText(string.Join(Environment.NewLine + Environment.NewLine, parts)), engine);
    }

    private static string ExtractTextFromXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var values = doc.DescendantNodes()
                .OfType<XText>()
                .Select(static node => node.Value.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value));
            return NormalizeText(string.Join(Environment.NewLine, values));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ExtractionResult ExtractXlsx(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var sharedStrings = LoadSharedStrings(archive);
        var relationshipMap = LoadWorkbookRelationships(archive);
        var sheetInfos = LoadSheetInfos(archive, relationshipMap);
        var parts = new List<string>();

        foreach (var info in sheetInfos)
        {
            var entry = archive.GetEntry(info.EntryPath) ?? archive.GetEntry($"xl/{info.EntryPath}");
            if (entry is null)
            {
                continue;
            }

            using var entryStream = entry.Open();
            var doc = XDocument.Load(entryStream);
            var lines = new List<string> { $"[Sheet] {info.Name}" };

            foreach (var row in doc.Descendants().Where(static item => item.Name.LocalName == "row"))
            {
                var rowValues = new List<string>();
                foreach (var cell in row.Elements().Where(static item => item.Name.LocalName == "c"))
                {
                    var value = ReadCellValue(cell, sharedStrings);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        rowValues.Add(value);
                    }
                }

                if (rowValues.Count > 0)
                {
                    lines.Add(string.Join(" | ", rowValues));
                }
            }

            if (lines.Count > 1)
            {
                parts.Add(string.Join(Environment.NewLine, lines));
            }
        }

        return new ExtractionResult(NormalizeText(string.Join(Environment.NewLine + Environment.NewLine, parts)), "zip-xlsx");
    }

    private static List<string> LoadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);

        return doc.Descendants()
            .Where(static item => item.Name.LocalName == "si")
            .Select(static item => NormalizeText(string.Join(string.Empty,
                item.Descendants().Where(static x => x.Name.LocalName == "t").Select(static x => x.Value))))
            .ToList();
    }

    private static Dictionary<string, string> LoadWorkbookRelationships(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (entry is null)
        {
            return map;
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        foreach (var relationship in doc.Descendants().Where(static item => item.Name.LocalName == "Relationship"))
        {
            var id = relationship.Attribute("Id")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(target))
            {
                map[id] = target.Replace('\\', '/');
            }
        }

        return map;
    }

    private static List<SheetInfo> LoadSheetInfos(ZipArchive archive, Dictionary<string, string> relationshipMap)
    {
        var entry = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidOperationException("workbook.xml을 찾지 못했어.");
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);

        return doc.Descendants()
            .Where(static item => item.Name.LocalName == "sheet")
            .Select(sheet =>
            {
                var name = sheet.Attribute("name")?.Value ?? "Sheet";
                var relId = sheet.Attributes().FirstOrDefault(static attr => attr.Name.LocalName == "id")?.Value ?? string.Empty;
                relationshipMap.TryGetValue(relId, out var target);
                return new SheetInfo(name, target ?? string.Empty);
            })
            .Where(static info => !string.IsNullOrWhiteSpace(info.EntryPath))
            .ToList();
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        var value = cell.Elements().FirstOrDefault(static item => item.Name.LocalName == "v")?.Value ?? string.Empty;
        var inlineText = cell.Descendants().Where(static item => item.Name.LocalName == "t").Select(static item => item.Value);

        if (type == "inlineStr")
        {
            return NormalizeText(string.Join(string.Empty, inlineText));
        }

        if (type == "s" && int.TryParse(value, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex];
        }

        if (inlineText.Any())
        {
            return NormalizeText(string.Join(string.Empty, inlineText));
        }

        return NormalizeText(value);
    }

    private static ExtractionResult ExtractTextFile(string path)
    {
        foreach (var encoding in CandidateEncodings())
        {
            try
            {
                var text = File.ReadAllText(path, encoding);
                return new ExtractionResult(NormalizeText(text), "text");
            }
            catch (DecoderFallbackException)
            {
                // Try next encoding.
            }
        }

        throw new InvalidOperationException("텍스트 파일 인코딩을 읽지 못했어.");
    }

    private static IEnumerable<Encoding> CandidateEncodings()
    {
        yield return new UTF8Encoding(false, true);
        yield return Encoding.Unicode;
        yield return Encoding.BigEndianUnicode;
        yield return Encoding.UTF32;
        yield return Encoding.GetEncoding(949);
        yield return Encoding.Default;
    }

    private static ExtractionResult ExtractWithWord(string path)
    {
        return RunComExtraction("Word.Application", app =>
        {
            SetProperty(app, "Visible", false);
            SetProperty(app, "DisplayAlerts", 0);
            var documents = GetProperty(app, "Documents");
            var document = Invoke(documents, "Open", path, Missing.Value, true);
            try
            {
                var content = GetProperty(document, "Content");
                var text = GetProperty(content, "Text")?.ToString() ?? string.Empty;
                ReleaseComObject(content);
                return new ExtractionResult(NormalizeText(text), "word-com");
            }
            finally
            {
                Invoke(document, "Close", false);
                ReleaseComObject(document);
                ReleaseComObject(documents);
            }
        });
    }

    private static ExtractionResult ExtractWithExcel(string path)
    {
        return RunComExtraction("Excel.Application", app =>
        {
            SetProperty(app, "Visible", false);
            SetProperty(app, "DisplayAlerts", false);
            var workbooks = GetProperty(app, "Workbooks");
            var workbook = Invoke(workbooks, "Open", path, Missing.Value, true);
            var parts = new List<string>();

            try
            {
                var worksheets = GetProperty(workbook, "Worksheets");
                var count = Convert.ToInt32(GetProperty(worksheets, "Count"));

                for (var i = 1; i <= count; i++)
                {
                    var sheet = GetProperty(worksheets, "Item", i);
                    try
                    {
                        var lines = new List<string> { $"[Sheet] {GetProperty(sheet, "Name")}" };
                        var usedRange = GetProperty(sheet, "UsedRange");
                        var values = GetProperty(usedRange, "Value");
                        lines.AddRange(FlattenComMatrix(values));
                        ReleaseComObject(usedRange);

                        if (lines.Count > 1)
                        {
                            parts.Add(string.Join(Environment.NewLine, lines));
                        }
                    }
                    finally
                    {
                        ReleaseComObject(sheet);
                    }
                }

                ReleaseComObject(worksheets);
                return new ExtractionResult(NormalizeText(string.Join(Environment.NewLine + Environment.NewLine, parts)), "excel-com");
            }
            finally
            {
                Invoke(workbook, "Close", false);
                ReleaseComObject(workbook);
                ReleaseComObject(workbooks);
            }
        });
    }

    private static ExtractionResult ExtractWithPowerPoint(string path)
    {
        return RunComExtraction("PowerPoint.Application", app =>
        {
            var presentations = GetProperty(app, "Presentations");
            var presentation = Invoke(presentations, "Open", path, false, false, false);
            var parts = new List<string>();

            try
            {
                var slides = GetProperty(presentation, "Slides");
                var slideCount = Convert.ToInt32(GetProperty(slides, "Count"));

                for (var i = 1; i <= slideCount; i++)
                {
                    var slide = GetProperty(slides, "Item", i);
                    try
                    {
                        var lines = new List<string> { $"[Slide {GetProperty(slide, "SlideIndex")}]" };
                        var shapes = GetProperty(slide, "Shapes");
                        var shapeCount = Convert.ToInt32(GetProperty(shapes, "Count"));

                        for (var j = 1; j <= shapeCount; j++)
                        {
                            var shape = GetProperty(shapes, "Item", j);
                            try
                            {
                                var hasTextFrame = Convert.ToBoolean(GetProperty(shape, "HasTextFrame"));
                                if (!hasTextFrame)
                                {
                                    continue;
                                }

                                var textFrame = GetProperty(shape, "TextFrame");
                                try
                                {
                                    var hasText = Convert.ToBoolean(GetProperty(textFrame, "HasText"));
                                    if (!hasText)
                                    {
                                        continue;
                                    }

                                    var textRange = GetProperty(textFrame, "TextRange");
                                    try
                                    {
                                        var text = GetProperty(textRange, "Text")?.ToString();
                                        if (!string.IsNullOrWhiteSpace(text))
                                        {
                                            lines.Add(text.Trim());
                                        }
                                    }
                                    finally
                                    {
                                        ReleaseComObject(textRange);
                                    }
                                }
                                finally
                                {
                                    ReleaseComObject(textFrame);
                                }
                            }
                            finally
                            {
                                ReleaseComObject(shape);
                            }
                        }

                        ReleaseComObject(shapes);
                        if (lines.Count > 1)
                        {
                            parts.Add(string.Join(Environment.NewLine, lines));
                        }
                    }
                    finally
                    {
                        ReleaseComObject(slide);
                    }
                }

                ReleaseComObject(slides);
                return new ExtractionResult(NormalizeText(string.Join(Environment.NewLine + Environment.NewLine, parts)), "powerpoint-com");
            }
            finally
            {
                Invoke(presentation, "Close");
                ReleaseComObject(presentation);
                ReleaseComObject(presentations);
            }
        });
    }

    private static ExtractionResult ExtractWithHwp(string path)
    {
        return RunComExtraction("HWPFrame.HwpObject", app =>
        {
            Invoke(app, "RegisterModule", "FilePathCheckDLL", "FilePathCheckerModule");
            Invoke(app, "Open", path);
            Invoke(app, "InitScan");

            var lines = new List<string>();
            try
            {
                while (true)
                {
                    var result = Invoke(app, "GetText");
                    try
                    {
                        var state = Convert.ToInt32(GetProperty(result, "Item", 0));
                        var text = GetProperty(result, "Item", 1)?.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            lines.Add(text.Trim());
                        }

                        if (state is 0 or 1)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(result);
                    }
                }

                return new ExtractionResult(NormalizeText(string.Join(Environment.NewLine, lines)), "hwp-com");
            }
            finally
            {
                TryInvoke(app, "ReleaseScan");
                TryInvoke(app, "Clear", 3);
                TryInvoke(app, "Quit");
            }
        });
    }

    private static IEnumerable<string> FlattenComMatrix(object? value)
    {
        if (value is null)
        {
            yield break;
        }

        if (value is object[,] matrix)
        {
            var rowCount = matrix.GetLength(0);
            var columnCount = matrix.GetLength(1);
            for (var row = 1; row <= rowCount; row++)
            {
                var parts = new List<string>();
                for (var col = 1; col <= columnCount; col++)
                {
                    var cell = matrix[row, col]?.ToString();
                    if (!string.IsNullOrWhiteSpace(cell))
                    {
                        parts.Add(cell.Trim());
                    }
                }

                if (parts.Count > 0)
                {
                    yield return string.Join(" | ", parts);
                }
            }

            yield break;
        }

        var text = value.ToString();
        if (!string.IsNullOrWhiteSpace(text))
        {
            yield return text.Trim();
        }
    }

    private static ExtractionResult RunComExtraction(string progId, Func<object, ExtractionResult> extractor)
    {
        object? app = null;
        try
        {
            var type = Type.GetTypeFromProgID(progId) ?? throw new InvalidOperationException($"{progId}를 찾지 못했어.");
            app = Activator.CreateInstance(type) ?? throw new InvalidOperationException($"{progId} 실행에 실패했어.");
            return extractor(app);
        }
        finally
        {
            if (app is not null)
            {
                TryInvoke(app, "Quit");
                ReleaseComObject(app);
            }
        }
    }

    private static object? GetProperty(object target, string name, params object[] index)
    {
        return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, index.Length == 0 ? null : index);
    }

    private static void SetProperty(object target, string name, object value)
    {
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);
    }

    private static object? Invoke(object target, string name, params object[] args)
    {
        return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);
    }

    private static void TryInvoke(object target, string name, params object[] args)
    {
        try
        {
            Invoke(target, name, args);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    private sealed record SheetInfo(string Name, string EntryPath);
}

public sealed record ExtractionResult(string Text, string Engine);

public sealed record SearchResult(string Path, string Snippet, string Status)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public string DirectoryPath => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}

public sealed record SearchProgress(int CurrentFile, int TotalFiles, string CurrentPath, SearchResult? Result, bool IsCompleted);

public enum SearchTarget
{
    FileName,
    DocumentContent,
}
