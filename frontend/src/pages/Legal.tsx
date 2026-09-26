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
        <p>O resultado completo do diagnóstico, a lista de processos, os programas de inicialização, os backups e o histórico de alterações ficam na pasta de dados do FPSX no seu computador e não são enviados.</p>
        <h2>O que o servidor recebe</h2>
        <ul>
          <li>Conta: e-mail, nome e senha (guardada só como hash).</li>
          <li>PC ativado: um identificador calculado a partir do Windows (hash irreversível), o nome do computador, a versão do Windows e do app.</li>
          <li>Pagamentos: plano, valor, status e o identificador do Mercado Pago. Dados de cartão ficam só com o Mercado Pago.</li>
        </ul>
        <h2>Dados de uso, só com permissão</h2>
        <p>Se você permitir no app, enviamos: quais otimizações foram aplicadas, se funcionaram, resultados de benchmark, versão do app e do Windows. Nunca enviamos arquivos, nomes de programas, caminhos de pasta ou conteúdo pessoal. Você pode desligar a qualquer momento em Configurações, e a fila local é apagada.</p>
        <h2>Cookies</h2>
        <p>O site usa só o cookie essencial de sessão (para manter você conectado) e guarda no navegador a sua escolha de tema.</p>
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
        <h2>O que o FPSX faz</h2>
        <p>O FPSX analisa o seu PC e aplica, com a sua confirmação, apenas alterações de uma lista fechada, sempre com backup do estado anterior e opção de desfazer. Limpezas de cache e reparos de rede não têm estado anterior e são indicados como tal antes de aplicar.</p>
        <h2>Sem promessa de desempenho</h2>
        <p>Resultados dependem do hardware, do jogo e da configuração de cada PC. O FPSX não promete aumento de FPS ou redução de latência: ele mede antes e depois e informa o resultado real, inclusive quando não há ganho.</p>
        <h2>Licença</h2>
        <p>Cada plano permite um número de PCs ativos ao mesmo tempo. Você pode trocar de PC desativando um na sua conta. A licença é pessoal e não pode ser revendida.</p>
        <h2>Cancelamento</h2>
        <p>Planos não renovam sozinhos. Ao vencer, o app volta ao plano Free e todas as alterações já feitas continuam podendo ser desfeitas.</p>
      </Prose>
    </PageShell>
  )
}

export function NotFound() {
  return (
    <PageShell title="Página não encontrada" noindex width="narrow" revelacao={false} mainClassName="text-center py-20">
      <p className="font-display text-6xl font-extrabold text-ink-1">404</p>
      <p className="mt-3 text-ink-3">Esta página não existe.</p>
      <div className="mt-6 flex justify-center"><Button to="/">Voltar ao início</Button></div>
    </PageShell>
  )
}
