namespace Fpsx.Core.Model;

// Tudo que o Agent sabe sobre o PC num instante. Campo nullable significa
// "não consegui ler", e as regras tratam isso como Unknown em vez de supor o
// valor padrão do Windows: supor é o jeito mais fácil de mentir no relatório.

public sealed record SystemSnapshot
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public bool IsElevated { get; init; }
    public OsInfo Os { get; init; } = new();
    public CpuInfo? Cpu { get; init; }
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
    public MemoryInfo? Memory { get; init; }
    public IReadOnlyList<DiskInfo> Disks { get; init; } = [];
    public PowerInfo? Power { get; init; }
    public GamingFeatures Gaming { get; init; } = new();
    public IReadOnlyList<StartupEntry> Startup { get; init; } = [];
    public IReadOnlyList<ProcessSample> Processes { get; init; } = [];
    public IReadOnlyList<DisplayInfo> Displays { get; init; } = [];
    public NetworkInfo? Network { get; init; }
    public SecurityInfo? Security { get; init; }
    public IReadOnlyList<GameInstall> Games { get; init; } = [];

    /// <summary>Tempo de amostragem de CPU/processos, para o relatório dizer "no momento do scan".</summary>
    public double SampleSeconds { get; init; }
}

public sealed record OsInfo
{
    public string Caption { get; init; } = "";
    public string Version { get; init; } = "";
    public int Build { get; init; }
    public bool IsWindows11 => Build >= 22000;
}

public sealed record CpuInfo
{
    public string Name { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public int Cores { get; init; }
    public int Threads { get; init; }
    public int MaxClockMhz { get; init; }

    /// <summary>Uso médio total no período amostrado (0-100).</summary>
    public double? AvgUsagePercent { get; init; }

    /// <summary>
    /// Média do contador "% Performance Limit": abaixo de 100 o processador
    /// está sendo limitado por temperatura, energia ou firmware.
    /// </summary>
    public double? AvgPerformanceLimitPercent { get; init; }

    public double? MinPerformanceLimitPercent { get; init; }

    /// <summary>Verdadeiro quando a amostra foi feita com carga (teste de CPU), não em repouso.</summary>
    public bool SampledUnderLoad { get; init; }

    public double? TemperatureC { get; init; }
}

public sealed record GpuInfo
{
    public string Name { get; init; } = "";
    public GpuVendor Vendor { get; init; }
    public string DriverVersion { get; init; } = "";
    public DateTime? DriverDate { get; init; }
    public long? VramBytes { get; init; }
    public bool LikelyIntegrated { get; init; }
}

public sealed record MemoryInfo
{
    public long TotalBytes { get; init; }
    public long AvailableBytes { get; init; }
    public long? CommitLimitBytes { get; init; }
    public long? CommittedBytes { get; init; }

    /// <summary>null = não lido; false = sem nenhum arquivo de paginação configurado.</summary>
    public bool? PagefilePresent { get; init; }

    public bool? PagefileAutomatic { get; init; }

    public double UsedPercent => TotalBytes <= 0 ? 0 : 100.0 * (TotalBytes - AvailableBytes) / TotalBytes;
}

public sealed record DiskInfo
{
    public string DriveLetter { get; init; } = "";
    public string Label { get; init; } = "";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public MediaKind Media { get; init; }
    public string BusType { get; init; } = "";
    public string Health { get; init; } = "";

    /// <summary>Estado operacional reportado pelo disco (ex.: "Predictive Failure" do SMART).</summary>
    public string OperationalStatus { get; init; } = "";

    public string Model { get; init; } = "";
    public bool IsSystemDrive { get; init; }

    public double FreePercent => TotalBytes <= 0 ? 0 : 100.0 * FreeBytes / TotalBytes;
}

public sealed record PowerScheme(string Guid, string Name);

public sealed record PowerInfo
{
    public string? ActiveSchemeGuid { get; init; }
    public string? ActiveSchemeName { get; init; }
    public bool HasBattery { get; init; }
    public bool? OnAcPower { get; init; }
    public IReadOnlyList<PowerScheme> Schemes { get; init; } = [];
}

public sealed record GamingFeatures
{
    /// <summary>Valor cru de AutoGameModeEnabled. null = valor ausente (padrão do Windows 11: ligado).</summary>
    public int? AutoGameModeValue { get; init; }

    /// <summary>AppCaptureEnabled (captura de jogos pela Game Bar).</summary>
    public int? AppCaptureValue { get; init; }

    /// <summary>HistoricalCaptureEnabled: "Gravar o que aconteceu" em segundo plano.</summary>
    public int? BackgroundRecordingValue { get; init; }

    /// <summary>HwSchMode: 2 = HAGS ligado, 1 = desligado, null = sem suporte ou chave ausente.</summary>
    public int? HagsValue { get; init; }

    /// <summary>EnableTransparency do tema. null = valor ausente (padrão do Windows: ligado).</summary>
    public int? TransparencyValue { get; init; }

    /// <summary>Texto cru de DirectXUserGlobalSettings ("SwapEffectUpgradeEnable=1;..."). null = ausente.</summary>
    public string? DirectXGlobalSettings { get; init; }

    /// <summary>Preferência de GPU por executável de jogo detectado (caminho -> texto cru). Só dos jogos com perfil.</summary>
    public IReadOnlyDictionary<string, string> GpuPreferences { get; init; } = new Dictionary<string, string>();
}

public sealed record StartupEntry
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";

    /// <summary>Origem: HKCU_RUN, HKLM_RUN, HKLM_RUN32, STARTUP_FOLDER_USER, STARTUP_FOLDER_COMMON.</summary>
    public string Location { get; init; } = "";

    public bool Enabled { get; init; }

    /// <summary>Bytes crus do StartupApproved (hex), null quando o valor não existe.</summary>
    public string? ApprovedRawHex { get; init; }

    /// <summary>RAM do processo correspondente se ele estiver rodando agora.</summary>
    public long? RunningWorkingSetBytes { get; init; }
}

public sealed record ProcessSample
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public double CpuPercent { get; init; }
    public long WorkingSetBytes { get; init; }
}

public sealed record DisplayInfo
{
    public string DeviceName { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public bool IsPrimary { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int CurrentHz { get; init; }
    public int MaxHzAtCurrentResolution { get; init; }
}

public sealed record PingResult
{
    public string Target { get; init; } = "";
    public string Role { get; init; } = "";
    public int Sent { get; init; }
    public int Received { get; init; }
    public double? AvgMs { get; init; }
    public double? MinMs { get; init; }
    public double? MaxMs { get; init; }
    public double? JitterMs { get; init; }

    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;
}

public sealed record NetworkInfo
{
    public string AdapterName { get; init; } = "";

    /// <summary>ETHERNET, WIFI, OTHER ou NONE.</summary>
    public string AdapterType { get; init; } = "NONE";

    public long? LinkSpeedMbps { get; init; }
    public IReadOnlyList<PingResult> Pings { get; init; } = [];
}

public sealed record SecurityInfo
{
    public bool? MemoryIntegrityEnabled { get; init; }
    public bool? VbsRunning { get; init; }
}

public sealed record GameInstall
{
    public string GameId { get; init; } = "";
    public string Name { get; init; } = "";
    public string InstallPath { get; init; } = "";
    public string? ConfigPath { get; init; }
    public IReadOnlyDictionary<string, string> Config { get; init; } = new Dictionary<string, string>();

    /// <summary>Executável do jogo quando a pasta de instalação é conhecida (usado na preferência de GPU).</summary>
    public string? ExecutablePath { get; init; }
}
