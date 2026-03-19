using System.Text;

namespace DocumentExplorerApp;

internal static class Program
{
    private static readonly string StartupLogPath = Path.Combine(AppContext.BaseDirectory, "startup.log");

    [STAThread]
    private static void Main()
    {
        try
        {
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

            ApplicationConfiguration.Initialize();
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
            File.AppendAllText(StartupLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}
