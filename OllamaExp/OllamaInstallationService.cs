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