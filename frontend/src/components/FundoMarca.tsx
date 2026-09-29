/*
 * Fundo da marca: a grade ciano com o brilho no canto, o mesmo das artes do
 * Instagram. So' decoracao, atras de tudo e sem receber clique.
 *
 * Cores so' por token: a grade usa --grid-alpha (mais forte no tema claro,
 * onde 5% sumiria no branco) e o brilho usa --accent. Quem liga e' o
 * PageShell (prop `fundo`), que da' o `isolate` para o -z-10 ficar acima do
 * fundo da pagina e abaixo do conteudo.
 */
export default function FundoMarca() {
  return (
    <div aria-hidden className="pointer-events-none absolute inset-0 -z-10 overflow-hidden">
      <div className="absolute inset-0 bg-data-grid [background-size:56px_56px] [mask-image:radial-gradient(ellipse_at_75%_0%,black,transparent_70%)]" />
      <div className="absolute -right-72 -top-80 h-[46rem] w-[46rem] rounded-full bg-[radial-gradient(circle,rgb(var(--accent)/0.18),transparent_65%)]" />
      <div className="absolute -left-80 top-[60%] h-[38rem] w-[38rem] rounded-full bg-[radial-gradient(circle,rgb(var(--accent)/0.07),transparent_65%)]" />
    </div>
  )
}
