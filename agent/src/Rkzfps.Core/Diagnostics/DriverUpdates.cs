namespace Rkzfps.Core.Diagnostics;

/// <summary>Um driver que o Windows Update oferece para este PC e ainda não está instalado.</summary>
public sealed record DriverUpdate(string Title, string Category, string Manufacturer, DateTime? Date)
{
    /// <summary>Id da atualização no Windows Update: é por ele que o driver é instalado.</summary>
    public string UpdateId { get; init; } = "";
}

public static class DriverUpdates
{
    /// <summary>Resumo para a tela: quantos e quais, em uma frase.</summary>
    public static string Summary(IReadOnlyList<DriverUpdate> drivers)
    {
        if (drivers.Count == 0)
            return "O Windows Update não tem driver novo para este PC agora.";
        var nomes = drivers.Take(4).Select(d => string.IsNullOrWhiteSpace(d.Manufacturer) || d.Title.Contains(d.Manufacturer, StringComparison.OrdinalIgnoreCase)
            ? d.Title : $"{d.Manufacturer} {d.Title}");
        var mais = drivers.Count > 4 ? $" e mais {drivers.Count - 4}" : "";
        return $"{drivers.Count} {(drivers.Count == 1 ? "driver novo disponível" : "drivers novos disponíveis")} no Windows Update: {string.Join("; ", nomes)}{mais}.";
    }
}
