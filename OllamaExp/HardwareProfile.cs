namespace OllamaExp
{
    using System.Diagnostics;
    using System.Management;

    public sealed class HardwareProfile
    {
        public int TotalRamGb { get; init; }
        public string? GpuName { get; init; }
        public int? GpuVramGb { get; init; }

        public static HardwareProfile Detect()
        {
            var ramGb = DetectRamGb();
            var gpu = DetectGpu();

            return new HardwareProfile
            {
                TotalRamGb = ramGb,
                GpuName = gpu.Name,
                GpuVramGb = gpu.VramGb
            };
        }

        private static int DetectRamGb()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");

                foreach (var obj in searcher.Get().Cast<ManagementObject>())
                {
                    if (obj["TotalPhysicalMemory"] is ulong bytes)
                    {
                        return (int)Math.Round(bytes / 1024d / 1024d / 1024d);
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private static (string? Name, int? VramGb) DetectGpu()
        {
            // First try nvidia-smi for NVIDIA cards because it is usually more reliable.
            var nvidia = TryDetectNvidiaGpuWithNvidiaSmi();
            if (nvidia.Name is not null)
                return nvidia;

            // Fall back to WMI.
            return TryDetectGpuWithWmi();
        }

        private static (string? Name, int? VramGb) TryDetectNvidiaGpuWithNvidiaSmi()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=name,memory.total --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return (null, null);

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1500);

                var firstLine = output
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(firstLine))
                    return (null, null);

                var parts = firstLine.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length < 2)
                    return (null, null);

                var name = parts[0];
                if (int.TryParse(parts[1], out var vramMb))
                {
                    var vramGb = (int)Math.Round(vramMb / 1024d);
                    return (name, vramGb);
                }

                return (name, null);
            }
            catch
            {
                return (null, null);
            }
        }

        private static (string? Name, int? VramGb) TryDetectGpuWithWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, AdapterRAM FROM Win32_VideoController");

                foreach (var obj in searcher.Get().Cast<ManagementObject>())
                {
                    var name = obj["Name"]?.ToString();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    int? vramGb = null;

                    if (obj["AdapterRAM"] != null && long.TryParse(obj["AdapterRAM"].ToString(), out var ramBytes))
                    {
                        vramGb = (int)Math.Round(ramBytes / 1024d / 1024d / 1024d);
                    }

                    // Prefer NVIDIA if present.
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        return (name, vramGb);

                    // Otherwise return the first valid GPU.
                    return (name, vramGb);
                }
            }
            catch
            {
            }

            return (null, null);
        }
    }
}