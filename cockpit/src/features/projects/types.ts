export interface ApiKeySummary {
  id: string;
  createdAt: string;
  revokedAt: string | null;
}

export interface ProjectSummary {
  id: string;
  name: string;
  apiKeys: ApiKeySummary[];
}
