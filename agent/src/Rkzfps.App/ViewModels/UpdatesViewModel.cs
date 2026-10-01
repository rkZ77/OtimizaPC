using System.Windows.Input;
using Rkzfps.Client;

namespace Rkzfps.App.ViewModels;

/// <summary>
/// Atualizações, no desenho do Windows Update: a versão instalada, quando
/// verificou por último, um botão para verificar agora e, se houver versão
/// nova, o que ela traz e o botão de instalar com o progresso do download.
///
/// A verificação automática continua (ao abrir e a cada 6 horas); esta tela é
/// o lugar fixo para a pessoa conferir e mandar atualizar quando quiser. Só
/// instala versão com assinatura e SHA-256 conferidos (Updater).
/// </summary>
public sealed class UpdatesViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;
    private bool _checking;
    private string _installLabel = "Baixar e instalar";

    public UpdatesViewModel()
    {
        CheckCommand = new AsyncCommand(Check, () => !_checking && !Installing);
        InstallCommand = new AsyncCommand(Install, () => _host.Update is not null && !Installing);
        AllVersionsCommand = new RelayCommand(() => AppHost.OpenUrl(_host.SiteUrl + "/download"));
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppHost.Update) or nameof(AppHost.LastUpdateCheck) or nameof(AppHost.LastUpdateCheckFailed))
                RaiseAll();
        };
    }

    public override string Title => "Atualizações";

    /// <summary>Sincronizar (Segoe MDL2 Assets).</summary>
    public override string Icon => "";

    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand AllVersionsCommand { get; }

    public string CurrentVersion => $"Versão instalada: RKZFPS {AgentContext.Version}";

    public bool HasUpdate => _host.Update is not null;

    public string StatusTitle =>
        _checking ? "Procurando atualizações..."
        : _host.Update is { } u ? $"RKZFPS {u.Version} disponível"
        : _host.LastUpdateCheckFailed && _host.LastUpdateCheck is null ? "Não foi possível verificar"
        : _host.LastUpdateCheck is null ? "Ainda não verificado"
        : "Você está com a versão mais recente";

    public string StatusText
    {
        get
        {
            if (_checking)
                return "Conferindo com o servidor do RKZFPS.";
            var quando = _host.LastUpdateCheck is { } t ? $"Última verificação: {Quando(t)}." : "";
            if (_host.LastUpdateCheckFailed)
                return $"Sem resposta do servidor agora. Confira a internet e tente de novo. {quando}".Trim();
            if (_host.Update is not null)
                return $"Instale quando quiser: leva menos de um minuto e o RKZFPS abre de novo sozinho. {quando}".Trim();
            return string.IsNullOrEmpty(quando) ? "Toque em Verificar agora." : quando;
        }
    }

    /// <summary>O que a versão nova traz (texto publicado junto com ela).</summary>
    public string Notes => _host.Update?.Notes ?? "";

    public bool Installing { get; private set; }

    public string InstallLabel
    {
        get => _installLabel;
        private set => Set(ref _installLabel, value);
    }

    public string AutoInfo =>
        "O RKZFPS procura atualizações sozinho ao abrir e a cada 6 horas, mesmo na bandeja, e avisa quando sai versão nova. " +
        "Antes de instalar, ele confere a assinatura e o código do arquivo: um instalador alterado no caminho é recusado.";

    public override void OnShown()
    {
        // Abrir a tela já confere de novo, como o Windows Update.
        if (!_checking && !Installing && (_host.LastUpdateCheck is null || DateTimeOffset.Now - _host.LastUpdateCheck > TimeSpan.FromMinutes(10)))
            _ = Check();
    }

    private async Task Check()
    {
        _checking = true;
        RaiseAll();
        try
        {
            await _host.CheckUpdateAsync();
        }
        finally
        {
            _checking = false;
            RaiseAll();
        }
    }

    private async Task Install()
    {
        Installing = true;
        RaiseAll();
        try
        {
            InstallLabel = "Baixando...";
            await _host.UpdateNowAsync(new Progress<int>(p => InstallLabel = $"Baixando {p}%"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or System.IO.IOException)
        {
            Dialogs.Info("Não foi possível atualizar", ex.Message + "\n\nO RKZFPS continua funcionando na versão atual. Você também pode baixar pelo site.");
        }
        finally
        {
            Installing = false;
            InstallLabel = "Baixar e instalar";
            RaiseAll();
        }
    }

    private static string Quando(DateTimeOffset t)
    {
        var local = t.ToLocalTime();
        return local.Date == DateTime.Today ? $"hoje às {local:HH:mm}" : $"{local:dd/MM} às {local:HH:mm}";
    }

    private void RaiseAll()
    {
        foreach (var n in new[] { nameof(HasUpdate), nameof(StatusTitle), nameof(StatusText), nameof(Notes), nameof(Installing) })
            Raise(n);
        CommandManager.InvalidateRequerySuggested();
    }
}
