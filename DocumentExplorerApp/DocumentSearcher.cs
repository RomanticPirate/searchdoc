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

    // COM 앱 재사용을 위한 ProgID 매핑 (파일마다 앱 새로 만들지 않음)
    private static readonly Dictionary<string, string> ComProgIds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".doc"] = "Word.Application",
        [".xls"] = "Excel.Application",
        [".ppt"] = "PowerPoint.Application",
        [".hwp"] = "HWPFrame.HwpObject",
    };

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

    public DocumentIndexData BuildIndex(
        string rootFolder,
        IReadOnlyList<string> patterns,
        IProgress<IndexingProgress> progress,
        CancellationToken cancellationToken)
    {
        var files = FindFiles(rootFolder, patterns, cancellationToken);
        var entries = new List<DocumentIndexEntry>(files.Count);
        progress.Report(new IndexingProgress(0, files.Count, string.Empty, false));

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[i];
            var fileInfo = new FileInfo(path);
            var extension = Path.GetExtension(path);
            var status = "성공";
            var content = string.Empty;

            try
            {
                if (_extractors.TryGetValue(extension, out var extractor))
                {
                    content = extractor(path).Text;
                }
                else
                {
                    status = "실패";
                    content = "본문 추출을 지원하지 않는 형식입니다.";
                }
            }
            catch (Exception ex)
            {
                status = "실패";
                content = CreateFailureMessage(path, ex);
            }

            entries.Add(new DocumentIndexEntry
            {
                Path = path,
                LastWriteUtcTicks = fileInfo.Exists ? fileInfo.LastWriteTimeUtc.Ticks : 0,
                FileLength = fileInfo.Exists ? fileInfo.Length : 0,
                Status = status,
                Content = content,
            });

            progress.Report(new IndexingProgress(i + 1, files.Count, path, false));
        }

        progress.Report(new IndexingProgress(files.Count, files.Count, string.Empty, true));

        return new DocumentIndexData
        {
            FormatVersion = DocumentIndexData.CurrentFormatVersion,
            RootFolder = rootFolder,
            Patterns = patterns.Select(static item => item.Trim()).Where(static item => !string.IsNullOrWhiteSpace(item)).ToList(),
            IndexedAtUtc = DateTimeOffset.UtcNow,
            Entries = entries,
        };
    }

    public DocumentIndexData BuildOrUpdateIndex(
        IReadOnlyList<string> rootFolders,
        IReadOnlyList<string> patterns,
        DocumentIndexData? existingIndex,
        IProgress<IndexingProgress> progress,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        foreach (var folder in rootFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(folder))
                files.AddRange(FindFiles(folder, patterns, cancellationToken));
        }

        var existingEntries = existingIndex?.Entries.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, DocumentIndexEntry>(StringComparer.OrdinalIgnoreCase);

        // 결과 배열 (인덱스 위치 유지)
        var entries = new DocumentIndexEntry?[files.Count];

        // 변경된 파일만 추출 대상, COM 필요 파일은 별도 분류
        var nonComPending = new List<(int Index, string Path, FileInfo Info)>();
        var comPending = new Dictionary<string, List<(int Index, string Path, FileInfo Info)>>(StringComparer.OrdinalIgnoreCase);

        progress.Report(new IndexingProgress(0, files.Count, string.Empty, false));

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[i];
            var fileInfo = new FileInfo(path);

            if (existingEntries.TryGetValue(path, out var existingEntry) &&
                existingEntry.LastWriteUtcTicks == fileInfo.LastWriteTimeUtc.Ticks &&
                existingEntry.FileLength == fileInfo.Length)
            {
                entries[i] = existingEntry;
                progress.Report(new IndexingProgress(i + 1, files.Count, path, false));
                continue;
            }

            var ext = Path.GetExtension(path);
            if (ComProgIds.TryGetValue(ext, out var progId))
            {
                if (!comPending.TryGetValue(progId, out var list))
                {
                    list = [];
                    comPending[progId] = list;
                }
                list.Add((i, path, fileInfo));
            }
            else
            {
                nonComPending.Add((i, path, fileInfo));
            }
        }

        // 비-COM 파일 먼저 처리 (ZIP 기반 docx/xlsx/pptx, 텍스트 등)
        var processed = entries.Count(static e => e is not null);
        foreach (var (idx, path, info) in nonComPending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries[idx] = BuildIndexEntry(path, info, cancellationToken);
            processed++;
            progress.Report(new IndexingProgress(processed, files.Count, path, false));
        }

        // COM 파일: ProgID별로 앱 한 번만 열어서 배치 처리
        foreach (var (progId, fileList) in comPending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessComBatch(progId, fileList, entries, files.Count, ref processed, progress, cancellationToken);
        }

        progress.Report(new IndexingProgress(files.Count, files.Count, string.Empty, true));

        var cleanedPatterns = patterns.Select(static item => item.Trim()).Where(static item => !string.IsNullOrWhiteSpace(item)).ToList();
        return new DocumentIndexData
        {
            FormatVersion = DocumentIndexData.CurrentFormatVersion,
            RootFolder = rootFolders.Count > 0 ? rootFolders[0] : string.Empty,
            RootFolders = rootFolders.ToList(),
            Patterns = cleanedPatterns,
            IndexedAtUtc = DateTimeOffset.UtcNow,
            Entries = entries.Select(static e => e!).ToList(),
        };
    }

    /// <summary>COM 앱 하나로 같은 타입 파일들을 배치 처리 (메모리 절약)</summary>
    private void ProcessComBatch(
        string progId,
        List<(int Index, string Path, FileInfo Info)> fileList,
        DocumentIndexEntry?[] entries,
        int totalFiles,
        ref int processed,
        IProgress<IndexingProgress> progress,
        CancellationToken cancellationToken)
    {
        object? app = null;
        var filesProcessedInBatch = 0;
        try
        {
            var type = Type.GetTypeFromProgID(progId);
            if (type is not null)
                app = Activator.CreateInstance(type);

            if (app is not null)
            {
                // 앱 초기 설정
                InitializeComApp(progId, app);
            }

            foreach (var (idx, path, info) in fileList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (app is null)
                {
                    entries[idx] = MakeFailureEntry(path, info, $"{progId}를 찾지 못했어.");
                }
                else
                {
                    try
                    {
                        var result = ExtractWithComApp(progId, app, path);
                        entries[idx] = new DocumentIndexEntry
                        {
                            Path = path,
                            LastWriteUtcTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
                            FileLength = info.Exists ? info.Length : 0,
                            Status = "성공",
                            Content = result.Text,
                        };
                    }
                    catch (Exception ex)
                    {
                        entries[idx] = MakeFailureEntry(path, info, CreateFailureMessage(path, ex));
                    }
                }

                filesProcessedInBatch++;
                processed++;
                progress.Report(new IndexingProgress(processed, totalFiles, path, false));

                // COM 앱이 50파일마다 메모리 누적되면 재시작
                if (app is not null && filesProcessedInBatch % 50 == 0)
                {
                    ShutdownComApp(progId, app);
                    ReleaseComObject(app);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    app = Activator.CreateInstance(type!);
                    if (app is not null) InitializeComApp(progId, app);
                }
            }
        }
        finally
        {
            if (app is not null)
            {
                ShutdownComApp(progId, app);
                ReleaseComObject(app);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static void InitializeComApp(string progId, object app)
    {
        if (progId == "Word.Application")
        {
            SetProperty(app, "Visible", false);
            SetProperty(app, "DisplayAlerts", 0);
        }
        else if (progId == "Excel.Application")
        {
            SetProperty(app, "Visible", false);
            SetProperty(app, "DisplayAlerts", false);
        }
        else if (progId == "HWPFrame.HwpObject")
        {
            TryInvoke(app, "RegisterModule", "FilePathCheckDLL", "FilePathCheckerModule");
        }
        // PowerPoint.Application은 별도 초기화 불필요
    }

    private static void ShutdownComApp(string progId, object app)
    {
        if (progId == "HWPFrame.HwpObject")
        {
            TryInvoke(app, "Quit");
        }
        else
        {
            TryInvoke(app, "Quit");
        }
    }

    private static ExtractionResult ExtractWithComApp(string progId, object app, string path)
    {
        if (progId == "Word.Application") return ExtractWordDocument(app, path);
        if (progId == "Excel.Application") return ExtractExcelWorkbook(app, path);
        if (progId == "PowerPoint.Application") return ExtractPowerPointPresentation(app, path);
        if (progId == "HWPFrame.HwpObject") return ExtractHwpDocument(app, path);
        throw new InvalidOperationException($"알 수 없는 COM ProgID: {progId}");
    }

    private static ExtractionResult ExtractWordDocument(object app, string path)
    {
        object? documents = null;
        object? document = null;
        try
        {
            documents = GetProperty(app, "Documents");
            document = Invoke(documents, "Open", path, Missing.Value, true);
            var content = GetProperty(document, "Content");
            var text = GetProperty(content, "Text")?.ToString() ?? string.Empty;
            ReleaseComObject(content);
            return new ExtractionResult(NormalizeText(text), "word-com");
        }
        finally
        {
            if (document is not null) { TryInvoke(document, "Close", false); ReleaseComObject(document); }
            ReleaseComObject(documents);
        }
    }

    private static ExtractionResult ExtractExcelWorkbook(object app, string path)
    {
        object? workbooks = null;
        object? workbook = null;
        object? worksheets = null;
        try
        {
            workbooks = GetProperty(app, "Workbooks");
            workbook = Invoke(workbooks, "Open", path, Missing.Value, true);
            var parts = new List<string>();

            worksheets = GetProperty(workbook, "Worksheets");
            var count = Convert.ToInt32(GetProperty(worksheets, "Count"));

            for (var i = 1; i <= count; i++)
            {
                var sheet = GetProperty(worksheets, "Item", i);
                try
                {
                    var lines = new List<string> { $"[Sheet] {GetProperty(sheet, "Name")}" };
                    var usedRange = GetProperty(sheet, "UsedRange");
                    try
                    {
                        var values = GetProperty(usedRange, "Value");
                        lines.AddRange(FlattenComMatrix(values));
                        ReleaseComObject(values);
                    }
                    finally
                    {
                        ReleaseComObject(usedRange);
                    }

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

            return new ExtractionResult(NormalizeText(string.Join(Environment.NewLine + Environment.NewLine, parts)), "excel-com");
        }
        finally
        {
            ReleaseComObject(worksheets);
            if (workbook is not null) { TryInvoke(workbook, "Close", false); ReleaseComObject(workbook); }
            ReleaseComObject(workbooks);
        }
    }

    private static ExtractionResult ExtractPowerPointPresentation(object app, string path)
    {
        object? presentations = null;
        object? presentation = null;
        var parts = new List<string>();

        try
        {
            presentations = GetProperty(app, "Presentations");
            presentation = Invoke(presentations, "Open", path, false, false, false);
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
                            if (!hasTextFrame) continue;

                            var textFrame = GetProperty(shape, "TextFrame");
                            try
                            {
                                var hasText = Convert.ToBoolean(GetProperty(textFrame, "HasText"));
                                if (!hasText) continue;

                                var textRange = GetProperty(textFrame, "TextRange");
                                try
                                {
                                    var text = GetProperty(textRange, "Text")?.ToString();
                                    if (!string.IsNullOrWhiteSpace(text))
                                        lines.Add(text.Trim());
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
                        parts.Add(string.Join(Environment.NewLine, lines));
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
            if (presentation is not null) { TryInvoke(presentation, "Close"); ReleaseComObject(presentation); }
            ReleaseComObject(presentations);
        }
    }

    private static ExtractionResult ExtractHwpDocument(object app, string path)
    {
        ReleaseComObject(Invoke(app, "Open", path));
        ReleaseComObject(Invoke(app, "InitScan"));

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
                        lines.Add(text.Trim());

                    if (state is 0 or 1) break;
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
        }
    }

    private static DocumentIndexEntry MakeFailureEntry(string path, FileInfo info, string message)
    {
        return new DocumentIndexEntry
        {
            Path = path,
            LastWriteUtcTicks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0,
            FileLength = info.Exists ? info.Length : 0,
            Status = "실패",
            Content = message,
        };
    }

    public void Search(
        DocumentIndexData index,
        string keyword,
        SearchTarget searchTarget,
        IProgress<SearchProgress> progress,
        CancellationToken cancellationToken)
    {
        var entries = index.Entries;
        progress.Report(new SearchProgress(0, entries.Count, string.Empty, null, false));

        var trimmedKeyword = keyword.Trim();

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];
            SearchResult? result = null;
            var fileName = Path.GetFileName(entry.Path);

            if (searchTarget == SearchTarget.FileName)
            {
                if (string.IsNullOrWhiteSpace(trimmedKeyword) ||
                    MatchesAnyToken(fileName, trimmedKeyword))
                {
                    var snippet = entry.Status == "실패"
                        ? entry.Content
                        : $"파일명 일치: {fileName}{Environment.NewLine}경로: {Path.GetDirectoryName(entry.Path)}";
                    result = new SearchResult(entry.Path, snippet, entry.Status);
                }

                progress.Report(new SearchProgress(i + 1, entries.Count, entry.Path, result, false));
                continue;
            }

            if (entry.Status == "실패")
            {
                if (string.IsNullOrWhiteSpace(trimmedKeyword))
                {
                    result = new SearchResult(entry.Path, entry.Content, "실패");
                }
            }
            else if (string.IsNullOrWhiteSpace(trimmedKeyword) ||
                     MatchesAnyToken(entry.Content, trimmedKeyword))
            {
                result = new SearchResult(
                    entry.Path,
                    CreateSnippet(entry.Content, trimmedKeyword),
                    "성공");
            }

            progress.Report(new SearchProgress(i + 1, entries.Count, entry.Path, result, false));
        }

        progress.Report(new SearchProgress(entries.Count, entries.Count, string.Empty, null, true));
    }

    public void Search(
        string rootFolder,
        string keyword,
        IReadOnlyList<string> patterns,
        SearchTarget searchTarget,
        IProgress<SearchProgress> progress,
        CancellationToken cancellationToken)
    {
        var files = FindFiles(rootFolder, patterns, cancellationToken);
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
                    MatchesAnyToken(fileName, trimmedKeyword))
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
                        MatchesAnyToken(extraction.Text, trimmedKeyword))
                    {
                        result = new SearchResult(
                            path,
                            CreateSnippet(extraction.Text, trimmedKeyword),
                            "성공");
                    }
                }
                catch (Exception ex)
                {
                    result = new SearchResult(path, CreateFailureMessage(path, ex), "실패");
                }
            }

            progress.Report(new SearchProgress(i + 1, files.Count, path, result, false));
        }

        progress.Report(new SearchProgress(files.Count, files.Count, string.Empty, null, true));
    }

    private static string CreateFailureMessage(string path, Exception ex)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var message = ex.Message;

        if (extension is ".doc" or ".xls" or ".ppt" or ".hwp")
        {
            return "본문 추출에 실패했습니다.\n해당 형식을 여는 프로그램이 설치되어 있는지 먼저 확인해 주세요.";
        }

        if (extension is ".txt" or ".md" or ".csv" or ".tsv" or ".json" or ".xml" or ".log" or ".ini" or ".cfg" or ".yaml" or ".yml" or ".html" or ".htm" or ".css" or ".js" or ".ts" or ".py" or ".cs" or ".java" or ".sql")
        {
            return "텍스트 파일을 읽지 못했어.\n인코딩이 다르거나 파일이 손상됐을 수 있어.";
        }

        if (message.Contains("workbook.xml", StringComparison.OrdinalIgnoreCase))
        {
            return "엑셀 파일 구조를 읽지 못했어.\n파일이 손상됐거나 내부 형식이 올바르지 않을 수 있어.";
        }

        if (message.Contains("Open", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("COM", StringComparison.OrdinalIgnoreCase))
        {
            return "문서를 여는 중 오류가 났어.\n파일이 잠겨 있거나 해당 프로그램 자동화에 실패했을 수 있어.";
        }

            return "본문 추출에 실패했습니다.\n파일이 잠겨 있거나 손상되었을 수 있습니다.";
    }

    private DocumentIndexEntry BuildIndexEntry(string path, FileInfo fileInfo, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path);
        var status = "성공";
        var content = string.Empty;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_extractors.TryGetValue(extension, out var extractor))
            {
                content = extractor(path).Text;
            }
            else
            {
                status = "실패";
                content = "본문 추출을 지원하지 않는 형식입니다.";
            }
        }
        catch (Exception ex)
        {
            status = "실패";
            content = CreateFailureMessage(path, ex);
        }

        return new DocumentIndexEntry
        {
            Path = path,
            LastWriteUtcTicks = fileInfo.Exists ? fileInfo.LastWriteTimeUtc.Ticks : 0,
            FileLength = fileInfo.Exists ? fileInfo.Length : 0,
            Status = status,
            Content = content,
        };
    }

    private static List<string> FindFiles(string rootFolder, IReadOnlyList<string> patterns, CancellationToken cancellationToken = default)
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
            cancellationToken.ThrowIfCancellationRequested();
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
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(file).ToLowerInvariant();
                    if (name.StartsWith("~$", StringComparison.Ordinal))
                    {
                        continue;
                    }

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

    public static string[] SplitTokens(string keyword)
    {
        return keyword.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    // 띄어쓰기로 나뉜 단어 중 하나라도 포함되면 일치 (OR 검색)
    public static bool MatchesAnyToken(string text, string keyword)
    {
        foreach (var token in SplitTokens(keyword))
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static int FirstTokenIndex(string text, string keyword)
    {
        var best = -1;
        foreach (var token in SplitTokens(keyword))
        {
            var i = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && (best < 0 || i < best))
            {
                best = i;
            }
        }

        return best;
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

        var index = FirstTokenIndex(text, keyword);
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
            "zip-xml",
            ExtractWordTextFromXml);
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

    private static ExtractionResult ExtractZipXml(
        string path,
        Func<ZipArchiveEntry, bool> includeEntry,
        string engine,
        Func<string, string>? textExtractor = null)
    {
        var parts = new List<string>();
        textExtractor ??= ExtractTextFromXml;

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
            var text = textExtractor(xml);
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

    private static string ExtractWordTextFromXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var values = doc.DescendantNodes()
                .OfType<XText>()
                .Where(static node => !IsWordFieldCodeText(node))
                .Select(static node => node.Value.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value));
            return NormalizeText(string.Join(Environment.NewLine, values));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsWordFieldCodeText(XText node)
    {
        var parent = node.Parent;
        if (parent is null)
        {
            return false;
        }

        if (parent.Name.LocalName is "instrText" or "delInstrText")
        {
            return true;
        }

        return parent.Ancestors().Any(static ancestor =>
            ancestor.Name.LocalName is "fldSimple" or "instrText" or "delInstrText");
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
            object? documents = null;
            object? document = null;
            try
            {
                documents = GetProperty(app, "Documents");
                document = Invoke(documents, "Open", path, Missing.Value, true);
                var content = GetProperty(document, "Content");
                var text = GetProperty(content, "Text")?.ToString() ?? string.Empty;
                ReleaseComObject(content);
                return new ExtractionResult(NormalizeText(text), "word-com");
            }
            finally
            {
                if (document is not null) { TryInvoke(document, "Close", false); ReleaseComObject(document); }
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
            object? workbooks = null;
            object? workbook = null;
            object? worksheets = null;
            try
            {
                workbooks = GetProperty(app, "Workbooks");
                workbook = Invoke(workbooks, "Open", path, Missing.Value, true);
                var parts = new List<string>();

                worksheets = GetProperty(workbook, "Worksheets");
                var count = Convert.ToInt32(GetProperty(worksheets, "Count"));

                for (var i = 1; i <= count; i++)
                {
                    var sheet = GetProperty(worksheets, "Item", i);
                    try
                    {
                        var lines = new List<string> { $"[Sheet] {GetProperty(sheet, "Name")}" };
                        var usedRange = GetProperty(sheet, "UsedRange");
                        try
                        {
                            var values = GetProperty(usedRange, "Value");
                            lines.AddRange(FlattenComMatrix(values));
                            ReleaseComObject(values);
                        }
                        finally
                        {
                            ReleaseComObject(usedRange);
                        }

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

                return new ExtractionResult(NormalizeText(string.Join(Environment.NewLine + Environment.NewLine, parts)), "excel-com");
            }
            finally
            {
                ReleaseComObject(worksheets);
                if (workbook is not null) { TryInvoke(workbook, "Close", false); ReleaseComObject(workbook); }
                ReleaseComObject(workbooks);
            }
        });
    }

    private static ExtractionResult ExtractWithPowerPoint(string path)
    {
        return RunComExtraction("PowerPoint.Application", app =>
        {
            object? presentations = null;
            object? presentation = null;
            var parts = new List<string>();

            try
            {
                presentations = GetProperty(app, "Presentations");
                presentation = Invoke(presentations, "Open", path, false, false, false);
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
                if (presentation is not null) { TryInvoke(presentation, "Close"); ReleaseComObject(presentation); }
                ReleaseComObject(presentations);
            }
        });
    }

    private static ExtractionResult ExtractWithHwp(string path)
    {
        return RunComExtraction("HWPFrame.HwpObject", app =>
        {
            ReleaseComObject(Invoke(app, "RegisterModule", "FilePathCheckDLL", "FilePathCheckerModule"));
            ReleaseComObject(Invoke(app, "Open", path));
            ReleaseComObject(Invoke(app, "InitScan"));

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
            // COM RCW 잔여 참조 확실히 정리
            GC.Collect();
            GC.WaitForPendingFinalizers();
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
