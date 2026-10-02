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
        "Perfis novos: Automático, Desempenho, Equilibrado, Qualidade e Personalizado. O RKZFPS junta o que você escolheu com o seu hardware: " +
        "em PC forte, pedir mais FPS não piora a imagem do jogo; em PC de entrada, traz a configuração leve, sempre com sua confirmação. " +
        "Seu perfil de antes continua valendo. Para trocar, ou responder as perguntas rápidas, vá em Configurações.\n\n" +
        "Telas mais limpas: cada cartão mostra o essencial. A explicação completa está no ícone de informação ao lado do título.\n\n" +
        "Dicas da IA por jogo levam em conta o seu perfil.\n\n" +
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
