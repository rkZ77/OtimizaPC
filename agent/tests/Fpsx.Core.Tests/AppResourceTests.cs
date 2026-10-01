using System.Text.RegularExpressions;

namespace Fpsx.Core.Tests;

/// <summary>
/// Recurso de tema usado numa tela e não definido só quebra em tempo de
/// execução, quando a pessoa abre aquela tela (foi assim com o Surface3 do
/// seletor). Este teste lê os XAML e o C# do app e confere antes do build sair.
/// </summary>
public class AppResourceTests
{
    // Parte da pasta deste arquivo-fonte, não da saída do build: o build pode
    // sair fora do repositório (--artifacts-path).
    private static string AppDir([System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(source) ?? AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Fpsx.App")))
            dir = dir.Parent;
        return dir is null ? throw new DirectoryNotFoundException("src/Fpsx.App não encontrado") : Path.Combine(dir.FullName, "src", "Fpsx.App");
    }

    [Fact]
    public void Todo_recurso_usado_nas_telas_existe_no_tema()
    {
        var app = AppDir();
        var xaml = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories).Where(f => !f.Contains(Path.Combine("obj", ""))).ToList();
        var cs = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.Combine("obj", ""))).ToList();

        var defined = xaml.SelectMany(f => Regex.Matches(File.ReadAllText(f), "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value)).ToHashSet();
        var used = xaml.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:Static|Dynamic)Resource (\w+)\}").Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Concat(cs.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:Resources\[""|FindResource\("")(\w+)""").Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value))));

        var missing = used.Where(u => !defined.Contains(u.Key)).Select(u => $"{u.File}: {u.Key}").Distinct().ToList();
        Assert.True(missing.Count == 0, "Recursos sem definição: " + string.Join(", ", missing));
    }

    [Fact]
    public void Tema_claro_e_escuro_tem_as_mesmas_cores()
    {
        // Cor que só existe num tema não quebra o build: some da tela quando a
        // pessoa troca. As duas paletas precisam ter exatamente as mesmas chaves.
        static HashSet<string> Keys(string file) =>
            Regex.Matches(File.ReadAllText(file), "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        var themes = Path.Combine(AppDir(), "Themes");
        var dark = Keys(Path.Combine(themes, "Dark.xaml"));
        var light = Keys(Path.Combine(themes, "Light.xaml"));
        Assert.Empty(dark.Except(light));
        Assert.Empty(light.Except(dark));
        Assert.Contains("OnFill", dark);
        Assert.Contains("AccentFill", dark);
    }

    [Fact]
    public void Nenhuma_cor_solta_nas_telas()
    {
        // Regra do projeto: cor muda no token, nunca solta no componente. Cor
        // solta não troca com o tema e fica ilegível no claro ou no escuro.
        var app = AppDir();
        var soltas = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.Combine("obj", "")) && !f.Contains(Path.Combine("Themes", "")))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "=\"#[0-9A-Fa-f]{6,8}\"").Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();
        Assert.True(soltas.Count == 0, "Cor fora dos temas: " + string.Join(", ", soltas));
    }
}
