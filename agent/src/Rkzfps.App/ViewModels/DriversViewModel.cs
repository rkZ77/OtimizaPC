using System.Collections.ObjectModel;
using System.Windows.Input;
using Rkzfps.Core.Diagnostics;
using Rkzfps.Core.Model;

namespace Rkzfps.App.ViewModels;

/// <summary>Um driver novo que o Windows Update oferece, com o botão de instalar.</summary>
public sealed record DriverItem(string Title, string Detail, string ProposalId);

/// <summary>
/// Drivers e reparo: a página fixa para driver (procurar e instalar pelo
/// Windows Update, ou abrir o site do fabricante) e para arquivos do Windows
/// corrompidos (verificar só lendo, e reparar com DISM e SFC).
///
/// Instalar e reparar passam pelo ApplyFlow de sempre: plano, confirmação com
/// o que muda, permissão do Windows e resultado no Histórico.
/// </summary>
public sealed class DriversViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;
    private bool _searched;
    private bool _searching;
    private string _searchText = "Procure para ver se o Windows Update tem driver novo para este PC.";
    private SystemHealthReport? _health;

    public DriversViewModel()
    {
        SearchCommand = new AsyncCommand(Search, () => !_searching && !IsBusy);
        InstallCommand = new AsyncCommand(p => Busy(() => Install([((DriverItem)p!).ProposalId])), p => !IsBusy && p is DriverItem);
        InstallAllCommand = new AsyncCommand(() => Busy(() => Install(Drivers.Select(d => d.ProposalId).ToList())), () => !IsBusy && Drivers.Count > 1);
        OpenUrlCommand = new RelayCommand(p => AppHost.OpenUrl((string)p!), p => p is string);
        CheckCommand = new AsyncCommand(() => Busy(Check), () => !IsBusy);
        RepairCommand = new AsyncCommand(() => Busy(() => ApplyFlow.RunAsync(["system-files-repair"], Reporter)), () => !IsBusy);
        ExportCommand = new AsyncCommand(() => Busy(Export), () => !IsBusy);
        RestoreKitCommand = new AsyncCommand(() => Busy(RestoreKit), () => !IsBusy);
        DismissChangesCommand = new RelayCommand(() => _host.DismissHardwareChanges());
        WindowsMediaCommand = new RelayCommand(() => AppHost.OpenUrl(WindowsMediaUrl));
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppHost.Scan))
                Raise(nameof(DriverLinks));
            if (e.PropertyName == nameof(AppHost.HardwareChanges))
            {
                Raise(nameof(HardwareChanges));
                Raise(nameof(HasHardwareChanges));
            }
        };
    }

    public override string Title => "Drivers e reparo";

    /// <summary>Ferramenta (Segoe MDL2 Assets).</summary>
    public override string Icon => "";

    // Primeira vez na página: já procura. É para isso que a pessoa veio.
    public override void OnShown()
    {
        if (!_searched && SearchCommand.CanExecute(null))
            SearchCommand.Execute(null);
    }

    public ICommand SearchCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand InstallAllCommand { get; }
    public ICommand OpenUrlCommand { get; }
    public ICommand CheckCommand { get; }
    public ICommand RepairCommand { get; }

    public ObservableCollection<DriverItem> Drivers { get; } = [];
    public bool HasDrivers => Drivers.Count > 0;
    public bool HasManyDrivers => Drivers.Count > 1;

    public string SearchText
    {
        get => _searchText;
        private set => Set(ref _searchText, value);
    }

    /// <summary>Atualizador e site oficial do fabricante (o driver de vídeo do fabricante sai antes do Windows Update).</summary>
    public IReadOnlyList<FindingAction> DriverLinks =>
        _host.Scan is { } scan ? Rkzfps.Core.Diagnostics.DriverLinks.For(scan.Snapshot).Where(a => a.IsAllowed).ToList() : [];

    private async Task Search()
    {
        _searching = true;
        _searched = true;
        SearchText = "Procurando no Windows Update. Pode levar até um minuto...";
        try
        {
            var found = await Rkzfps.Windows.WindowsUpdateDrivers.SearchAsync();
            _host.SetPendingDrivers(found);
            Fill(found);
            SearchText = found.Count == 0
                ? "O Windows Update não tem driver novo para este PC agora. Para o de vídeo, o site do fabricante costuma ter versão antes: use os botões abaixo."
                : DriverUpdates.Summary(found);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            SearchText = "Não deu para falar com o Windows Update agora. Confira a internet e tente de novo.";
        }
        finally
        {
            _searching = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void Fill(IReadOnlyList<DriverUpdate> found)
    {
        Drivers.Clear();
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        foreach (var d in found.Where(d => d.UpdateId.Length > 0))
        {
            var parts = new[] { d.Manufacturer, d.Category, d.Date is { } date ? "versão de " + date.ToString("dd/MM/yyyy", pt) : "" }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            Drivers.Add(new DriverItem(d.Title, string.Join(", ", parts), $"driver-update:{d.UpdateId}"));
        }
        Raise(nameof(HasDrivers));
        Raise(nameof(HasManyDrivers));
    }

    private async Task Install(IReadOnlyList<string> ids)
    {
        var session = await ApplyFlow.RunAsync(ids, Reporter);
        // Instalou (ou tentou): pergunta de novo, para a lista mostrar só o que falta.
        if (session is not null)
            await Search();
    }

    // ---- troca de peça e formatação ----

    /// <summary>Página oficial da Microsoft para criar o pendrive de instalação do Windows 11.</summary>
    public const string WindowsMediaUrl = "https://www.microsoft.com/pt-br/software-download/windows11";

    public ICommand ExportCommand { get; }
    public ICommand RestoreKitCommand { get; }
    public ICommand DismissChangesCommand { get; }
    public ICommand WindowsMediaCommand { get; }

    public IReadOnlyList<HardwareAdvice> HardwareChanges => _host.HardwareChanges;
    public bool HasHardwareChanges => _host.HardwareChanges.Count > 0;

    private string _kitText = "";

    /// <summary>Resultado do último salvar ou reinstalar kit.</summary>
    public string KitText
    {
        get => _kitText;
        private set => Set(ref _kitText, value);
    }

    private static string? PickFolder(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private async Task Export()
    {
        if (PickFolder("Onde salvar os drivers (pendrive ou outro disco)") is not { } folder)
            return;
        // O disco do Windows é o que a formatação apaga: salvar nele não serve.
        var systemRoot = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        if (folder.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase)
            && !Dialogs.Confirm("Pasta no disco do Windows",
                $"A pasta escolhida está em {systemRoot}, o disco que a formatação apaga. Salve num pendrive ou em outro disco.\n\nSalvar aqui mesmo assim (para copiar depois)?", "Salvar aqui"))
            return;

        var result = await _host.ExportDriversAsync(folder, Reporter);
        if (result is null)
        {
            Dialogs.Info("Permissão recusada", "Sem a permissão do Windows, os drivers não foram salvos. Nada foi mudado.");
            return;
        }

        KitText = result.Message + (result.StorageWarning is { } w ? "\n\n" + w : "");
        if (result.Ok)
            Dialogs.Info("Drivers salvos", KitText + "\n\nNa pasta também ficou um LEIA-ME com as peças deste PC e o passo a passo.");
    }

    private async Task RestoreKit()
    {
        if (PickFolder("Escolha a pasta \"RKZFPS drivers\" salva antes de formatar") is not { } folder)
            return;
        if (_host.SetDriverKit(folder) is { } problem)
        {
            KitText = problem;
            Dialogs.Info("Kit de drivers", problem);
            return;
        }

        var session = await ApplyFlow.RunAsync(["driver-kit-install"], Reporter);
        if (session is not null)
            KitText = "Drivers do kit instalados. Reinicie o PC para concluir.";
    }

    // ---- arquivos do Windows ----

    public bool HasHealth => _health is not null;
    public string HealthTitle => _health?.Title ?? "Verificar arquivos do Windows";

    public string HealthText => _health?.Detail
        ?? "Confere se a imagem do Windows e o disco do sistema têm arquivo corrompido. Só lê, não muda nada. Leva de 5 a 15 minutos e o Windows pede permissão.";

    /// <summary>Reparar aparece quando a verificação achou algo, ou quando não conseguiu concluir.</summary>
    public bool CanRepair => _health is { Status: HealthStatus.Problem or HealthStatus.Unknown, ImageBroken: false };

    private async Task Check()
    {
        var report = await _host.CheckSystemAsync(Reporter);
        if (report is null)
        {
            Dialogs.Info("Permissão recusada", "Sem a permissão do Windows, a verificação dos arquivos não roda. Nada foi mudado.");
            return;
        }

        _health = report;
        foreach (var n in new[] { nameof(HasHealth), nameof(HealthTitle), nameof(HealthText), nameof(CanRepair) })
            Raise(n);
    }
}
