using Fpsx.Client;

namespace Fpsx.App;

/// <summary>
/// "O que há de novo" na primeira abertura depois de uma atualização. Só
/// para quem atualizou: instalação nova vê o tutorial e já fica marcada.
/// </summary>
public static class WhatsNew
{
    // Atualizar junto com a versão do Directory.Build.props e com a nota do admin.
    private const string Notes =
        "Vigia do PC: com o RKZFPS na bandeja, ele confere o PC depois que o Windows liga e a cada 6 horas. " +
        "Se uma atualização do Windows, um driver novo ou um jogo desfez uma correção, você recebe um aviso para corrigir de novo.\n\n" +
        "Resumo da semana: uma vez por semana, o FPS medido nas suas partidas, com a semana anterior ao lado.\n\n" +
        "Correção travada agora mostra o plano que libera, e a imagem da partida compartilhada leva o seu link de indicação.\n\n" +
        "Os avisos podem ser desligados em Configurações. Novidades e dicas no Instagram @rkzfps.br.";

    public static void ShowIfUpdated()
    {
        // Só é chamado com o tutorial já visto: sem versão salva, a pessoa veio
        // de uma versão anterior à 0.5.0, que não guardava o campo.
        if (AppHost.Current.Ctx.Settings.LastSeenVersion == AgentContext.Version)
            return;
        Dialogs.Info($"Novidades do RKZFPS {AgentContext.Version}", Notes);
    }

    public static void MarkSeen()
    {
        var ctx = AppHost.Current.Ctx;
        if (ctx.Settings.LastSeenVersion != AgentContext.Version)
            ctx.Storage.SaveSettings(ctx.Settings with { LastSeenVersion = AgentContext.Version });
    }
}
