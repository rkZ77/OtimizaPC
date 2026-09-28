using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Fpsx.Core.Games;
using Fpsx.Core.Model;
using Fpsx.Core.Optimizations;
using Microsoft.Win32;

namespace Fpsx.Windows;

public sealed record CollectOptions
{
    public double SampleSeconds { get; init; } = 3;

    /// <summary>Coloca todos os núcleos em carga durante a amostra para revelar throttling.</summary>
    public bool CpuStressTest { get; init; }

    public bool Network { get; init; } = true;
}

/// <summary>
/// Monta o SystemSnapshot. Cada bloco é independente e falha isolado: o que
/// não puder ser lido fica null e aparece como "desconhecido" no relatório.
/// Nada aqui escreve no sistema, e nada coleta arquivo ou conteúdo pessoal.
/// </summary>
public sealed class SnapshotCollector(IReadOnlyList<GameProfile> gameProfiles)
{
    public SystemSnapshot Collect(CollectOptions options, Action<string>? progress = null)
    {
        progress?.Invoke("Lendo sistema e hardware");
        var os = Os();
        var gpus = Gpus();
        var memory = Memory();
        var disks = Disks();
        var power = Power();
        var displays = Displays();
        var security = Security();
        var board = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();

        progress?.Invoke("Procurando jogos");
        var games = Games();
        var gaming = Gaming(games);

        var network = Task.Run(() => options.Network ? NetworkProbe() : null);

        progress?.Invoke(options.CpuStressTest ? $"Teste de CPU sob carga ({options.SampleSeconds:0} s)" : $"Amostrando CPU e processos ({options.SampleSeconds:0} s)");
        var (cpu, processes) = SampleCpuAndProcesses(options);

        progress?.Invoke("Lendo programas de inicialização");
        var startup = Startup(processes);

        if (options.Network)
            progress?.Invoke("Testando rede");
        var net = network.GetAwaiter().GetResult();

        return new SystemSnapshot
        {
            CapturedAt = DateTimeOffset.Now,
            IsElevated = IsElevated(),
            Os = os,
            Cpu = cpu,
            Gpus = gpus,
            Memory = memory,
            Disks = disks,
            Power = power,
            Gaming = gaming,
            Startup = startup,
            Processes = processes,
            Displays = displays,
            Network = net,
            Security = security,
            Games = games,
            Discord = Discord(startup),
            BoardManufacturer = board?.Str("Manufacturer").Trim() ?? "",
            BoardProduct = board?.Str("Product").Trim() ?? "",
            SampleSeconds = options.SampleSeconds,
        };
    }

    private static DiscordInfo? Discord(IReadOnlyList<StartupEntry> startup)
    {
        try
        {
            return DiscordSettings.Read(startup);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static OsInfo Os()
    {
        var row = Wmi.Query("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
        if (row is null)
            return new OsInfo { Build = Environment.OSVersion.Version.Build, Version = Environment.OSVersion.VersionString };
        return new OsInfo
        {
            Caption = row.Str("Caption").Replace("Microsoft ", ""),
            Version = row.Str("Version"),
            Build = int.TryParse(row.Str("BuildNumber"), out var b) ? b : Environment.OSVersion.Version.Build,
        };
    }

    private static (CpuInfo?, IReadOnlyList<ProcessSample>) SampleCpuAndProcesses(CollectOptions options)
    {
        var proc = Wmi.Query("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").FirstOrDefault();

        var before = ProcessTimes();
        var sw = Stopwatch.StartNew();
        using var stop = new CancellationTokenSource();
        var stress = options.CpuStressTest
            ? Enumerable.Range(0, Environment.ProcessorCount).Select(_ => Task.Factory.StartNew(() => Spin(stop.Token), TaskCreationOptions.LongRunning)).ToArray()
            : [];

        var usage = new List<double>();
        var limit = new List<double>();
        while (sw.Elapsed.TotalSeconds < options.SampleSeconds)
        {
            var row = Wmi.Query("SELECT PercentProcessorUtility, PercentProcessorTime, PercentPerformanceLimit FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'").FirstOrDefault();
            if (row is not null)
            {
                if ((row.Long("PercentProcessorUtility") ?? row.Long("PercentProcessorTime")) is { } u)
                    usage.Add(Math.Min(100, u));
                if (row.Long("PercentPerformanceLimit") is { } l)
                    limit.Add(l);
            }

            Thread.Sleep(500);
        }

        stop.Cancel();
        Task.WaitAll(stress);
        var elapsed = sw.Elapsed.TotalSeconds;
        var after = ProcessTimes();

        var cores = Environment.ProcessorCount;
        var processes = after
            .Where(a => before.ContainsKey(a.Key))
            .Select(a =>
            {
                var (name, cpuTime, ws, window, minimized) = a.Value;
                var delta = (cpuTime - before[a.Key].Cpu).TotalSeconds;
                return new ProcessSample { Pid = a.Key, Name = name, CpuPercent = Math.Max(0, 100.0 * delta / (elapsed * cores)), WorkingSetBytes = ws, HasWindow = window, Minimized = minimized };
            })
            .Where(p => p.Pid != Environment.ProcessId)
            .OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes)
            .ToList();

        // Só os relevantes vão para o snapshot: o resto é ruído no relatório
        // e dado que não precisamos guardar.
        var top = processes.Where(p => p.CpuPercent >= 0.5).Take(25)
            .Concat(processes.OrderByDescending(p => p.WorkingSetBytes).Take(15))
            .ToList();
        // Navegador e afins rodam em vários processos, e o que pesa quase nunca
        // é o da janela. Os irmãos com o mesmo nome entram para somar o peso do
        // programa inteiro e achar quem recebe o pedido de fechar.
        var names = top.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relevant = top.Concat(processes.Where(p => names.Contains(p.Name)))
            .DistinctBy(p => p.Pid).ToList();

        CpuInfo? cpu = proc is null ? null : new CpuInfo
        {
            Name = proc.Str("Name").Trim(),
            Manufacturer = proc.Str("Manufacturer"),
            Cores = (int)(proc.Long("NumberOfCores") ?? 0),
            Threads = (int)(proc.Long("NumberOfLogicalProcessors") ?? cores),
            MaxClockMhz = (int)(proc.Long("MaxClockSpeed") ?? 0),
            AvgUsagePercent = usage.Count > 0 ? usage.Average() : null,
            AvgPerformanceLimitPercent = limit.Count > 0 ? limit.Average() : null,
            MinPerformanceLimitPercent = limit.Count > 0 ? limit.Min() : null,
            SampledUnderLoad = options.CpuStressTest,
            TemperatureC = Temperature(),
        };
        return (cpu, relevant);
    }

    private static void Spin(CancellationToken token)
    {
        double x = 1;
        while (!token.IsCancellationRequested)
            x = Math.Sqrt(x + 1.000001);
        GC.KeepAlive(x);
    }

    private static Dictionary<int, (string Name, TimeSpan Cpu, long Ws, bool Window, bool Minimized)> ProcessTimes()
    {
        var result = new Dictionary<int, (string, TimeSpan, long, bool, bool)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    var window = p.MainWindowHandle;
                    result[p.Id] = (p.ProcessName, p.TotalProcessorTime, p.WorkingSet64, window != IntPtr.Zero, window != IntPtr.Zero && Native.IsIconic(window));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                {
                    // Processos protegidos (antivírus, sistema) não expõem tempo de CPU sem admin.
                }
            }
        }

        return result;
    }

    /// <summary>Zona térmica ACPI: muitas placas não expõem ou expõem valor fixo. Só é usada se plausível.</summary>
    private static double? Temperature()
    {
        var temps = Wmi.Query("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\wmi")
            .Select(r => r.Long("CurrentTemperature"))
            .OfType<long>()
            .Select(t => t / 10.0 - 273.15)
            .Where(c => c is > 20 and < 120)
            .ToList();
        return temps.Count > 0 ? temps.Max() : null;
    }

    private static IReadOnlyList<GpuInfo> Gpus()
    {
        var vram = VramByName();
        return Wmi.Query("SELECT Name, AdapterCompatibility, DriverVersion, DriverDate FROM Win32_VideoController")
            .Where(r => !r.Str("Name").Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
            .Select(r =>
            {
                var name = r.Str("Name").Trim();
                var vendor = VendorOf(name + " " + r.Str("AdapterCompatibility"));
                return new GpuInfo
                {
                    Name = name,
                    Vendor = vendor,
                    DriverVersion = r.Str("DriverVersion"),
                    DriverDate = r.CimDate("DriverDate"),
                    VramBytes = vram.TryGetValue(name, out var v) ? v : null,
                    LikelyIntegrated = IsIntegrated(vendor, name),
                };
            })
            .ToList();
    }

    private static GpuVendor VendorOf(string s)
    {
        if (s.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Nvidia;
        if (s.Contains("AMD", StringComparison.OrdinalIgnoreCase) || s.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || s.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Amd;
        if (s.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Intel;
        return GpuVendor.Unknown;
    }

    private static bool IsIntegrated(GpuVendor vendor, string name) => vendor switch
    {
        GpuVendor.Intel => !name.Contains("Arc", StringComparison.OrdinalIgnoreCase),
        // APUs aparecem como "AMD Radeon(TM) Graphics" ou "Radeon Vega 8 Graphics", sem modelo RX.
        GpuVendor.Amd => name.EndsWith("Graphics", StringComparison.OrdinalIgnoreCase) && !name.Contains(" RX ", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    // Win32_VideoController.AdapterRAM é uint32 e trava em 4 GB. O valor real
    // fica na chave da classe de vídeo, gravada pelo driver.
    private static Dictionary<string, long> VramByName()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null)
                return result;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                try
                {
                    using var key = cls.OpenSubKey(sub);
                    if (key?.GetValue("DriverDesc") is not string desc)
                        continue;
                    var raw = key.GetValue("HardwareInformation.qwMemorySize") ?? key.GetValue("HardwareInformation.MemorySize");
                    long? bytes = raw switch
                    {
                        long l => l,
                        int i => (uint)i,
                        byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => null,
                    };
                    if (bytes is > 0)
                        result[desc.Trim()] = bytes.Value;
                }
                catch (System.Security.SecurityException)
                {
                }
            }
        }
        catch (System.Security.SecurityException)
        {
        }

        return result;
    }

    private static MemoryInfo? Memory()
    {
        var status = new Native.MemoryStatusEx { Length = (uint)Marshal.SizeOf<Native.MemoryStatusEx>() };
        if (!Native.GlobalMemoryStatusEx(ref status))
            return null;

        var pagefiles = Wmi.Query("SELECT Name FROM Win32_PageFileUsage");
        var cs = Wmi.Query("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem").FirstOrDefault();
        return new MemoryInfo
        {
            TotalBytes = (long)status.TotalPhys,
            AvailableBytes = (long)status.AvailPhys,
            CommitLimitBytes = (long)status.TotalPageFile,
            CommittedBytes = (long)(status.TotalPageFile - status.AvailPageFile),
            // Consulta vazia pode ser falha de WMI; só afirmamos "sem pagefile"
            // quando a outra classe respondeu e diz que não é automático.
            PagefilePresent = pagefiles.Count > 0 ? true : cs is null ? null : false,
            PagefileAutomatic = cs?.Str("AutomaticManagedPagefile") is { Length: > 0 } a ? a.Equals("True", StringComparison.OrdinalIgnoreCase) : null,
            Modules = Wmi.Query("SELECT DeviceLocator, BankLabel, Capacity, ConfiguredClockSpeed, Speed, PartNumber FROM Win32_PhysicalMemory")
                .Select(r => new MemoryModule
                {
                    Slot = r.Str("DeviceLocator").Trim(),
                    Bank = r.Str("BankLabel").Trim(),
                    CapacityBytes = r.Long("Capacity") ?? 0,
                    // ConfiguredClockSpeed é a velocidade atual; placas antigas só preenchem Speed.
                    ConfiguredMts = (int)(r.Long("ConfiguredClockSpeed") is > 0 and var c ? c : r.Long("Speed") ?? 0),
                    PartNumber = r.Str("PartNumber").Trim(),
                })
                .ToList(),
        };
    }

    private static IReadOnlyList<DiskInfo> Disks()
    {
        const string storage = @"root\Microsoft\Windows\Storage";
        var partitions = Wmi.Query("SELECT DriveLetter, DiskNumber FROM MSFT_Partition", storage);
        var physical = Wmi.Query("SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus, OperationalStatus FROM MSFT_PhysicalDisk", storage)
            .ToDictionary(r => r.Str("DeviceId"), r => r);
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\').ToUpperInvariant() ?? "C:";

        var result = new List<DiskInfo>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var letter = drive.Name.TrimEnd('\\').ToUpperInvariant();
            var part = partitions.FirstOrDefault(p => (p.Str("DriveLetter") + ":").Equals(letter, StringComparison.OrdinalIgnoreCase));
            physical.TryGetValue(part?.Str("DiskNumber") ?? "-", out var disk);

            result.Add(new DiskInfo
            {
                DriveLetter = letter,
                Label = drive.VolumeLabel,
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.AvailableFreeSpace,
                Media = disk?.Long("MediaType") switch { 3 => MediaKind.Hdd, 4 => MediaKind.Ssd, _ => MediaKind.Unknown },
                BusType = disk?.Long("BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", null => "", _ => "outro" },
                Health = disk?.Long("HealthStatus") switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", null => "", _ => "Unknown" },
                OperationalStatus = OperationalStatusText(disk),
                Model = disk?.Str("FriendlyName") ?? "",
                IsSystemDrive = letter == systemRoot,
            });
        }

        return result;
    }

    // OperationalStatus é um array de códigos; 0xD00B (53259) = Predictive Failure.
    private static string OperationalStatusText(Dictionary<string, object?>? disk)
    {
        if (disk is null || !disk.TryGetValue("OperationalStatus", out var raw) || raw is not Array codes)
            return "";
        return string.Join(", ", codes.Cast<object>().Select(c => Convert.ToInt32(c, System.Globalization.CultureInfo.InvariantCulture) switch
        {
            2 => "OK",
            3 => "Degraded",
            5 => "Predictive Failure",
            0xD00B => "Predictive Failure",
            0xD00D => "Removing",
            _ => "Code " + c,
        }).Distinct());
    }

    private static PowerInfo? Power()
    {
        var active = PowerApi.ActiveScheme();
        var schemes = PowerApi.Schemes();
        bool hasBattery = false;
        bool? onAc = null;
        if (Native.GetSystemPowerStatus(out var st))
        {
            // 128 = sem bateria; 255 = status desconhecido.
            hasBattery = st.BatteryFlag != 128 && st.BatteryFlag != 255;
            onAc = st.ACLineStatus switch { 1 => true, 0 => false, _ => null };
        }

        return new PowerInfo
        {
            ActiveSchemeGuid = active,
            ActiveSchemeName = active is null ? null : schemes.FirstOrDefault(s => PowerSchemes.Is(s.Guid, active))?.Name,
            HasBattery = hasBattery,
            OnAcPower = onAc,
            Schemes = schemes,
        };
    }

    private static int? ReadDword(RegistryKey hive, string path, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            return key?.GetValue(name) is int i ? i : null;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? ReadString(RegistryKey hive, string path, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    private static GamingFeatures Gaming(IReadOnlyList<GameInstall> games) => new()
    {
        AutoGameModeValue = ReadDword(Registry.CurrentUser, RegistryPaths.GameBar, "AutoGameModeEnabled"),
        AppCaptureValue = ReadDword(Registry.CurrentUser, RegistryPaths.GameDvr, "AppCaptureEnabled"),
        BackgroundRecordingValue = ReadDword(Registry.CurrentUser, RegistryPaths.GameDvr, "HistoricalCaptureEnabled"),
        HagsValue = ReadDword(Registry.LocalMachine, RegistryPaths.GraphicsDrivers, "HwSchMode"),
        TransparencyValue = ReadDword(Registry.CurrentUser, RegistryPaths.Personalize, "EnableTransparency"),
        DirectXGlobalSettings = ReadString(Registry.CurrentUser, RegistryPaths.DirectXUserGpuPreferences, DirectXSettings.GlobalValueName),
        // Só os executáveis dos jogos com perfil: a lista inteira diria que
        // programas a pessoa usa, e isso não é da conta do RKZFPS.
        GpuPreferences = games
            .Where(g => g.ExecutablePath is not null)
            .Select(g => (Exe: g.ExecutablePath!, Value: ReadString(Registry.CurrentUser, RegistryPaths.DirectXUserGpuPreferences, g.ExecutablePath!)))
            .Where(x => x.Value is not null)
            .ToDictionary(x => x.Exe, x => x.Value!, StringComparer.OrdinalIgnoreCase),
    };

    private static IReadOnlyList<DisplayInfo> Displays() =>
        DisplayApi.Devices()
            .Select(d => (d, mode: DisplayApi.Current(d.Device)))
            .Where(x => x.mode is not null)
            .Select(x => new DisplayInfo
            {
                DeviceName = x.d.Device,
                FriendlyName = x.d.Friendly,
                IsPrimary = x.d.Primary,
                AdapterName = x.d.Adapter,
                Width = x.mode!.Width,
                Height = x.mode.Height,
                CurrentHz = x.mode.RefreshHz,
                MaxHzAtCurrentResolution = DisplayApi.MaxHz(x.d.Device, x.mode.Width, x.mode.Height),
            })
            .ToList();

    private static SecurityInfo Security()
    {
        var dg = Wmi.Query("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard").FirstOrDefault();
        bool? hvci = null;
        bool? vbs = null;
        if (dg is not null)
        {
            vbs = dg.Long("VirtualizationBasedSecurityStatus") == 2;
            // SecurityServicesRunning contém 2 quando HVCI está ativo.
            hvci = dg.TryGetValue("SecurityServicesRunning", out var running) && running is Array arr && arr.Cast<object>().Any(v => Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture) == 2);
        }
        else if (ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled") is { } enabled)
        {
            hvci = enabled == 1;
        }

        return new SecurityInfo { MemoryIntegrityEnabled = hvci, VbsRunning = vbs };
    }

    // ---- inicialização ----

    private static IReadOnlyList<StartupEntry> Startup(IReadOnlyList<ProcessSample> processes)
    {
        var entries = new List<StartupEntry>();
        AddRun(entries, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU_RUN", processes);
        AddRun(entries, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM_RUN", processes);
        AddRun(entries, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM_RUN32", processes);
        AddFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "STARTUP_FOLDER_USER", processes);
        AddFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "STARTUP_FOLDER_COMMON", processes);
        return entries;
    }

    private static void AddRun(List<StartupEntry> entries, RegistryKey hive, string path, string location, IReadOnlyList<ProcessSample> processes)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null)
                return;
            foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
            {
                var command = key.GetValue(name)?.ToString() ?? "";
                entries.Add(Entry(name, command, location, ExeStem(command), processes));
            }
        }
        catch (System.Security.SecurityException)
        {
        }
    }

    private static void AddFolder(List<StartupEntry> entries, string folder, string location, IReadOnlyList<ProcessSample> processes)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return;
        foreach (var file in Directory.EnumerateFiles(folder).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileName(file);
            entries.Add(Entry(name, file, location, Path.GetFileNameWithoutExtension(file), processes));
        }
    }

    private static StartupEntry Entry(string name, string command, string location, string? stem, IReadOnlyList<ProcessSample> processes)
    {
        string? raw = null;
        if (StartupOptimization.ApprovedLocation(location) is { } approved)
        {
            try
            {
                using var key = (approved.Root == RegistryRoot.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine).OpenSubKey(approved.Path);
                if (key?.GetValue(name) is byte[] bytes)
                    raw = Convert.ToHexString(bytes);
            }
            catch (System.Security.SecurityException)
            {
            }
        }

        var running = stem is null ? null : processes.Where(p => p.Name.Equals(stem, StringComparison.OrdinalIgnoreCase)).Sum(p => (long?)p.WorkingSetBytes);
        return new StartupEntry
        {
            Name = name,
            Command = command,
            Location = location,
            ApprovedRawHex = raw,
            Enabled = StartupOptimization.IsEnabledRaw(raw),
            RunningWorkingSetBytes = running is > 0 ? running : null,
        };
    }

    private static string? ExeStem(string command)
    {
        var c = command.Trim();
        string path;
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            path = end > 0 ? c[1..end] : c.Trim('"');
        }
        else
        {
            var exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            path = exe > 0 ? c[..(exe + 4)] : c.Split(' ')[0];
        }

        var stem = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrEmpty(stem) ? null : stem;
    }

    // ---- rede ----

    private static NetworkInfo NetworkProbe()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => (n, gw: n.GetIPProperties().GatewayAddresses.Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(System.Net.IPAddress.Any))))
            .Where(x => x.gw is not null)
            // Ethernet antes de Wi-Fi: com os dois ligados, o Windows prefere o cabo.
            .OrderBy(x => x.n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0)
            .FirstOrDefault();
        if (nic.n is null)
            return new NetworkInfo { AdapterType = "NONE" };

        var type = nic.n.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => "WIFI",
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "ETHERNET",
            _ => "OTHER",
        };

        var targets = new[] { (nic.gw!.ToString(), "gateway"), ("1.1.1.1", "internet"), ("8.8.8.8", "internet") };
        var pings = targets.Select(t => Task.Run(() => PingSeries(t.Item1, t.Item2, 20))).ToArray();
        Task.WaitAll(pings);

        return new NetworkInfo
        {
            AdapterName = nic.n.Name,
            AdapterType = type,
            LinkSpeedMbps = nic.n.Speed > 0 ? nic.n.Speed / 1_000_000 : null,
            Pings = pings.Select(p => p.Result).ToList(),
        };
    }

    private static PingResult PingSeries(string target, string role, int count)
    {
        var rtts = new List<double>();
        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            try
            {
                var reply = ping.Send(target, 1000);
                if (reply.Status == IPStatus.Success)
                    rtts.Add(reply.RoundtripTime);
            }
            catch (PingException)
            {
            }

            Thread.Sleep(100);
        }

        double? jitter = rtts.Count >= 2 ? rtts.Zip(rtts.Skip(1), (a, b) => Math.Abs(b - a)).Average() : null;
        return new PingResult
        {
            Target = target,
            Role = role,
            Sent = count,
            Received = rtts.Count,
            AvgMs = rtts.Count > 0 ? rtts.Average() : null,
            MinMs = rtts.Count > 0 ? rtts.Min() : null,
            MaxMs = rtts.Count > 0 ? rtts.Max() : null,
            JitterMs = jitter,
        };
    }

    // ---- jogos ----

    private IReadOnlyList<GameInstall> Games()
    {
        var result = new List<GameInstall>();
        foreach (var profile in gameProfiles)
        {
            var install = profile.Detect.SteamAppId is { } appId ? SteamLocator.FindApp(appId)?.InstallPath : null;
            var configPath = profile.Config is { } source ? GameLocator.ConfigPath(source) : null;
            // Jogo fora da Steam conta como instalado quando o arquivo de
            // configuração existe (ver GameDetect.ByConfigFile).
            if (install is null && !(profile.Detect.ByConfigFile && configPath is not null))
                continue;

            IReadOnlyDictionary<string, string> config = new Dictionary<string, string>();
            if (configPath is not null)
            {
                try
                {
                    config = ConfigFiles.Parse(profile.Config!.Format, TextFiles.Read(configPath).Text);
                }
                catch (IOException)
                {
                    // Arquivo preso pelo jogo aberto: aparece como "configuração não lida".
                }
            }

            string? exe = null;
            if (install is not null && profile.Detect.Executable is { } rel)
            {
                exe = Path.Combine(install, rel.Replace('/', '\\'));
                if (!File.Exists(exe))
                    exe = null;
            }

            result.Add(new GameInstall
            {
                GameId = profile.Id, Name = profile.Name, InstallPath = install ?? "",
                ConfigPath = configPath, Config = config, ExecutablePath = exe,
            });
        }

        return result;
    }
}
