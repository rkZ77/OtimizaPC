using Rkzfps.Client;

namespace Rkzfps.App;

/// <summary>
/// "O que há de novo" na primeira abertura depois de uma atualização. Só
/// para quem atualizou: instalação nova vê o tutorial e já fica marcada.
/// </summary>
public static class WhatsNew
{
    // Atualizar junto com a versão do Directory.Build.props e com a nota do admin.
    private const string Notes =
        "Dicas para cada jogo, na hora: em Jogos, o RKZFPS mostra o que ajustar no menu de vídeo para o seu PC e o seu perfil, sem esperar a IA. " +
        "Vale para CS2, Valorant, League of Legends, Fortnite, Overwatch 2 e Free Fire no emulador. A IA continua em Mais dicas com IA.\n\n" +
        "Sua placa reconhecida pelo modelo: o RKZFPS sabe se ela tem Reflex ou DLSS e acerta melhor o nível do PC.\n\n" +
        "Jogos novos: Overwatch 2 e Free Fire pelo emulador (BlueStacks, MSI App Player, LDPlayer, MEmu e MuMu), com o FPS medido nas partidas. " +
        "No League of Legends, o RKZFPS também confere a configuração de vídeo.\n\n" +
        "Novidades e dicas no Instagram @rkzfps.br.";

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
