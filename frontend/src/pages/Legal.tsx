import type { ReactNode } from 'react'
import PageShell from '../components/PageShell'
import { Button } from '../components/ui'

function Prose({ children }: { children: ReactNode }) {
  return <div className="space-y-4 leading-relaxed text-ink-2 [&_h2]:mt-8 [&_h2]:font-display [&_h2]:text-lg [&_h2]:font-bold [&_h2]:text-ink-1 [&_li]:ml-5 [&_li]:list-disc">{children}</div>
}

export function Privacidade() {
  return (
    <PageShell title="Política de privacidade" width="prose" revelacao={false}
               bar={{ title: 'Política de privacidade', sub: 'Coletamos o mínimo necessário para o produto funcionar, e nada sem você saber.' }}>
      <Prose>
        <h2>O que fica só no seu PC</h2>
        <p>O resultado completo do diagnóstico, a lista de processos, os programas de inicialização, os backups e o histórico de alterações ficam na pasta de dados do RKZFPS no seu computador e não são enviados.</p>
        <h2>O que o servidor recebe</h2>
        <ul>
          <li>Conta: e-mail, nome e senha (guardada só como hash).</li>
          <li>PC ativado: um identificador calculado a partir do Windows (hash irreversível), o nome do computador, a versão do Windows e do app.</li>
          <li>Pagamentos: plano, valor, status e o identificador do Mercado Pago. Dados de cartão ficam só com o Mercado Pago.</li>
        </ul>
        <h2>Dados de uso, só com permissão</h2>
        <p>Se você permitir no app, enviamos:</p>
        <ul>
          <li>quais otimizações foram aplicadas e se funcionaram, com a data e a versão do app;</li>
          <li>o FPS das partidas medidas: números (FPS médio, 1% low, travadas), o gráfico da partida e quantas quedas vieram de cada tipo de causa (programa aberto, processador, placa de vídeo ou o próprio jogo);</li>
          <li>um resumo do hardware: modelo do processador e da placa de vídeo, quantidade de memória e versão do Windows.</li>
        </ul>
        <p>Com isso o RKZFPS compara o seu PC com PCs parecidos (a comparação só aparece quando o grupo tem pelo menos 5 PCs, para ninguém ser identificado) e descobre, no conjunto, o que de fato melhora o FPS. Nunca enviamos arquivos, nomes de programas, o nome do PC, caminhos de pasta ou conteúdo pessoal. Você pode desligar a qualquer momento em Configurações, e a fila local é apagada.</p>
        <h2>Recursos com inteligência artificial</h2>
        <p>Alguns recursos usam a OpenAI para escrever a resposta, e só rodam quando você pede:</p>
        <ul>
          <li>o assistente do site recebe as mensagens que você digita no chat;</li>
          <li>no app, "explicar o diagnóstico", "qual peça trocar" e "dicas por jogo" enviam o resumo do hardware, os itens do diagnóstico e, quando houver, os números medidos nas suas partidas.</li>
        </ul>
        <p>Não vão e-mail, nome, nome do PC, arquivos nem nomes de programas. Não digite dados pessoais no chat.</p>
        <h2>Cookies</h2>
        <p>O site usa o cookie essencial de sessão (para manter você conectado) e guarda no navegador a sua escolha de tema e a sua resposta sobre cookies.</p>
        <p>Se você tocar em "Aceitar todos", o site também usa o Google Analytics para contar visitas e saber quais páginas ajudam mais. Ele recebe as páginas visitadas, a origem da visita e dados do navegador e do aparelho, sem seu e-mail ou nome. Em "Só essenciais", nada vai para o Google. Para mudar a resposta, limpe os dados do site no navegador e escolha de novo.</p>
        <h2>Seus direitos</h2>
        <p>Você pode pedir acesso, correção ou exclusão dos seus dados pelo suporte. Ao excluir a conta, as licenças e os PCs vinculados são removidos.</p>
      </Prose>
    </PageShell>
  )
}

export function Termos() {
  return (
    <PageShell title="Termos de uso" width="prose" revelacao={false} bar={{ title: 'Termos de uso' }}>
      <Prose>
        <h2>O que o RKZFPS faz</h2>
        <p>O RKZFPS analisa o seu PC e aplica, com a sua confirmação, apenas alterações de uma lista fechada, sempre com backup do estado anterior e opção de desfazer. Limpezas de cache e reparos de rede não têm estado anterior e são indicados como tal antes de aplicar.</p>
        <h2>Sem promessa de desempenho</h2>
        <p>Resultados dependem do hardware, do jogo e da configuração de cada PC. O RKZFPS não promete aumento de FPS ou redução de latência: ele mede antes e depois e informa o resultado real, inclusive quando não há ganho.</p>
        <h2>Licença</h2>
        <p>Cada assinatura vale para 1 PC ativo por vez. Você pode trocar de PC desativando o anterior em Minha conta. A licença é pessoal: não pode ser dividida, emprestada nem revendida.</p>
        <h2>Teste grátis</h2>
        <p>O teste grátis do plano Pro vale uma vez por PC. Se ele terminar sem assinatura, o app volta ao plano Free e desfaz sozinho as correções aplicadas durante o teste, restaurando o estado anterior salvo no backup. Alterações que você ou outro programa mudaram depois ficam como estão. Correções de sistema pedem a permissão do Windows para serem desfeitas.</p>
        <h2>Cancelamento</h2>
        <p>Planos pagos não renovam sozinhos. Ao vencer, o app volta ao plano Free, as correções feitas no plano pago continuam no PC e todas podem ser desfeitas quando você quiser.</p>
      </Prose>
    </PageShell>
  )
}

export function NotFound() {
  return (
    <PageShell title="Página não encontrada" noindex width="narrow" fundo revelacao={false} mainClassName="text-center py-20">
      <p className="font-display text-6xl font-extrabold text-ink-1">404</p>
      <p className="mt-3 text-ink-3">Esta página não existe.</p>
      <div className="mt-6 flex justify-center"><Button to="/">Voltar ao início</Button></div>
    </PageShell>
  )
}
