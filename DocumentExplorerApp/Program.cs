using System.Text;

namespace DocumentExplorerApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            AppPaths.EnsureAppDataDirectory();
            Log("Main start");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, args) =>
            {
                Log($"ThreadException: {args.Exception}");
                MessageBox.Show(args.Exception.ToString(), "찾아줘문서 실행 오류");
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                Log($"UnhandledException: {args.ExceptionObject}");
            };

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Log("ApplicationConfiguration initialized");

            using var form = new MainForm();
            Log("MainForm created");
            Application.Run(form);
            Log("Application.Run exited");
        }
        catch (Exception ex)
        {
            Log($"Fatal: {ex}");
            MessageBox.Show(ex.ToString(), "찾아줘문서 실행 오류");
        }
    }

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(AppPaths.StartupLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}
