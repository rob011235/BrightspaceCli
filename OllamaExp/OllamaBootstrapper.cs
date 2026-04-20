namespace OllamaExp
{
    using System.Diagnostics;
    using System.Net.Http.Json;

    public sealed class OllamaBootstrapper
    {
        private readonly HttpClient _http = new()
        {
            BaseAddress = new Uri("http://localhost:11434"),
            Timeout = TimeSpan.FromMilliseconds(800)
        };

        public async Task EnsureRunningAsync()
        {
            if (await CanConnectAsync())
                return;

            Console.WriteLine("Ollama not running. Attempting to start...");
            var started = StartOllama();

            if (!started)
                throw new InvalidOperationException("Could not locate or launch Ollama.");

            var ready = await WaitForOllamaAsync();

            if (!ready)
                throw new InvalidOperationException("Ollama did not become available.");
        }

        public async Task<bool> IsModelInstalledAsync(string modelName)
        {
            var tags = await _http.GetFromJsonAsync<TagsResponse>("/api/tags");

            return tags?.Models.Any(m =>
                string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

        private async Task<bool> CanConnectAsync()
        {
            try
            {
                using var response = await _http.GetAsync("/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static bool StartOllama()
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

        private async Task<bool> WaitForOllamaAsync()
        {
            int[] delaysMs = [150, 250, 400, 600, 1000, 1500];

            foreach (var delay in delaysMs)
            {
                if (await CanConnectAsync())
                    return true;

                await Task.Delay(delay);
            }

            return false;
        }
    }
}