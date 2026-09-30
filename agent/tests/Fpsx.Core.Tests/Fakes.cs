using Fpsx.Core.Catalog;
using Fpsx.Core.Engine;
using Fpsx.Core.Games;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;

namespace Fpsx.Core.Tests;

/// <summary>Windows em memória. Permite simular falha de escrita e escrita que "não pega".</summary>
public sealed class FakeSystem : ISystemAccess
{
    public Dictionary<(RegistryRoot, string, string), RegValue> Registry { get; } = new();
    public string? ActiveScheme { get; set; } = PowerSchemes.Balanced;
    public Dictionary<string, DisplayMode> Displays { get; } = new();
    public List<string> Log { get; } = [];

    /// <summary>Nomes de valor cuja escrita lança exceção.</summary>
    public HashSet<string> FailWrites { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nomes de valor cuja escrita é ignorada em silêncio (verificação deve pegar).</summary>
    public HashSet<string> IgnoreWrites { get; } = new(StringComparer.OrdinalIgnoreCase);

    private static (RegistryRoot, string, string) K(RegistryRoot r, string p, string n) => (r, p.ToLowerInvariant(), n.ToLowerInvariant());

    public RegValue? ReadRegistry(RegistryRoot root, string path, string name) => Registry.GetValueOrDefault(K(root, path, name));

    public void WriteRegistry(RegistryRoot root, string path, string name, RegValue value)
    {
        if (FailWrites.Contains(name))
            throw new UnauthorizedAccessException($"acesso negado a {name}");
        Log.Add($"write {name}={value.Data}");
        if (!IgnoreWrites.Contains(name))
            Registry[K(root, path, name)] = value;
    }

    public void DeleteRegistryValue(RegistryRoot root, string path, string name)
    {
        Log.Add($"delete {name}");
        Registry.Remove(K(root, path, name));
    }

    public string? GetActivePowerScheme() => ActiveScheme;

    /// <summary>Planos cuja ativação lança exceção (sem permissão, plano removido).</summary>
    public HashSet<string> FailPowerSchemes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void SetActivePowerScheme(string guid)
    {
        if (FailPowerSchemes.Contains(guid))
            throw new UnauthorizedAccessException($"acesso negado ao plano {guid}");
        Log.Add($"power {guid}");
        ActiveScheme = guid;
    }

    public DisplayMode? GetDisplayMode(string deviceName) => Displays.GetValueOrDefault(deviceName);

    public void SetDisplayMode(string deviceName, DisplayMode mode)
    {
        Log.Add($"display {deviceName} {mode.RefreshHz}");
        Displays[deviceName] = mode;
    }

    public CacheClearResult ClearCache(CacheTarget target)
    {
        Log.Add($"cache {target}");
        return new CacheClearResult(10, 1024, 0, target.ToString());
    }

    public CommandResult RunNetworkRepair(NetworkRepairKind kind)
    {
        Log.Add($"net {kind}");
        return new CommandResult(0, "ok");
    }

    public CommandResult RunSystemRepair(SystemRepairKind kind)
    {
        Log.Add($"repair {kind}");
        return new CommandResult(0, "ok");
    }

    /// <summary>false simula a Proteção do Sistema desligada.</summary>
    public bool RestorePointsEnabled { get; set; } = true;

    public CommandResult CreateRestorePoint(string description)
    {
        Log.Add("restore-point");
        return RestorePointsEnabled ? new CommandResult(0, "ok") : new CommandResult(1, "Proteção do sistema desligada");
    }

    public CommandResult InstallDriverPackage(string folder)
    {
        Log.Add($"kit {folder}");
        return new CommandResult(0, "Drivers do kit instalados.");
    }

    public CommandResult InstallDriverUpdate(string updateId)
    {
        Log.Add($"driver {updateId}");
        return new CommandResult(0, "Driver instalado.");
    }

    /// <summary>PID -> nome dos processos "abertos".</summary>
    public Dictionary<int, string> Processes { get; } = new();

    /// <summary>Processos que ignoram o pedido de fechar (sem janela, travados).</summary>
    public HashSet<int> Stubborn { get; } = new();

    public string? ProcessName(int pid) => Processes.GetValueOrDefault(pid);

    public bool CloseProcess(int pid, TimeSpan timeout)
    {
        Log.Add($"close {pid}");
        if (Stubborn.Contains(pid))
            return false;
        Processes.Remove(pid);
        return true;
    }

    public bool GameRunning { get; set; }

    public Dictionary<string, string> GameConfig { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsGameRunning(string gameId) => GameRunning;

    public string? ReadGameConfig(string gameId, string key) => GameConfig.GetValueOrDefault(key);

    public void WriteGameConfig(string gameId, string key, string value)
    {
        Log.Add($"game {key}={value}");
        GameConfig[key] = value;
    }

    public bool AppRunning { get; set; }

    public Dictionary<string, string> AppSettings { get; } = new(StringComparer.Ordinal);

    public bool IsAppRunning(string appId) => AppRunning;

    public string? ReadAppSetting(string appId, string key) => AppSettings.GetValueOrDefault(key);

    public void WriteAppSetting(string appId, string key, string value)
    {
        Log.Add($"app {key}={value}");
        AppSettings[key] = value;
    }
}

public static class Pc
{
    private const long Gb = 1024L * 1024 * 1024;

    /// <summary>Desktop saudável e já bem configurado: o caso em que o FPSX não deve mudar nada.</summary>
    public static SystemSnapshot Healthy() => new()
    {
        CapturedAt = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero),
        IsElevated = false,
        Os = new OsInfo { Caption = "Windows 11 Pro", Build = 26100 },
        Cpu = new CpuInfo { Name = "AMD Ryzen 5 5600", Manufacturer = "AuthenticAMD", Cores = 6, Threads = 12, AvgUsagePercent = 4, AvgPerformanceLimitPercent = 100, MinPerformanceLimitPercent = 100 },
        Gpus = [new GpuInfo { Name = "NVIDIA GeForce RTX 3060", Vendor = GpuVendor.Nvidia, DriverVersion = "32.0.15.6094", DriverDate = new DateTime(2026, 6, 1), VramBytes = 12 * Gb }],
        Memory = new MemoryInfo { TotalBytes = 16 * Gb, AvailableBytes = 10 * Gb, PagefilePresent = true },
        Disks = [new DiskInfo { DriveLetter = "C:", TotalBytes = 1000 * Gb, FreeBytes = 400 * Gb, Media = MediaKind.Ssd, BusType = "NVMe", Health = "Healthy", IsSystemDrive = true }],
        Power = new PowerInfo
        {
            ActiveSchemeGuid = PowerSchemes.HighPerformance,
            ActiveSchemeName = "Alto desempenho",
            HasBattery = false,
            OnAcPower = true,
            Schemes = [new PowerScheme(PowerSchemes.Balanced, "Equilibrado"), new PowerScheme(PowerSchemes.HighPerformance, "Alto desempenho"), new PowerScheme(PowerSchemes.PowerSaver, "Economia de energia")],
        },
        Gaming = new GamingFeatures(),
        Displays = [new DisplayInfo { DeviceName = @"\\.\DISPLAY1", IsPrimary = true, Width = 1920, Height = 1080, CurrentHz = 144, MaxHzAtCurrentResolution = 144 }],
        Network = new NetworkInfo
        {
            AdapterName = "Ethernet",
            AdapterType = "ETHERNET",
            Pings =
            [
                new PingResult { Target = "192.168.0.1", Role = "gateway", Sent = 20, Received = 20, AvgMs = 1, JitterMs = 0.2 },
                new PingResult { Target = "1.1.1.1", Role = "internet", Sent = 20, Received = 20, AvgMs = 12, JitterMs = 1 },
            ],
        },
        Security = new SecurityInfo { MemoryIntegrityEnabled = true },
        SampleSeconds = 3,
    };

    /// <summary>PC com todos os problemas clássicos de configuração.</summary>
    public static SystemSnapshot Misconfigured() => Healthy() with
    {
        Power = Healthy().Power! with { ActiveSchemeGuid = PowerSchemes.PowerSaver, ActiveSchemeName = "Economia de energia" },
        Gaming = new GamingFeatures { AutoGameModeValue = 0, BackgroundRecordingValue = 1, AppCaptureValue = 1, HagsValue = 1 },
        Displays = [new DisplayInfo { DeviceName = @"\\.\DISPLAY1", IsPrimary = true, Width = 1920, Height = 1080, CurrentHz = 60, MaxHzAtCurrentResolution = 144 }],
        Startup =
        [
            new StartupEntry { Name = "Discord", Command = @"C:\Users\x\AppData\Local\Discord\Update.exe --processStart Discord.exe", Location = "HKCU_RUN", Enabled = true },
            new StartupEntry { Name = "SecurityHealth", Command = @"%windir%\system32\SecurityHealthSystray.exe", Location = "HKLM_RUN", Enabled = true },
            new StartupEntry { Name = "RtkAudUService", Command = @"C:\Windows\System32\RtkAudUService64.exe", Location = "HKLM_RUN", Enabled = true },
        ],
    };

    public static SystemSnapshot Laptop(bool onAc) => Healthy() with
    {
        Power = Healthy().Power! with { ActiveSchemeGuid = PowerSchemes.Balanced, ActiveSchemeName = "Equilibrado", HasBattery = true, OnAcPower = onAc },
        Gpus = [new GpuInfo { Name = "Intel(R) Iris(R) Xe Graphics", Vendor = GpuVendor.Intel, DriverDate = new DateTime(2026, 1, 1), LikelyIntegrated = true }],
    };

    public static SystemSnapshot WithCs2(SystemSnapshot s, Dictionary<string, string> config) => s with
    {
        Games = [new GameInstall { GameId = "cs2", Name = "Counter-Strike 2", InstallPath = @"C:\Steam\steamapps\common\Counter-Strike Global Offensive", ConfigPath = "cs2_video.txt", Config = config }],
    };

    /// <summary>Matriz de ambientes da seção 42 do spec.</summary>
    public static IEnumerable<object[]> Matrix()
    {
        var cpus = new[] { ("Intel Core i5-12400F", "GenuineIntel"), ("AMD Ryzen 7 7800X3D", "AuthenticAMD") };
        var gpus = new[]
        {
            new GpuInfo { Name = "NVIDIA GeForce RTX 4070", Vendor = GpuVendor.Nvidia, DriverDate = new DateTime(2026, 8, 1) },
            new GpuInfo { Name = "AMD Radeon RX 7800 XT", Vendor = GpuVendor.Amd, DriverDate = new DateTime(2024, 1, 1) },
            new GpuInfo { Name = "Intel(R) UHD Graphics 770", Vendor = GpuVendor.Intel, DriverDate = new DateTime(2025, 12, 1), LikelyIntegrated = true },
        };
        var ram = new[] { 8L, 16, 32 };
        var media = new[] { MediaKind.Ssd, MediaKind.Hdd };
        var laptop = new[] { false, true };

        foreach (var (cpu, vendor) in cpus)
            foreach (var gpu in gpus)
                foreach (var gb in ram)
                    foreach (var m in media)
                        foreach (var isLaptop in laptop)
                        {
                            var baseline = Misconfigured();
                            yield return
                            [
                                $"{cpu} | {gpu.Name} | {gb} GB | {m} | {(isLaptop ? "notebook" : "desktop")}",
                                baseline with
                                {
                                    Cpu = baseline.Cpu! with { Name = cpu, Manufacturer = vendor },
                                    Gpus = [gpu],
                                    Memory = new MemoryInfo { TotalBytes = gb * Gb, AvailableBytes = gb * Gb / 3, PagefilePresent = true },
                                    Disks = [baseline.Disks[0] with { Media = m }],
                                    Power = baseline.Power! with { HasBattery = isLaptop, OnAcPower = !isLaptop },
                                },
                            ];
                        }
    }
}

public static class TestData
{
    private static readonly string Base = AppContext.BaseDirectory;

    public static OptimizationCatalog Catalog() => OptimizationCatalog.Load(Path.Combine(Base, "catalog"));

    public static IReadOnlyList<GameProfile> Games() => GameProfile.LoadAll(Path.Combine(Base, "game-profiles"));

    public static DecisionEngine Engine() => new(Catalog(), Games());

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fpsx-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
