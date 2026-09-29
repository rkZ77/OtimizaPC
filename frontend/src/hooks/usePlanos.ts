import { useEffect, useState } from 'react'
import api, { errorMessage, type PlansResponse } from '../services/api'

/*
 * Planos pedidos uma vez por visita e divididos entre a home, a grade e o
 * pagamento. A home mostra o teste gratis no topo e a grade logo abaixo: sem
 * isto seriam duas chamadas iguais na mesma tela.
 */
let pedido: Promise<PlansResponse> | null = null

export function carregarPlanos(): Promise<PlansResponse> {
  pedido ??= api.get<PlansResponse>('/public/plans').then(({ data }) => data).catch((e) => {
    pedido = null
    throw e
  })
  return pedido
}

export function usePlanos() {
  const [data, setData] = useState<PlansResponse | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    let vivo = true
    carregarPlanos()
      .then((d) => vivo && setData(d))
      .catch((e) => vivo && setError(errorMessage(e, 'Não foi possível carregar os planos agora.')))
    return () => { vivo = false }
  }, [])
  return { data, error }
}
