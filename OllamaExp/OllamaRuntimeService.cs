namespace OllamaExp
{
    using System.Diagnostics;
    using System.Net.Http.Json;

    public sealed class OllamaRuntimeService
    {
        private readonly HttpClient _http = new()
        {
            BaseAddress = new Uri("http://localhost:11434"),
            Timeout = TimeSpan.FromMilliseconds(800)
        };

        public async Task<bool> IsServerRunningAsync()
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

        public async Task<bool> EnsureRunningAsync(string ollamaLaunchPath)
        {
            if (await IsServerRunningAsync())
                return true;

            Console.WriteLine("Ollama not running. Attempting to start...");

            var started = StartOllama(ollamaLaunchPath);
            if (!started)
                return false;

            return await WaitForOllamaAsync();
        }

        public async Task<bool> IsModelInstalledAsync(string modelName)
        {
            var tags = await _http.GetFromJsonAsync<TagsResponse>("/api/tags");

            return tags?.Models.Any(m =>
                string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase)) ?? false;
        }

        public async Task<bool> PullModelAsync(string modelName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = $"pull {modelName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return false;
                }

                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        Console.WriteLine(line);
                    }
                }

                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine(error);
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool StartOllama(string ollamaLaunchPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ollamaLaunchPath,
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
            int[] delaysMs = [150, 250, 400, 600, 1000, 1500, 2000];

            foreach (var delay in delaysMs)
            {
                if (await IsServerRunningAsync())
                    return true;

                await Task.Delay(delay);
            }

            return false;
        }
    }
}