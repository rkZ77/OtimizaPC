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

/// <summary>Todas as otimizações, separadas pelo que o motor decidiu, com escolha item a item.</summary>
public sealed class OptimizationsViewModel : PageViewModel
{
    private readonly AppHost _host = AppHost.Current;

    public OptimizationsViewModel()
    {
        ApplyCommand = new AsyncCommand(() => Busy(Apply), () => !IsBusy && Selected().Count > 0);
        ScanCommand = new AsyncCommand(() => Busy(() => _host.RunScanAsync(Reporter)), () => !IsBusy);
        // O fluxo de aplicar já explica o bloqueio e oferece a saída.
        UnlockCommand = new AsyncCommand(p => ApplyFlow.RunAsync([((OptimizationItem)p!).Id], Reporter), p => p is OptimizationItem);
        _host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppHost.Scan))
                Load();
        };
        Load();
    }

    public override string Title => "Otimizações";

    public override string Icon => "\uE90F";

    public override bool AdvancedOnly => true;

    public ICommand ApplyCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand UnlockCommand { get; }
    public ObservableCollection<OptimizationGroup> Groups { get; } = [];
    public bool HasScan => _host.Scan is not null;

    /// <summary>Uma linha no topo: o que dá para fazer agora e o que já está certo.</summary>
    public string Summary
    {
        get
        {
            int Count(string title) => Groups.FirstOrDefault(g => g.Title == title)?.Items.Count ?? 0;
            var fazer = Count("Recomendadas") + Count("Opcionais");
            var certas = Count("Já otimizado");
            return fazer == 0
                ? $"Nada para fazer agora: {certas} {(certas == 1 ? "item já está certo" : "itens já estão certos")}."
                : $"{fazer} {(fazer == 1 ? "item para escolher" : "itens para escolher")} e {certas} já {(certas == 1 ? "certo" : "certos")}.";
        }
    }

    private void Load()
    {
        Groups.Clear();
        if (_host.Scan is { } scan)
        {
            var items = scan.Optimizations
                .Where(o => o.Definition.Classification != Classification.NotRecommended)
                .Select(o => new OptimizationItem(o)).ToList();

            void Add(string title, string hint, Func<OptimizationItem, bool> filter, bool collapsible = false)
            {
                var list = items.Where(filter).ToList();
                if (list.Count > 0)
                    Groups.Add(new OptimizationGroup(title, hint, list, collapsible));
            }

            Add("Recomendadas", "Há benefício esperado neste PC. As marcadas vêm do seu perfil.", i => i.Result.Decision == Decision.Recommended);
            // Reparo não é otimização: limpar cache ou resetar a rede não sobe
            // FPS, e no teste real foram clicados duas vezes "por garantia".
            // Ficam separados, no fim, dizendo para que servem.
            static bool IsRepair(OptimizationItem i) => i.Result.Definition.Category is "troubleshooting" or "network";
            Add("Opcionais", "Disponíveis por escolha sua: itens de inicialização e experimentais.", i => i.Result.Decision == Decision.Optional && !IsRepair(i));
            Add("Disponíveis em outro plano", "Resolveriam algo encontrado no seu PC. Cada uma diz o quê: para aplicar, é só assinar o plano indicado.", i => i.Result.Decision == Decision.Blocked);
            Add("Já otimizado", "Já estão na configuração certa. O RKZFPS não mexe no que já está bom.", i => i.Result.Decision == Decision.AlreadyOptimal, collapsible: true);
            Add("Reparos (não aumentam FPS)", "Só para quando o problema descrito em cada um acontecer: travadas estranhas depois de atualizar o jogo ou o driver, sites que não abrem, internet que caiu depois de remover VPN. Sem o problema, não fazem diferença.", i => i.Result.Decision == Decision.Optional && IsRepair(i), collapsible: true);
            Add("Não se aplicam a este PC", "Hardware, sistema ou jogo não atendem aos critérios.", i => i.Result.Decision is Decision.NotApplicable or Decision.Unknown, collapsible: true);
        }

        Raise(nameof(HasScan));
        Raise(nameof(Summary));
    }

    private List<string> Selected() =>
        Groups.SelectMany(g => g.Items).SelectMany(i => i.Proposals).Where(p => p.IsSelected && p.Actionable).Select(p => p.Id).ToList();

    private Task Apply() => ApplyFlow.RunAsync(Selected(), Reporter);
}
