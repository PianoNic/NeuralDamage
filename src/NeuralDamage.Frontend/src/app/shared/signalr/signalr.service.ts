import { Injectable, inject, signal } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private readonly oidc = inject(OidcSecurityService);
  private chatHub: signalR.HubConnection | null = null;
  private userHub: signalR.HubConnection | null = null;

  readonly connected = signal(false);

  async start() {
    const token = await this.getToken();
    if (!token) return;

    this.chatHub = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}/hubs/chat`, {
        accessTokenFactory: () => this.getToken(),
      })
      .withAutomaticReconnect()
      .build();

    this.userHub = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}/hubs/user`, {
        accessTokenFactory: () => this.getToken(),
      })
      .withAutomaticReconnect()
      .build();

    await Promise.all([this.chatHub.start(), this.userHub.start()]);
    this.connected.set(true);
  }

  async stop() {
    await Promise.all([this.chatHub?.stop(), this.userHub?.stop()]);
    this.chatHub = null;
    this.userHub = null;
    this.connected.set(false);
  }

  async joinChat(chatId: string) {
    await this.chatHub?.invoke('JoinChat', chatId);
  }

  async leaveChat(chatId: string) {
    await this.chatHub?.invoke('LeaveChat', chatId);
  }

  // Chat hub event listeners
  onChatEvent(event: string, callback: (...args: any[]) => void) {
    this.chatHub?.on(event, callback);
  }

  offChatEvent(event: string, callback: (...args: any[]) => void) {
    this.chatHub?.off(event, callback);
  }

  // User hub event listeners
  onUserEvent(event: string, callback: (...args: any[]) => void) {
    this.userHub?.on(event, callback);
  }

  offUserEvent(event: string, callback: (...args: any[]) => void) {
    this.userHub?.off(event, callback);
  }

  private async getToken(): Promise<string> {
    const result = await firstValueFrom(this.oidc.getAccessToken());
    return result;
  }
}
