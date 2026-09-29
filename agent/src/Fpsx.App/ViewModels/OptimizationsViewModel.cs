using Fpsx.Core.Engine;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpsx.Core.Model;

namespace Fpsx.App.ViewModels;

/// <summary>
/// Grupo da tela. Os de consulta (já otimizado, reparos, não se aplicam) nascem
/// fechados e, abertos, viram lista simples: eram 20 cartões grandes do que
/// já estava certo empurrando para baixo o que a pessoa veio fazer.
/// </summary>
public sealed class OptimizationGroup : ObservableObject
{
    private bool _isOpen;

    public OptimizationGroup(string title, string hint, IReadOnlyList<OptimizationItem> items, bool collapsible)
    {
        Title = title;
        Hint = hint;
        Items = items;
        Collapsible = collapsible;
        _isOpen = !collapsible;
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen);
    }

    public string Title { get; }
    public string Hint { get; }
    public IReadOnlyList<OptimizationItem> Items { get; }

    /// <summary>Grupo de consulta: fechado por padrão, com linhas simples em vez de cartões.</summary>
    public bool Collapsible { get; }

    public bool ShowCards => !Collapsible && IsOpen;
    public bool ShowRows => Collapsible && IsOpen;

    public string Header => Collapsible ? $"{Title} ({Items.Count})" : Title;

    /// <summary>Seta do grupo fechável (Segoe MDL2: ChevronDown / ChevronRight).</summary>
    public string Chevron => IsOpen ? "\uE70D" : "\uE76C";

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (Set(ref _isOpen, value))
                foreach (var n in new[] { nameof(ShowCards), nameof(ShowRows), nameof(Chevron) })
                    Raise(n);
        }
    }

    public ICommand ToggleCommand { get; }
}

/// <summary>
/// FPS Boost: cada otimização é uma chave. Ligar aplica (com a confirmação e o
/// backup de sempre), desligar desfaz pelo Histórico. O que já estava certo,
/// os reparos e o que não se aplica ficam em grupos fechados no fim.
/// </summary>
public sealed class OptimizationsViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;

    public OptimizationsViewModel()
    {
        ApplyCommand = new AsyncCommand(() => Busy(Apply), () => !IsBusy && Selected().Count > 0);
        ScanCommand = new AsyncCommand(() => Busy(() => _host.RunScanAsync(Reporter)), () => !IsBusy);
        // O fluxo de aplicar já explica o bloqueio e oferece a saída.
        UnlockCommand = new AsyncCommand(p => ApplyFlow.RunAsync([((OptimizationItem)p!).Id], Reporter), p => p is OptimizationItem);
        SwitchCommand = new AsyncCommand(p => Busy(() => Switch((OptimizationItem)p!)), p => !IsBusy && p is OptimizationItem);
        // Ação e reparo de uma vez só: roda todos os itens da otimização (vários programas para fechar, por exemplo).
        RepairCommand = new AsyncCommand(p => Busy(() => ApplyFlow.RunAsync(((OptimizationItem)p!).Proposals.Select(x => x.Id).ToList(), Reporter)),
            p => !IsBusy && p is OptimizationItem { Proposals.Count: > 0 });
        PickCommand = new RelayCommand(p => ((OptimizationItem)p!).Picking ^= true, p => p is OptimizationItem);
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppHost.Scan))
                Load();
        };
        Load();
    }

    public override string Title => "FPS Boost";

    public override string Icon => "\uE90F";

    // Chave é o jeito mais simples de usar: fica no modo simples também.
    public override bool AdvancedOnly => false;

    public ICommand ApplyCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand UnlockCommand { get; }
    public ICommand SwitchCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand PickCommand { get; }
    public ObservableCollection<OptimizationGroup> Groups { get; } = [];
    public bool HasScan => _host.Scan is not null;

    /// <summary>Uma linha no topo: quantas chaves estão ligadas e quantas dá para ligar.</summary>
    public string Summary
    {
        get
        {
            var all = Groups.SelectMany(g => g.Items).ToList();
            var on = all.Count(i => i.Boost.Kind == BoostKind.On);
            var off = all.Count(i => i.Boost.Kind is BoostKind.Off or BoostKind.Pick or BoostKind.Action);
            var certas = all.Count(i => i.Boost.Kind == BoostKind.AlreadyRight);
            if (on + off == 0)
                return $"Nada para ligar agora: {certas} {(certas == 1 ? "item já estava certo" : "itens já estavam certos")}.";
            return $"{on} {(on == 1 ? "ligada" : "ligadas")}, {off} para ligar e {certas} já {(certas == 1 ? "estava certa" : "estavam certas")}.";
        }
    }

    private void Load()
    {
        Groups.Clear();
        if (_host.Scan is { } scan)
        {
            var history = _host.Ctx.Store.All();
            var items = scan.Optimizations
                .Where(o => o.Definition.Classification != Classification.NotRecommended)
                .Select(o => new OptimizationItem(o, history)).ToList();

            void Add(string title, string hint, Func<OptimizationItem, bool> filter, bool collapsible = false)
            {
                var list = items.Where(filter).ToList();
                if (list.Count > 0)
                    Groups.Add(new OptimizationGroup(title, hint, list, collapsible));
            }

            // Primeiro o que dá para ligar agora, depois o que já está ligado e,
            // por último, o que pede plano: quem abre a tela quer agir.
            static int Ordem(OptimizationItem i) => i.Boost.Kind switch
            {
                BoostKind.Off => 0, BoostKind.Action => 1, BoostKind.Pick => 2, BoostKind.On => 3, _ => 4,
            };
            var main = items.Where(i => i.Boost.Kind is BoostKind.Off or BoostKind.Action or BoostKind.Pick or BoostKind.On or BoostKind.Locked)
                .OrderBy(Ordem).ToList();
            if (main.Count > 0)
                Groups.Add(new OptimizationGroup("Otimizações deste PC", "", main, collapsible: false));
            Add("Já estava certo", "O Windows já estava na configuração certa. O RKZFPS não mexe no que já está bom.", i => i.Boost.Kind == BoostKind.AlreadyRight, collapsible: true);
            Add("Reparos (não aumentam FPS)", "Só para quando o problema descrito em cada um acontecer: travadas estranhas depois de atualizar o jogo ou o driver, sites que não abrem, internet que caiu depois de remover VPN.", i => i.Boost.Kind == BoostKind.Repair, collapsible: true);
            Add("Não se aplicam a este PC", "Hardware, sistema ou jogo não atendem aos critérios.", i => i.Boost.Kind == BoostKind.NotApplicable, collapsible: true);
        }

        Raise(nameof(HasScan));
        Raise(nameof(Summary));
    }

    private List<string> Selected() =>
        Groups.SelectMany(g => g.Items).SelectMany(i => i.Proposals).Where(p => p.IsSelected && p.Actionable).Select(p => p.Id).ToList();

    private Task Apply() => ApplyFlow.RunAsync(Selected(), Reporter);

    /// <summary>
    /// Ligar aplica pelo fluxo de sempre (confirmação, backup, permissão do
    /// Windows quando precisa). Desligar desfaz cada alteração ativa daquela
    /// otimização e analisa de novo, para a chave mostrar o estado real.
    /// </summary>
    private async Task Switch(OptimizationItem item)
    {
        switch (item.Boost.Kind)
        {
            case BoostKind.Off:
                await ApplyFlow.RunAsync([item.Boost.ProposalId!], Reporter);
                break;
            case BoostKind.Locked:
                await ApplyFlow.RunAsync([item.Id], Reporter);
                break;
            case BoostKind.On:
                try
                {
                    foreach (var c in item.Boost.Active)
                        await _host.RollbackAsync(c.SessionId, c.ChangeId, force: false);
                }
                catch (InvalidOperationException ex)
                {
                    Dialogs.Info("Não deu para desligar", ex.Message);
                }
                await _host.RunScanAsync(Reporter, network: false);
                break;
        }
    }
}
