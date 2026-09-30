import { computed, inject, Service, signal, WritableSignal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ChatHubEvents, UserHubEvents } from './hub-events';

/** Reconnect delays in ms: quick at first, then every 30 s for as long as it takes. */
const RETRY_DELAYS = [0, 2_000, 5_000, 10_000, 20_000];
const RETRY_CEILING = 30_000;

/** Never gives up; the library's default stops after four tries and leaves the page silently dead. */
const retryForever: signalR.IRetryPolicy = {
  nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
    RETRY_DELAYS[previousRetryCount] ?? RETRY_CEILING,
};

export type ConnectionState = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

/**
 * The two hubs: `/hubs/chat` for everything inside a chat, `/hubs/user` for the chat list. Handlers
 * registered before the connections exist are kept and attached once they do.
 */
@Service()
export class SignalRService {
  private readonly oidc = inject(OidcSecurityService);
  private chatHub: signalR.HubConnection | null = null;
  private userHub: signalR.HubConnection | null = null;

  private readonly chatState = signal<ConnectionState>('idle');
  private readonly userState = signal<ConnectionState>('idle');

  /** The worse of the two hubs, for the connection banner. */
  readonly state = computed<ConnectionState>(() => {
    const states = [this.chatState(), this.userState()];
    for (const state of ['disconnected', 'reconnecting', 'connecting', 'idle'] as const)
      if (states.includes(state)) return state;
    return 'connected';
  });
  readonly connected = computed(() => this.state() === 'connected');

  /** Joined chats, rejoined after a reconnect since the server forgets a dropped connection's groups. */
  private readonly joined = new Set<string>();

  /** Guards against concurrent callers building duplicate connections. */
  private starting: Promise<void> | null = null;

  async start(): Promise<void> {
    if (this.chatHub && this.userHub) return;
    this.starting ??= this.connect().finally(() => (this.starting = null));
    return this.starting;
  }

  private async connect(): Promise<void> {
    const token = await this.getToken();
    if (!token) return;

    this.chatHub = this.build('/hubs/chat', this.chatState);
    this.userHub = this.build('/hubs/user', this.userState);
    this.chatHub.onreconnected(() => {
      for (const chatId of this.joined) void this.chatHub?.invoke('JoinChat', chatId).catch(() => undefined);
    });

    await Promise.all([
      this.startHub(this.chatHub, this.chatState),
      this.startHub(this.userHub, this.userState),
    ]);
  }

  private build(path: string, state: WritableSignal<ConnectionState>) {
    const hub = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}${path}`, { accessTokenFactory: () => this.getToken() })
      .withAutomaticReconnect(retryForever)
      // Information logs each connect URL, access_token query string included.
      .configureLogging(signalR.LogLevel.Warning)
      .build();
    hub.onreconnecting(() => state.set('reconnecting'));
    hub.onreconnected(() => state.set('connected'));
    // Automatic reconnect only covers a connection that was up; a closed one is restarted here.
    hub.onclose(() => {
      if (hub !== this.chatHub && hub !== this.userHub) return;
      state.set('disconnected');
      setTimeout(() => void this.startHub(hub, state), RETRY_CEILING);
    });
    for (const [event, handlers] of this.handlers) {
      const isChat = path === '/hubs/chat';
      if (isChat === this.chatEvents.has(event)) handlers.forEach((h) => hub.on(event, h));
    }
    return hub;
  }

  private async startHub(
    hub: signalR.HubConnection,
    state: WritableSignal<ConnectionState>,
    attempt = 0,
  ): Promise<void> {
    if (hub !== this.chatHub && hub !== this.userHub) return;
    state.set(attempt === 0 ? 'connecting' : 'reconnecting');
    try {
      await hub.start();
      state.set('connected');
      if (hub === this.chatHub)
        for (const chatId of this.joined) await hub.invoke('JoinChat', chatId).catch(() => undefined);
    } catch {
      state.set('disconnected');
      const delay = RETRY_DELAYS[attempt + 1] ?? RETRY_CEILING;
      setTimeout(() => void this.startHub(hub, state, attempt + 1), delay);
    }
  }

  async stop(): Promise<void> {
    const hubs = [this.chatHub, this.userHub];
    this.chatHub = null;
    this.userHub = null;
    this.joined.clear();
    await Promise.all(hubs.map((hub) => hub?.stop()));
    this.chatState.set('idle');
    this.userState.set('idle');
  }

  async joinChat(chatId: string): Promise<void> {
    await this.start();
    this.joined.add(chatId);
    if (this.chatHub?.state === signalR.HubConnectionState.Connected)
      await this.chatHub.invoke('JoinChat', chatId);
  }

  async leaveChat(chatId: string): Promise<void> {
    this.joined.delete(chatId);
    if (this.chatHub?.state === signalR.HubConnectionState.Connected)
      await this.chatHub.invoke('LeaveChat', chatId);
  }

  /** Fire-and-forget; callers throttle, and a dropped ping only hides the indicator early. */
  startTyping(chatId: string): void {
    if (this.chatHub?.state !== signalR.HubConnectionState.Connected) return;
    void this.chatHub.send('StartTyping', chatId).catch(() => undefined);
  }

  // Handlers are kept here as well as on the connection, so a subscription made before start()
  // resolves still takes effect. The event name picks the argument types out of ChatHubEvents, so a
  // handler that reads a payload the server does not send fails to compile.
  private readonly handlers = new Map<string, Set<(...args: unknown[]) => void>>();
  private readonly chatEvents = new Set<string>();

  onChatEvent<E extends keyof ChatHubEvents>(event: E, callback: (...args: ChatHubEvents[E]) => void) {
    this.chatEvents.add(event);
    this.add(this.chatHub, event, callback as (...args: unknown[]) => void);
  }

  offChatEvent<E extends keyof ChatHubEvents>(event: E, callback: (...args: ChatHubEvents[E]) => void) {
    this.remove(this.chatHub, event, callback as (...args: unknown[]) => void);
  }

  onUserEvent<E extends keyof UserHubEvents>(event: E, callback: (...args: UserHubEvents[E]) => void) {
    this.add(this.userHub, event, callback as (...args: unknown[]) => void);
  }

  offUserEvent<E extends keyof UserHubEvents>(event: E, callback: (...args: UserHubEvents[E]) => void) {
    this.remove(this.userHub, event, callback as (...args: unknown[]) => void);
  }

  private add(hub: signalR.HubConnection | null, event: string, callback: (...args: unknown[]) => void) {
    let set = this.handlers.get(event);
    if (!set) this.handlers.set(event, (set = new Set()));
    set.add(callback);
    hub?.on(event, callback);
  }

  private remove(hub: signalR.HubConnection | null, event: string, callback: (...args: unknown[]) => void) {
    this.handlers.get(event)?.delete(callback);
    hub?.off(event, callback);
  }

  private async getToken(): Promise<string> {
    return firstValueFrom(this.oidc.getAccessToken());
  }
}
