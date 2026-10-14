using System.Management;

namespace agent.Hardware;

public class WindowsHardwareProvider
{
    public HardwareInfo Collect()
    {
        return new HardwareInfo(
            GetCpu(), GetRam(), GetDisks(), GetGpus(), GetMotherboard(), GetOs());
    }

    private static CpuInfo GetCpu()
    {
        using var s = new ManagementObjectSearcher(
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
        foreach (ManagementObject o in s.Get())
            return new CpuInfo(
                (o["Name"]?.ToString() ?? "Unknown").Trim(),
                Convert.ToInt32(o["NumberOfCores"] ?? 0),
                Convert.ToInt32(o["NumberOfLogicalProcessors"] ?? 0),
                Math.Round(Convert.ToDouble(o["MaxClockSpeed"] ?? 0) / 1000.0, 2));
        return new CpuInfo("Unknown", 0, 0, 0);
    }

    private static List<RamModule> GetRam()
    {
        var list = new List<RamModule>();
        using var s = new ManagementObjectSearcher(
            "SELECT Manufacturer, Capacity, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        foreach (ManagementObject o in s.Get())
            list.Add(new RamModule(
                (o["Manufacturer"]?.ToString() ?? "Unknown").Trim(),
                Convert.ToInt64(o["Capacity"] ?? 0),
                Convert.ToInt32(o["Speed"] ?? 0),
                MapRamType(Convert.ToInt32(o["SMBIOSMemoryType"] ?? 0))));
        return list;
    }

    private static string MapRamType(int c) => c switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        _ => "Unknown"
    };

    private static List<DiskInfo> GetDisks()
    {
        var list = new List<DiskInfo>();
        using var s = new ManagementObjectSearcher(
            "SELECT Model, Size, InterfaceType, MediaType FROM Win32_DiskDrive");
        foreach (ManagementObject o in s.Get())
            list.Add(new DiskInfo(
                (o["Model"]?.ToString() ?? "Unknown").Trim(),
                Convert.ToInt64(o["Size"] ?? 0),
                o["InterfaceType"]?.ToString() ?? "Unknown",
                o["MediaType"]?.ToString() ?? "Unknown"));
        return list;
    }

    private static List<GpuInfo> GetGpus()
    {
        var list = new List<GpuInfo>();
        using var s = new ManagementObjectSearcher(
            "SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");
        foreach (ManagementObject o in s.Get())
        {
            long? vram = null;
            try { if (o["AdapterRAM"] != null) vram = Convert.ToInt64(o["AdapterRAM"]); } catch { }
            list.Add(new GpuInfo(
                (o["Name"]?.ToString() ?? "Unknown").Trim(),
                vram,
                o["DriverVersion"]?.ToString() ?? "Unknown"));
        }
        return list;
    }

    private static MotherboardInfo GetMotherboard()
    {
        using var s = new ManagementObjectSearcher(
            "SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard");
        foreach (ManagementObject o in s.Get())
            return new MotherboardInfo(
                (o["Manufacturer"]?.ToString() ?? "Unknown").Trim(),
                (o["Product"]?.ToString() ?? "Unknown").Trim(),
                (o["SerialNumber"]?.ToString() ?? "Unknown").Trim());
        return new MotherboardInfo("Unknown", "Unknown", "Unknown");
    }

    private static OsInfo GetOs()
    {
        using var s = new ManagementObjectSearcher(
            "SELECT Caption, Version, OSArchitecture, LastBootUpTime FROM Win32_OperatingSystem");
        foreach (ManagementObject o in s.Get())
        {
            var boot = ManagementDateTimeConverter.ToDateTime(o["LastBootUpTime"]?.ToString());
            return new OsInfo(
                (o["Caption"]?.ToString() ?? "Unknown").Trim(),
                o["Version"]?.ToString() ?? "Unknown",
                o["OSArchitecture"]?.ToString() ?? "Unknown",
                Environment.MachineName,
                DateTime.Now - boot);
        }
        return new OsInfo("Unknown", "Unknown", "Unknown", Environment.MachineName, TimeSpan.Zero);
    }
}