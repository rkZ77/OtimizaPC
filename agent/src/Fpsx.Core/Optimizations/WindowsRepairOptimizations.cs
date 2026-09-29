using Fpsx.Core.Model;

namespace Fpsx.Core.Optimizations;

/// <summary>
/// Reparo dos arquivos do Windows com as ferramentas da Microsoft: DISM
/// repara a imagem (a cópia de onde o SFC tira os arquivos bons) e o SFC
/// troca os arquivos corrompidos. Nessa ordem: SFC com a imagem estragada
/// "repara" com arquivo estragado.
/// </summary>
public sealed class SystemFilesRepairOptimization : IOptimization
{
    public string Id => "system-files-repair";

    public Evaluation Evaluate(EvaluationContext context) => new()
    {
        Decision = Decision.Optional,
        Potential = Potential.None,
        Reason = "Repara arquivos do Windows corrompidos (tela azul, programa que fecha sozinho, erro de DLL, Windows Update que falha). Não aumenta FPS.",
        Warning = "Pode levar de 10 a 30 minutos e usa a internet para baixar os arquivos originais. Não desligue o PC no meio.",
        Proposals =
        [
            new Proposal(Id, "Reparar os arquivos do Windows",
                [new SystemRepairChange(SystemRepairKind.ImageRestoreHealth), new SystemRepairChange(SystemRepairKind.SystemFileScan)],
                Potential.None, "Troubleshooting do Windows."),
        ],
    };
}

/// <summary>
/// Depois de formatar: reinstala os drivers salvos pelo "Preparar formatação".
/// Só existe proposta quando a pessoa escolheu uma pasta de kit já conferida.
/// </summary>
public sealed class DriverKitInstallOptimization : IOptimization
{
    public string Id => "driver-kit-install";

    public Evaluation Evaluate(EvaluationContext context) => context.Snapshot.DriverKitFolder is { Length: > 0 } folder
        ? new Evaluation
        {
            Decision = Decision.Optional,
            Potential = Potential.None,
            Reason = "Reinstala os drivers que este PC tinha antes de formatar, inclusive o de rede, sem precisar de internet.",
            Warning = "A tela pode piscar enquanto os drivers instalam. Reinicie o PC no fim.",
            Proposals = [new Proposal(Id, "Instalar os drivers salvos", [new DriverPackageInstallChange(folder)], Potential.None, "Kit salvo pelo RKZFPS antes de formatar.")],
        }
        : new Evaluation
        {
            Decision = Decision.NotApplicable,
            Reason = "Nenhum kit de drivers escolhido. Depois de formatar, use Reinstalar drivers salvos na página Drivers e reparo.",
        };
}

/// <summary>
/// Drivers novos do Windows Update, instalados pelo próprio Windows Update.
/// Só existe proposta depois que a pessoa procurou (a busca preenche
/// PendingDrivers); nunca entra em seleção automática.
/// </summary>
public sealed class DriverUpdateOptimization : IOptimization
{
    public string Id => "driver-update";

    public Evaluation Evaluate(EvaluationContext context)
    {
        var drivers = context.Snapshot.PendingDrivers.Where(d => d.UpdateId.Length > 0).ToList();
        if (drivers.Count == 0)
            return new Evaluation
            {
                Decision = Decision.NotApplicable,
                Reason = "Nenhum driver novo do Windows Update na última busca. Procure na página Drivers e reparo.",
            };

        return new Evaluation
        {
            Decision = Decision.Optional,
            Potential = Potential.Low,
            Reason = drivers.Count == 1
                ? "O Windows Update tem 1 driver novo para este PC."
                : $"O Windows Update tem {drivers.Count} drivers novos para este PC.",
            Warning = "A tela pode piscar enquanto o driver de vídeo instala. Alguns drivers pedem reinício.",
            Proposals = drivers.Select(d => new Proposal($"{Id}:{d.UpdateId}", d.Title,
                [new DriverInstallChange(d.UpdateId, d.Title)], Potential.Low, "Driver assinado, entregue pelo Windows Update.")).ToList(),
        };
    }
}
