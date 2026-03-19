using System.Diagnostics;
using System.Reflection;
using DocumentExplorerApp;

var rootFolder = args.Length > 0
    ? args[0]
    : throw new InvalidOperationException("대상 폴더 경로가 필요해.");

if (!Directory.Exists(rootFolder))
{
    throw new DirectoryNotFoundException(rootFolder);
}

var patterns = DocumentSearcher.DefaultPatterns
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var findFilesMethod = typeof(DocumentSearcher).GetMethod(
    "FindFiles",
    BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException("FindFiles");

var fileNameStopwatch = Stopwatch.StartNew();
var files = (List<string>)findFilesMethod.Invoke(null, [rootFolder, patterns])!;
fileNameStopwatch.Stop();

var extensionTop = files
    .GroupBy(path => Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
    .Select(group => new { Extension = string.IsNullOrWhiteSpace(group.Key) ? "(none)" : group.Key.ToLowerInvariant(), Count = group.Count() })
    .OrderByDescending(item => item.Count)
    .Take(15)
    .ToArray();

var contentResults = new List<SearchResult>();
var progress = new Progress<SearchProgress>(item =>
{
    if (item.Result is not null)
    {
        contentResults.Add(item.Result);
    }
});

var contentStopwatch = Stopwatch.StartNew();
var searcher = new DocumentSearcher();
searcher.Search(
    rootFolder,
    string.Empty,
    patterns,
    SearchTarget.DocumentContent,
    progress,
    CancellationToken.None);
contentStopwatch.Stop();

var successCount = contentResults.Count(item => item.Status == "성공");
var failureCount = contentResults.Count(item => item.Status == "실패");

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"대상 폴더: {rootFolder}");
Console.WriteLine($"대상 파일 수: {files.Count}");
Console.WriteLine($"파일명 인덱싱 시간: {fileNameStopwatch.Elapsed.TotalSeconds:F3}초");
Console.WriteLine($"문서 내용 인덱싱 시간: {contentStopwatch.Elapsed.TotalSeconds:F3}초");
Console.WriteLine($"본문 추출 성공: {successCount}");
Console.WriteLine($"본문 추출 실패: {failureCount}");
Console.WriteLine("확장자 상위 15개:");

foreach (var item in extensionTop)
{
    Console.WriteLine($"- {item.Extension}: {item.Count}");
}
