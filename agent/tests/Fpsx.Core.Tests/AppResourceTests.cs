using System.Text.RegularExpressions;

namespace Fpsx.Core.Tests;

/// <summary>
/// Recurso de tema usado numa tela e não definido só quebra em tempo de
/// execução, quando a pessoa abre aquela tela (foi assim com o Surface3 do
/// seletor). Este teste lê os XAML e o C# do app e confere antes do build sair.
/// </summary>
public class AppResourceTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
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
        var used = xaml.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"StaticResource (\w+)\}").Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Concat(cs.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:Resources\[""|FindResource\("")(\w+)""").Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value))));

        var missing = used.Where(u => !defined.Contains(u.Key)).Select(u => $"{u.File}: {u.Key}").Distinct().ToList();
        Assert.True(missing.Count == 0, "Recursos sem definição: " + string.Join(", ", missing));
    }
}
