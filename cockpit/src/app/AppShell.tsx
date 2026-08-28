import type { ReactNode } from "react";
import { NavLink } from "react-router-dom";
import { useAuth } from "./AuthProvider";
import { Button } from "../shared/ui/Button";

export function AppShell({ children }: { children: ReactNode }) {
  const { logout } = useAuth();

  return (
    <div className="app-shell">
      <header className="app-header">
        <span className="app-title">OmniAI</span>
        <nav className="app-nav">
          <NavLink to="/dashboard" className={({ isActive }) => (isActive ? "active" : "")}>
            Dashboard
          </NavLink>
          <NavLink to="/projects" className={({ isActive }) => (isActive ? "active" : "")}>
            Projetos
          </NavLink>
        </nav>
        <Button variant="secondary" onClick={logout}>
          Sair
        </Button>
      </header>
      <main className="app-main">{children}</main>
    </div>
  );
}
