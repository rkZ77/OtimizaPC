/*
 * Codigo de quem indicou. O link /r/CODIGO leva para a home (a pessoa precisa
 * conhecer o produto antes de criar conta), entao o codigo fica guardado no
 * navegador ate' o cadastro. Sem storage (aba anonima), so' o ?ref= da URL vale.
 */
const CHAVE = 'rkzfps_indicacao'
const VALIDADE_MS = 30 * 86_400_000

export function guardarIndicacao(code: string) {
  const limpo = code.trim().toUpperCase().slice(0, 20)
  if (!/^[A-Z0-9]{4,20}$/.test(limpo)) return
  try {
    localStorage.setItem(CHAVE, JSON.stringify({ code: limpo, em: Date.now() }))
  } catch {
    /* sem storage: segue sem indicacao */
  }
}

export function lerIndicacao(): string | null {
  try {
    const salvo = JSON.parse(localStorage.getItem(CHAVE) ?? 'null') as { code: string; em: number } | null
    return salvo && Date.now() - salvo.em < VALIDADE_MS ? salvo.code : null
  } catch {
    return null
  }
}

export function limparIndicacao() {
  try {
    localStorage.removeItem(CHAVE)
  } catch {
    /* nada a limpar */
  }
}
