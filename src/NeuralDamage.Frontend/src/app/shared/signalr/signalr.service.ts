import { Injectable, inject, signal } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { firstValueFrom } from 'rxjs';
import { ChatHubEvents, UserHubEvents } from './hub-events';

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private readonly oidc = inject(OidcSecurityService);
  private chatHub: signalR.HubConnection | null = null;
  private userHub: signalR.HubConnection | null = null;

  readonly connected = signal(false);
  /** Guards against concurrent callers building duplicate connections. */
  private starting: Promise<void> | null = null;

  async start(): Promise<void> {
    if (this.connected()) return;
    this.starting ??= this.connect().finally(() => (this.starting = null));
    return this.starting;
  }

  private async connect() {
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
    await this.start();
    await this.chatHub?.invoke('JoinChat', chatId);
  }

  async leaveChat(chatId: string) {
    await this.chatHub?.invoke('LeaveChat', chatId);
  }

  // Chat hub event listeners. The event name picks the argument types out of
  // ChatHubEvents, so a handler that reads a payload the server does not send
  // fails to compile. signalR.on is untyped, hence the cast at the boundary.
  onChatEvent<E extends keyof ChatHubEvents>(event: E, callback: (...args: ChatHubEvents[E]) => void) {
    this.chatHub?.on(event, callback as (...args: unknown[]) => void);
  }

  offChatEvent<E extends keyof ChatHubEvents>(event: E, callback: (...args: ChatHubEvents[E]) => void) {
    this.chatHub?.off(event, callback as (...args: unknown[]) => void);
  }

  // User hub event listeners
  onUserEvent<E extends keyof UserHubEvents>(event: E, callback: (...args: UserHubEvents[E]) => void) {
    this.userHub?.on(event, callback as (...args: unknown[]) => void);
  }

  offUserEvent<E extends keyof UserHubEvents>(event: E, callback: (...args: UserHubEvents[E]) => void) {
    this.userHub?.off(event, callback as (...args: unknown[]) => void);
  }

  private async getToken(): Promise<string> {
    const result = await firstValueFrom(this.oidc.getAccessToken());
    return result;
  }
}
