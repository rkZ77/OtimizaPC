using System.Collections.ObjectModel;
using System.Windows.Input;
using Fpsx.Core.Model;

namespace Fpsx.App.ViewModels;

public sealed record OptimizationGroup(string Title, string Hint, IReadOnlyList<OptimizationItem> Items);

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

    public ICommand ApplyCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand UnlockCommand { get; }
    public ObservableCollection<OptimizationGroup> Groups { get; } = [];
    public bool HasScan => _host.Scan is not null;

    private void Load()
    {
        Groups.Clear();
        if (_host.Scan is { } scan)
        {
            var items = scan.Optimizations
                .Where(o => o.Definition.Classification != Classification.NotRecommended)
                .Select(o => new OptimizationItem(o)).ToList();

            void Add(string title, string hint, Func<OptimizationItem, bool> filter)
            {
                var list = items.Where(filter).ToList();
                if (list.Count > 0)
                    Groups.Add(new OptimizationGroup(title, hint, list));
            }

            Add("Recomendadas", "Há benefício esperado neste PC. As marcadas vêm do seu perfil.", i => i.Result.Decision == Decision.Recommended);
            Add("Opcionais", "Disponíveis por escolha sua: troubleshooting, itens de inicialização e experimentais.", i => i.Result.Decision == Decision.Optional);
            Add("Bloqueadas", "Aplicáveis, mas exigem outro plano ou abrir o FPSX como administrador.", i => i.Result.Decision == Decision.Blocked);
            Add("Já otimizado", "Já estão na configuração certa. O FPSX não mexe no que já está bom.", i => i.Result.Decision == Decision.AlreadyOptimal);
            Add("Não se aplicam a este PC", "Hardware, sistema ou jogo não atendem aos critérios.", i => i.Result.Decision is Decision.NotApplicable or Decision.Unknown);
        }

        Raise(nameof(HasScan));
    }

    private List<string> Selected() =>
        Groups.SelectMany(g => g.Items).SelectMany(i => i.Proposals).Where(p => p.IsSelected && p.Actionable).Select(p => p.Id).ToList();

    private Task Apply() => ApplyFlow.RunAsync(Selected(), Reporter);
}
