using System.Text.Json.Serialization;

namespace Fpsx.Core.Model;

// As únicas operações que o Agent sabe executar. Não existe "rodar comando"
// genérico: cada tipo aqui tem um executor próprio, validado pela SafetyPolicy.
// Um catálogo adulterado ou uma resposta de servidor pode no máximo escolher
// entre estes tipos, nunca inventar um novo.

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RegistryValueChange), "registry")]
[JsonDerivedType(typeof(PowerSchemeChange), "power_scheme")]
[JsonDerivedType(typeof(DisplayRefreshChange), "display_refresh")]
[JsonDerivedType(typeof(CacheClearChange), "cache_clear")]
[JsonDerivedType(typeof(NetworkRepairChange), "network_repair")]
[JsonDerivedType(typeof(ProcessCloseChange), "process_close")]
[JsonDerivedType(typeof(GameConfigChange), "game_config")]
public abstract record Change
{
    /// <summary>Texto curto em pt-BR para o usuário ver antes de confirmar.</summary>
    public abstract string Describe();

    /// <summary>Ações sem estado anterior restaurável (apagar cache, reset de rede).</summary>
    [JsonIgnore]
    public virtual bool Reversible => true;

    [JsonIgnore]
    public virtual bool RequiresAdmin => false;

    [JsonIgnore]
    public virtual bool RequiresReboot => false;
}

public sealed record RegValue(RegistryKind Kind, string Data)
{
    public static RegValue DWord(int value) => new(RegistryKind.DWord, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public override string ToString() => $"{Kind}:{Data}";
}

/// <summary>Escreve (ou apaga, quando Value é null) um valor de registro.</summary>
/// <param name="Label">Descrição para o usuário; sem ela, a tela mostra a chave técnica.</param>
public sealed record RegistryValueChange(RegistryRoot Root, string Path, string Name, RegValue? Value, bool NeedsReboot = false, string? Label = null) : Change
{
    public override bool RequiresAdmin => Root == RegistryRoot.LocalMachine;

    public override bool RequiresReboot => NeedsReboot;

    public string FullPath => (Root == RegistryRoot.CurrentUser ? "HKCU\\" : "HKLM\\") + Path;

    public override string Describe() => Label ?? (Value is null
        ? $"Remover valor {FullPath}\\{Name}"
        : $"Definir {FullPath}\\{Name} = {Value.Data}");
}

public sealed record PowerSchemeChange(string SchemeGuid, string SchemeName) : Change
{
    public override string Describe() => $"Ativar plano de energia \"{SchemeName}\"";
}

public sealed record DisplayRefreshChange(string DeviceName, int Width, int Height, int RefreshHz) : Change
{
    public override string Describe() => $"Ajustar {DeviceName} para {RefreshHz} Hz em {Width}x{Height}";
}

public sealed record CacheClearChange(CacheTarget Target) : Change
{
    public override bool Reversible => false;

    public override string Describe() => Target switch
    {
        CacheTarget.DirectXShaderCache => "Limpar o cache de shaders do DirectX",
        CacheTarget.NvidiaDxCache => "Limpar o cache de shaders da NVIDIA",
        CacheTarget.AmdDxCache => "Limpar o cache de shaders da AMD",
        CacheTarget.SteamShaderCacheCs2 => "Limpar o cache de shaders do CS2 na Steam",
        CacheTarget.UserTemp => "Apagar arquivos temporários com mais de 24 horas",
        _ => "Limpar cache",
    };
}

/// <summary>
/// Pede para um programa fechar, como o botão X da janela. Nunca força o
/// encerramento: forçar pode perder trabalho não salvo do usuário.
/// </summary>
public sealed record ProcessCloseChange(int Pid, string Name) : Change
{
    public override bool Reversible => false;

    public override string Describe() => $"Fechar {Name} (PID {Pid})";
}

/// <summary>Altera uma chave do arquivo de configuração de vídeo do jogo. O jogo precisa estar fechado.</summary>
public sealed record GameConfigChange(string GameId, string Key, string Value) : Change
{
    public override string Describe() => $"Definir {Key} = {Value} na configuração de {GameId}";
}

public sealed record NetworkRepairChange(NetworkRepairKind Repair) : Change
{
    public override bool Reversible => false;

    public override bool RequiresAdmin => Repair == NetworkRepairKind.WinsockReset;

    public override bool RequiresReboot => Repair == NetworkRepairKind.WinsockReset;

    public override string Describe() => Repair switch
    {
        NetworkRepairKind.FlushDns => "Limpar o cache de DNS do Windows",
        NetworkRepairKind.WinsockReset => "Redefinir o catálogo Winsock (exige reinício)",
        _ => "Reparo de rede",
    };
}
