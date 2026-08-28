import { useState, type FormEvent } from "react";
import { Navigate } from "react-router-dom";
import { useAuth } from "../../app/AuthProvider";
import { useLogin } from "../../features/auth/useLogin";
import { Button } from "../../shared/ui/Button";
import { Input } from "../../shared/ui/Input";
import { Card } from "../../shared/ui/Card";

export function LoginPage() {
  const { token } = useAuth();
  const { submit, loading, error } = useLogin();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");

  if (token) {
    return <Navigate to="/dashboard" replace />;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    await submit(username, password);
  }

  return (
    <div className="centered-page">
      <Card title="OmniAI">
        <form onSubmit={handleSubmit} className="form">
          <Input
            label="Usuário"
            id="username"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            autoComplete="username"
            required
          />
          <Input
            label="Senha"
            id="password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            required
          />
          {error ? <p className="form-error">{error}</p> : null}
          <Button type="submit" disabled={loading}>
            {loading ? "Entrando..." : "Entrar"}
          </Button>
        </form>
      </Card>
    </div>
  );
}
