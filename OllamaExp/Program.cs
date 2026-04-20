namespace OllamaExp
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            var hardware = HardwareProfile.Detect();
            Console.WriteLine($"RAM: {hardware.TotalRamGb} GB");
            Console.WriteLine($"GPU: {hardware.GpuName ?? "Unknown"}");
            Console.WriteLine($"VRAM: {(hardware.GpuVramGb.HasValue ? hardware.GpuVramGb + " GB" : "Unknown")}");

            var recommendedModel = ModelSelector.SelectBestGradingModel(hardware);
            Console.WriteLine($"Recommended grading model: {recommendedModel}");

            var installer = new OllamaInstallationService();
            var runtime = new OllamaRuntimeService();

            if (!installer.IsOllamaInstalled())
            {
                Console.WriteLine("Ollama is not installed.");
                Console.WriteLine("Install Ollama, then run this tool again.");
                Console.WriteLine($"Recommended model after install: {recommendedModel}");
                return 1;
            }

            var launchPath = installer.GetBestLaunchPath();

            var running = await runtime.EnsureRunningAsync(launchPath);
            if (!running)
            {
                Console.WriteLine("Ollama could not be started.");
                return 2;
            }

            var modelInstalled = await runtime.IsModelInstalledAsync(recommendedModel);
            if (!modelInstalled)
            {
                Console.WriteLine($"Model '{recommendedModel}' is not installed. Pulling...");

                var pulled = await runtime.PullModelAsync(recommendedModel);
                if (!pulled)
                {
                    Console.WriteLine($"Failed to pull model '{recommendedModel}'.");
                    return 3;
                }
            }

            Console.WriteLine("Ollama is ready.");
            return 0;
        }
    }
}