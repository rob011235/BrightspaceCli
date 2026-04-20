namespace OllamaExp
{
    using System.Diagnostics;

    public sealed class OllamaInstallationService
    {
        public bool IsOllamaInstalled()
        {
            var localAppPath = GetLocalOllamaPath();

            if (File.Exists(localAppPath))
                return true;

            return IsCommandAvailable("ollama");
        }

        public string GetBestLaunchPath()
        {
            var localAppPath = GetLocalOllamaPath();

            if (File.Exists(localAppPath))
                return localAppPath;

            return "ollama";
        }

        public async Task<bool> InstallOllamaAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"irm https://ollama.com/install.ps1 | iex\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return false;

                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        Console.WriteLine(line);
                }

                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    if (!string.IsNullOrWhiteSpace(error))
                        Console.WriteLine(error);

                    return false;
                }

                return IsOllamaInstalled();
            }
            catch
            {
                return false;
            }
        }

        private static string GetLocalOllamaPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Ollama",
                "ollama.exe");
        }

        private static bool IsCommandAvailable(string command)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                    return false;

                process.WaitForExit(1500);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}