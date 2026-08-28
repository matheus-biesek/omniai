import { useState, type FormEvent } from "react";
import { AppShell } from "../../app/AppShell";
import { Card } from "../../shared/ui/Card";
import { Button } from "../../shared/ui/Button";
import { Input } from "../../shared/ui/Input";
import { useProjects } from "../../features/projects/useProjects";
import { useCreateProject } from "../../features/projects/useCreateProject";
import { useCreateApiKey } from "../../features/projects/useCreateApiKey";
import { useRevokeApiKey } from "../../features/projects/useRevokeApiKey";

function formatDate(value: string): string {
  return new Date(value).toLocaleString("pt-BR");
}

export function ProjectsPage() {
  const { projects, loading, error, refetch } = useProjects();
  const createProject = useCreateProject();
  const createApiKey = useCreateApiKey();
  const revokeApiKey = useRevokeApiKey();

  const [newProjectName, setNewProjectName] = useState("");
  const [revealedKey, setRevealedKey] = useState<{ label: string; apiKey: string } | null>(null);

  async function handleCreateProject(event: FormEvent) {
    event.preventDefault();
    const result = await createProject.submit(newProjectName);
    if (result) {
      setRevealedKey({ label: result.projectName, apiKey: result.apiKey });
      setNewProjectName("");
      await refetch();
    }
  }

  async function handleCreateApiKey(projectId: string, projectName: string) {
    const apiKey = await createApiKey.submit(projectId);
    if (apiKey) {
      setRevealedKey({ label: projectName, apiKey });
      await refetch();
    }
  }

  async function handleRevoke(apiKeyId: string) {
    const ok = await revokeApiKey.submit(apiKeyId);
    if (ok) {
      await refetch();
    }
  }

  return (
    <AppShell>
      <Card title="Novo projeto">
        <form onSubmit={handleCreateProject} className="form form-inline">
          <Input
            label="Nome do projeto"
            id="new-project-name"
            value={newProjectName}
            onChange={(e) => setNewProjectName(e.target.value)}
            required
          />
          <Button type="submit" disabled={createProject.loading}>
            {createProject.loading ? "Criando..." : "Criar projeto"}
          </Button>
        </form>
        {createProject.error ? <p className="form-error">{createProject.error}</p> : null}
      </Card>

      {revealedKey ? (
        <Card title={`Chave de API — ${revealedKey.label}`}>
          <p className="api-key-reveal">{revealedKey.apiKey}</p>
          <p className="hint">
            Copie esta chave agora. Ela não será exibida novamente.
          </p>
          <Button variant="secondary" onClick={() => setRevealedKey(null)}>
            Fechar
          </Button>
        </Card>
      ) : null}

      {loading ? <p>Carregando...</p> : null}
      {error ? <p className="form-error">{error}</p> : null}

      {projects.map((project) => (
        <Card key={project.id} title={project.name}>
          <table className="data-table">
            <thead>
              <tr>
                <th>Chave</th>
                <th>Criada em</th>
                <th>Status</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {project.apiKeys.map((key) => (
                <tr key={key.id}>
                  <td>{key.id}</td>
                  <td>{formatDate(key.createdAt)}</td>
                  <td>{key.revokedAt ? `Revogada em ${formatDate(key.revokedAt)}` : "Ativa"}</td>
                  <td>
                    {!key.revokedAt ? (
                      <Button
                        variant="danger"
                        onClick={() => handleRevoke(key.id)}
                        disabled={revokeApiKey.loading}
                      >
                        Revogar
                      </Button>
                    ) : null}
                  </td>
                </tr>
              ))}
              {project.apiKeys.length === 0 ? (
                <tr>
                  <td colSpan={4}>Nenhuma chave ainda.</td>
                </tr>
              ) : null}
            </tbody>
          </table>
          <Button
            variant="secondary"
            onClick={() => handleCreateApiKey(project.id, project.name)}
            disabled={createApiKey.loading}
          >
            Nova chave
          </Button>
        </Card>
      ))}
    </AppShell>
  );
}
