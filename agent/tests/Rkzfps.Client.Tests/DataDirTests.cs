using Rkzfps.Client;

namespace Rkzfps.Client.Tests;

/// <summary>
/// A pasta de dados mudou de %LOCALAPPDATA%\FPSX para RKZFPS. Os backups do
/// desfazer moram nela: a migração não pode deixar o histórico para trás.
/// </summary>
public class DataDirTests
{
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rkzfps-datadir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void Pasta_antiga_vira_a_nova_com_as_sessoes_dentro()
    {
        var root = NewRoot();
        var session = Path.Combine(root, "FPSX", "sessions", "s1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(session)!);
        File.WriteAllText(session, "{}");

        var dir = AgentContext.DefaultDataDir(root);

        Assert.Equal(Path.Combine(root, "RKZFPS"), dir);
        Assert.True(File.Exists(Path.Combine(dir, "sessions", "s1.json")));
        Assert.False(Directory.Exists(Path.Combine(root, "FPSX")));

        // As sessões ficam legíveis pelo mesmo SessionStore depois da troca.
        Assert.Single(Directory.EnumerateFiles(Path.Combine(dir, "sessions")));
    }

    [Fact]
    public void Instalacao_nova_usa_a_pasta_nova()
    {
        var root = NewRoot();
        Assert.Equal(Path.Combine(root, "RKZFPS"), AgentContext.DefaultDataDir(root));
    }

    [Fact]
    public void Pasta_nova_existente_nao_e_sobrescrita_pela_antiga()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "RKZFPS"));
        Directory.CreateDirectory(Path.Combine(root, "FPSX"));

        Assert.Equal(Path.Combine(root, "RKZFPS"), AgentContext.DefaultDataDir(root));
        Assert.True(Directory.Exists(Path.Combine(root, "FPSX")));
    }

    [Fact]
    public void Pasta_antiga_presa_continua_em_uso_em_vez_de_comecar_sem_historico()
    {
        var root = NewRoot();
        var legacy = Path.Combine(root, "FPSX");
        Directory.CreateDirectory(legacy);
        // Arquivo aberto sem compartilhar exclusão: o Windows não deixa renomear a pasta.
        using var held = new FileStream(Path.Combine(legacy, "changes.jsonl"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(legacy, AgentContext.DefaultDataDir(root));
        Assert.False(Directory.Exists(Path.Combine(root, "RKZFPS")));
    }
}
