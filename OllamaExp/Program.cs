using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

var modelName = "qwen2.5-coder:7b";

using var http = new HttpClient
{
    BaseAddress = new Uri("http://localhost:11434"),
    Timeout = TimeSpan.FromMilliseconds(800)
};

if (!await CanConnect(http))
{
    Console.WriteLine("Ollama not running. Attempting to start...");
    var launchAttempted = StartOllama();

    if (!launchAttempted)
    {
        Console.WriteLine("Could not locate or launch Ollama.");
        return;
    }

    var started = await WaitForOllama(http);

    if (!started)
    {
        Console.WriteLine("Ollama did not become available.");
        return;
    }
}

var tags = await http.GetFromJsonAsync<TagsResponse>("/api/tags");

var exists = tags?.Models.Any(m =>
    string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase)) ?? false;

Console.WriteLine(exists
    ? $"Model '{modelName}' is installed."
    : $"Model '{modelName}' is NOT installed.");

static async Task<bool> CanConnect(HttpClient client)
{
    try
    {
        using var response = await client.GetAsync("/api/tags");
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}

static bool StartOllama()
{
    try
    {
        var localAppPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Ollama",
            "ollama.exe");

        if (File.Exists(localAppPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = localAppPath,
                UseShellExecute = true
            });

            return true;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "ollama",
            UseShellExecute = true
        });

        return true;
    }
    catch
    {
        return false;
    }
}

static async Task<bool> WaitForOllama(HttpClient client)
{
    // quick checks first
    int[] delaysMs = [150, 250, 400, 600, 1000, 1500];

    foreach (var delay in delaysMs)
    {
        if (await CanConnect(client))
            return true;

        await Task.Delay(delay);
    }

    return false;
}

public class TagsResponse
{
    [JsonPropertyName("models")]
    public List<ModelInfo> Models { get; set; } = new();
}

public class ModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}