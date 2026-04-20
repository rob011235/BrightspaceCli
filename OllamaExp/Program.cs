namespace OllamaExp
{
    internal class Program
    {
        static async Task Main(string[] args)
        {

            var ollama = new OllamaBootstrapper();
            await ollama.EnsureRunningAsync();

            var hardware = HardwareProfile.Detect();
            Console.WriteLine($"RAM: {hardware.TotalRamGb} GB");
            Console.WriteLine($"GPU: {hardware.GpuName ?? "Unknown"}");
            Console.WriteLine($"VRAM: {(hardware.GpuVramGb.HasValue ? hardware.GpuVramGb + " GB" : "Unknown")}");

            var recommendedModel = ModelSelector.SelectBestGradingModel(hardware);

            Console.WriteLine($"Recommended grading model: {recommendedModel}");

            var installed = await ollama.IsModelInstalledAsync(recommendedModel);

            Console.WriteLine(installed
                ? $"Model '{recommendedModel}' is installed."
                : $"Model '{recommendedModel}' is NOT installed.");
        }
    }
}