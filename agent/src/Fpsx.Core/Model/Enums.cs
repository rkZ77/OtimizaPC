namespace Fpsx.Core.Model;

/// <summary>Classificação obrigatória de toda otimização (spec, seção 4).</summary>
public enum Classification
{
    Proven,
    Conditional,
    Troubleshooting,
    Experimental,
    NotRecommended,
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Potencial qualitativo. Não existe "+X FPS" em lugar nenhum do Agent: número
/// de ganho só sai do BenchmarkEngine, medido.
/// </summary>
public enum Potential
{
    None,
    Low,
    Moderate,
    High,
}

public enum HealthStatus
{
    Ok,
    Info,
    Attention,
    Problem,
    Unknown,
}

public enum Decision
{
    /// <summary>Não se aplica a este PC (hardware, SO, jogo ausente).</summary>
    NotApplicable,

    /// <summary>Já está na configuração certa. Nenhuma alteração.</summary>
    AlreadyOptimal,

    /// <summary>Há benefício esperado e o PC atende aos critérios.</summary>
    Recommended,

    /// <summary>Disponível só por escolha do usuário (troubleshooting, startup, experimental).</summary>
    Optional,

    /// <summary>Aplicável, mas impedido (sem admin, desativado pelo admin, perfil não permite).</summary>
    Blocked,

    /// <summary>O Agent não conseguiu ler o estado atual: não altera o que não enxerga.</summary>
    Unknown,
}

public enum RegistryRoot
{
    CurrentUser,
    LocalMachine,
}

public enum RegistryKind
{
    DWord,
    QWord,
    String,
    Binary,

    /// <summary>REG_MULTI_SZ. Data guarda as linhas separadas por \n.</summary>
    MultiString,
}

public enum CacheTarget
{
    DirectXShaderCache,
    NvidiaDxCache,
    AmdDxCache,
    SteamShaderCacheCs2,

    /// <summary>Arquivos temporários do usuário com mais de 24 h.</summary>
    UserTemp,
}

public enum NetworkRepairKind
{
    FlushDns,
    WinsockReset,
}

/// <summary>Reparos do Windows com as ferramentas oficiais da Microsoft. Lista fechada.</summary>
public enum SystemRepairKind
{
    /// <summary>DISM RestoreHealth: repara a imagem do Windows (a fonte que o SFC usa).</summary>
    ImageRestoreHealth,

    /// <summary>SFC /scannow: troca arquivos de sistema corrompidos pelos originais.</summary>
    SystemFileScan,
}

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
}

public enum MediaKind
{
    Unknown,
    Hdd,
    Ssd,
}
