using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpsx.Core.Engine;

namespace Fpsx.App.ViewModels;

/// <summary>
/// Histórico e desfazer. Desfazer funciona em TODOS os planos; o histórico
/// completo (sessões já totalmente desfeitas) é do Starter em diante.
/// </summary>
public sealed class HistoryViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;

    public HistoryViewModel()
    {
        UndoSessionCommand = new AsyncCommand(p => Busy(() => Undo((SessionItem)p!, null)), p => !IsBusy && p is SessionItem s && s.CanUndo);
        UndoChangeCommand = new AsyncCommand(p => Busy(() => UndoChange((ChangeItem)p!)), p => !IsBusy && p is ChangeItem c && c.CanUndo);
        UndoAllCommand = new AsyncCommand(() => Busy(UndoAll), () => !IsBusy && Sessions.Any(s => s.CanUndo));
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppHost.Scan) or nameof(AppHost.License))
                Load();
        };
        Load();
    }

    public override string Title => "Histórico";

    public ICommand UndoSessionCommand { get; }
    public ICommand UndoChangeCommand { get; }
    public ICommand UndoAllCommand { get; }
    public ObservableCollection<SessionItem> Sessions { get; } = [];
    public bool FullHistory => _host.Allows(Feature.History);
    public string PlanNote => FullHistory ? "" : $"Você pode desfazer qualquer alteração em qualquer plano. O histórico completo faz parte do plano {Plans.Label(PlanFeatures.RequiredPlan(Feature.History))}.";
    public bool IsEmpty => Sessions.Count == 0;

    public override void OnShown() => Load();

    private void Load()
    {
        Sessions.Clear();
        foreach (var s in _host.Ctx.Store.All().Select(s => new SessionItem(s)).Where(s => FullHistory || s.CanUndo))
            Sessions.Add(s);
        Raise(nameof(FullHistory));
        Raise(nameof(PlanNote));
        Raise(nameof(IsEmpty));
    }

    private async Task Undo(SessionItem item, string? changeId)
    {
        if (!Dialogs.Confirm("Desfazer", "O FPSX vai restaurar os valores salvos no backup desta sessão.", "Desfazer"))
            return;
        await Report(await _host.RollbackAsync(item.Session.Id, changeId, force: false));
    }

    private async Task UndoChange(ChangeItem change) =>
        await Report(await _host.RollbackAsync(change.SessionId, change.Id, force: false));

    private async Task UndoAll()
    {
        if (!Dialogs.Confirm("Desfazer tudo", "Desfazer TODAS as alterações que o FPSX fez neste PC?", "Desfazer tudo"))
            return;
        foreach (var s in Sessions.Where(s => s.CanUndo).ToList())
            await _host.RollbackAsync(s.Session.Id, null, force: false);
        Load();
        Dialogs.Info("Pronto", "As alterações foram desfeitas. Itens modificados depois por você ou por outro programa foram mantidos.");
    }

    private async Task Report(SessionRecord result)
    {
        Load();
        // Só o que tem inverso pode ser forçado; limpeza de cache fica como está.
        var kept = result.Changes.Where(c => c.Status is ChangeStatus.RollbackSkipped or ChangeStatus.RollbackFailed).ToList();
        if (kept.Count == 0)
        {
            Dialogs.Info("Desfeito", "Tudo voltou ao estado anterior.");
            return;
        }

        var text = string.Join("\n\n", kept.Select(c => $"{c.Applied.Describe()}\n{c.Error}"));
        if (kept.Any(c => c.Inverse is not null)
            && Dialogs.Show("Alguns itens foram mantidos", text + "\n\nRestaurar mesmo assim, sobrescrevendo a mudança feita depois?", "Restaurar mesmo assim", "Manter") == 0)
        {
            await _host.RollbackAsync(result.Id, null, force: true);
            Load();
        }
        else if (kept.All(c => c.Inverse is null))
        {
            Dialogs.Info("Itens sem desfazer", text);
        }
    }
}
