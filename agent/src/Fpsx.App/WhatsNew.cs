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
        "O que limitou a partida: com o jogo aberto, o RKZFPS lê uso por núcleo, clocks, temperatura da placa de vídeo, memória de vídeo, RAM e disco, " +
        "e diz se o FPS foi segurado pelo processador, placa de vídeo, memória, temperatura, disco ou configuração. Cada conclusão mostra os números que a sustentam, " +
        "e o que não deu para ler aparece como não disponível, nunca inventado.\n\n" +
        "Placa de vídeo do jogo: em PC com vídeo integrado e placa dedicada, o RKZFPS avisa quando o jogo rodou na placa errada e mostra como corrigir.\n\n" +
        "Modo Gaming: no Automático, o RKZFPS aplica ao abrir o jogo só o que você autorizou e desfaz quando ele fecha. No Manual, nada muda sozinho. Escolha na tela FPS Boost.\n\n" +
        "Histórico por jogo: filtre as partidas por jogo e veja médias, resolução e configuração usada.\n\n" +
        "Visual: chaves de ligar e desligar novas e avisos sem a barra branca do Windows.\n\n" +
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
