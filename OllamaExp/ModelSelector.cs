namespace OllamaExp
{
    public static class ModelSelector
    {
        public static string SelectBestGradingModel(HardwareProfile hardware)
        {
            // Conservative grading-focused heuristic.
            // Prefer coder models for code grading.
            // Fall back smaller if VRAM or RAM is limited.

            var ram = hardware.TotalRamGb;
            var vram = hardware.GpuVramGb ?? 0;

            if (vram >= 14 && ram >= 24)
                return "qwen2.5-coder:14b";

            if (vram >= 8 && ram >= 16)
                return "qwen2.5-coder:7b";

            if (vram >= 6 && ram >= 12)
                return "qwen2.5-coder:3b";

            if (ram >= 8)
                return "qwen2.5-coder:1.5b";

            return "qwen2.5-coder:0.5b";
        }
    }
}