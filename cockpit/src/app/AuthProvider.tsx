import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import { clearToken, readToken, writeToken } from "../features/auth/authStorage";

interface AuthContextValue {
  token: string | null;
  login: (token: string) => void;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(() => readToken());

  const value = useMemo<AuthContextValue>(
    () => ({
      token,
      login: (newToken: string) => {
        writeToken(newToken);
        setToken(newToken);
      },
      logout: () => {
        clearToken();
        setToken(null);
      },
    }),
    [token],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth precisa ser usado dentro de um AuthProvider.");
  }

  return context;
}
