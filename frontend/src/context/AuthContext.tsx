import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import api, { type User } from '../services/api'

interface AuthContextType {
  user: User | null
  loading: boolean
  login: (email: string, password: string) => Promise<User>
  register: (name: string, email: string, password: string) => Promise<User>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextType | null>(null)

/**
 * Sessao vem do cookie httpOnly: o front nunca ve o token. Na abertura,
 * /auth/me diz quem esta logado; nada sensivel fica no localStorage.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let alive = true
    api.get<{ user: User }>('/auth/me')
      .then(({ data }) => { if (alive) setUser(data.user) })
      .catch(() => { if (alive) setUser(null) })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const { data } = await api.post<{ user: User }>('/auth/login', { email, password })
    setUser(data.user)
    return data.user
  }, [])

  const register = useCallback(async (name: string, email: string, password: string) => {
    const { data } = await api.post<{ user: User }>('/auth/register', { name, email, password })
    setUser(data.user)
    return data.user
  }, [])

  const logout = useCallback(async () => {
    try {
      await api.post('/auth/logout')
    } finally {
      setUser(null)
    }
  }, [])

  return <AuthContext.Provider value={{ user, loading, login, register, logout }}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextType {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth fora do AuthProvider')
  return ctx
}
