using System.Windows;

namespace Fpsx.App;

/// <summary>
/// Tema claro/escuro, no mesmo modelo do site (frontend/src/lib/theme.ts):
/// o escuro é o padrão e o claro só vale quando a pessoa escolhe. Não segue o
/// Windows: quem já usa o RKZFPS conhece ele escuro, e o app virar do avesso
/// sozinho seria pior que não ter tema claro.
///
/// A troca é na hora: as telas leem as cores por DynamicResource, então basta
/// trocar o dicionário de cores (posição 0 do App.xaml).
/// </summary>
public static class ThemeManager
{
    public const string Dark = "dark";
    public const string Light = "light";

    /// <summary>Avisa quem desenha na mão (gráfico, cores calculadas em código) para repintar.</summary>
    public static event Action? Changed;

    public static string Current { get; private set; } = Dark;

    public static bool IsLight => Current == Light;

    public static string Normalize(string? value) => value == Light ? Light : Dark;

    public static void Apply(string? theme)
    {
        var target = Normalize(theme);
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var colors = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{(target == Light ? "Light" : "Dark")}.xaml", UriKind.Absolute) };
        if (dictionaries.Count > 0)
            dictionaries[0] = colors;
        else
            dictionaries.Insert(0, colors);
        var changed = target != Current;
        Current = target;
        if (changed)
            Changed?.Invoke();
    }

    /// <summary>O botão da barra de título: alterna e guarda a escolha.</summary>
    public static void Toggle() => Set(IsLight ? Dark : Light);

    public static void Set(string theme)
    {
        var ctx = AppHost.Current.Ctx;
        var target = Normalize(theme);
        if (ctx.Settings.Theme != target)
            ctx.Storage.SaveSettings(ctx.Settings with { Theme = target });
        Apply(target);
    }
}
