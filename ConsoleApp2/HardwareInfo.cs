using System;
using System.Collections.Generic;

namespace agent.Hardware
{
    public sealed record CpuInfo(
        string Name,
        int Cores,
        int Threads,
        double MaxClockGhz
    );

    public sealed record RamModule(
        string Manufacturer,
        long SizeBytes,
        int SpeedMhz,
        string Type
    );

    public sealed record DiskInfo(
        string Model,
        long SizeBytes,
        string InterfaceType,
        string MediaType
    );

    public sealed record GpuInfo(
        string Name,
        long? VramBytes,
        string DriverVersion
    );

    public sealed record MotherboardInfo(
        string Manufacturer,
        string Product,
        string SerialNumber
    );

    public sealed record OsInfo(
        string Caption,
        string Version,
        string Architecture,
        string MachineName,
        TimeSpan Uptime
    );

    public sealed record HardwareInfo(
        CpuInfo Cpu,
        IReadOnlyList<RamModule> RamModules,
        IReadOnlyList<DiskInfo> Disks,
        IReadOnlyList<GpuInfo> Gpus,
        MotherboardInfo Motherboard,
        OsInfo Os
    )
    {
        public long TotalRamBytes => System.Linq.Enumerable.Sum(RamModules, m => m.SizeBytes);
        public long TotalDiskBytes => System.Linq.Enumerable.Sum(Disks, d => d.SizeBytes);
    }
}