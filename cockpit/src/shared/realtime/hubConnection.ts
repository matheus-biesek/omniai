import * as signalR from "@microsoft/signalr";

export function createHubConnection(token: string): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(import.meta.env.VITE_HUB_URL, {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect()
    .build();
}
