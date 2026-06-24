using System.Text.Json;

namespace ForecastDesk;

public sealed class DataStore
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = ForecastJsonContext.Default
    };

    public DataStore()
    {
        Directory.CreateDirectory(AppDirectory);
        Directory.CreateDirectory(ScreenshotsDirectory);
        Directory.CreateDirectory(WebViewProfileDirectory);
    }

    public string AppDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ForecastDesk");

    public string ScreenshotsDirectory => Path.Combine(AppDirectory, "screenshots");

    public string WebViewProfileDirectory => Path.Combine(AppDirectory, "webview-profile");

    private string DataFile => Path.Combine(AppDirectory, "forecast-desk.json");

    public AppData Load()
    {
        if (!File.Exists(DataFile))
        {
            return new AppData();
        }

        try
        {
            var json = File.ReadAllText(DataFile);
            return JsonSerializer.Deserialize(json, ForecastJsonContext.Default.AppData) ?? new AppData();
        }
        catch
        {
            var backup = Path.Combine(AppDirectory, $"forecast-desk-broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(DataFile, backup, overwrite: true);
            return new AppData();
        }
    }

    public void Save(AppData data)
    {
        var json = JsonSerializer.Serialize(data, ForecastJsonContext.Default.AppData);
        File.WriteAllText(DataFile, json);
    }

    public string BuildScreenshotPath(string forecastId, string phase)
    {
        var fileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{forecastId}-{phase}.png";
        return Path.Combine(ScreenshotsDirectory, fileName);
    }
}
