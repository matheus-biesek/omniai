const STORAGE_KEY = "omniai_token";

export function readToken(): string | null {
  try {
    return sessionStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

export function writeToken(token: string): void {
  try {
    sessionStorage.setItem(STORAGE_KEY, token);
  } catch {
    // sessionStorage indisponível (ex: navegador privado bloqueando) - sessão simplesmente
    // não sobrevive a um F5 nesse caso, sem quebrar o app.
  }
}

export function clearToken(): void {
  try {
    sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    // ver nota em writeToken
  }
}
