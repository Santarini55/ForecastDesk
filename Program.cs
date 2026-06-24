namespace ForecastDesk;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => LogCrash(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogCrash(exception);
            }
        };

        try
        {
            Application.Run(new Form1());
        }
        catch (Exception exception)
        {
            LogCrash(exception);
            MessageBox.Show(
                exception.Message,
                "Forecast Desk не смог запуститься",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }    

    private static void LogCrash(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ForecastDesk");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // Last-resort crash logging must not throw another exception.
        }
    }
}
