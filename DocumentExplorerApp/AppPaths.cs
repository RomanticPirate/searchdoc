namespace DocumentExplorerApp;

internal static class AppPaths
{
    private static readonly string AppDataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SearchDoc");

    public static string StartupLogPath => Path.Combine(AppDataDirectory, "startup.log");

    public static string SettingsPath => Path.Combine(AppDataDirectory, "document_search_settings.json");

    public static string IndexPath => Path.Combine(AppDataDirectory, "document_search_index_v3.json");

    public static void EnsureAppDataDirectory()
    {
        Directory.CreateDirectory(AppDataDirectory);
    }
}
