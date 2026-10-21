import { computed, DestroyRef, inject, InjectionToken, Service, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom, Observable, Subject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ChatHubEvents, UserHubEvents } from './hub-events';

/** Reconnect delays in ms: quick at first, then every 30 s for as long as it takes. */
export const RETRY_DELAYS = [0, 2_000, 5_000, 10_000];
export const RETRY_CEILING = 30_000;

export const retryDelay = (attempt: number) => RETRY_DELAYS[attempt] ?? RETRY_CEILING;

/** Never gives up; the library's default stops after four tries and leaves the page silently dead. */
const retryForever: signalR.IRetryPolicy = {
  nextRetryDelayInMilliseconds: ({ previousRetryCount }) => retryDelay(previousRetryCount),
};

/** The slice of `HubConnection` this service uses, so tests can drive a fake one. */
export type HubConnectionLike = Pick<
  signalR.HubConnection,
  'state' | 'start' | 'stop' | 'invoke' | 'send' | 'on' | 'off' | 'onreconnecting' | 'onreconnected' | 'onclose'
>;

/** Builds the connection for a hub path; replaced in tests. */
export const HUB_CONNECTION_FACTORY = new InjectionToken<
  (path: string, token: () => Promise<string>) => HubConnectionLike
>('HUB_CONNECTION_FACTORY', {
  factory: () => (path, token) =>
    new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiBaseUrl}${path}`, { accessTokenFactory: token })
      .withAutomaticReconnect(retryForever)
      // Information logs each connect URL, access_token query string included.
      .configureLogging(signalR.LogLevel.Warning)
      .build(),
});

/**
 * What the connection banner shows. `offline` is the browser having no network at all;
 * `reconnecting` covers every other way of not being connected while it keeps trying.
 */
export type ConnectionState = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'offline';
/** Emitted after an outage; `since` is when it began, by this browser's clock. */
export interface Reconnected {
  since: number;
}

type HubState = 'idle' | 'connecting' | 'connected' | 'reconnecting';

const CHAT_HUB = '/hubs/chat';
const USER_HUB = '/hubs/user';

/**
 * One hub connection that does not stay down: the library's automatic reconnect covers a dropped
 * connection, and whenever it stops (or the first start fails) the connection is started again with
 * the same backoff, forever.
 */
class LiveHub {
  readonly state = signal<HubState>('idle');
  private timer: ReturnType<typeof setTimeout> | null = null;
  private attempt = 0;
  private stopped = false;

  constructor(
    readonly connection: HubConnectionLike,
    /** Runs on every (re)connect before the hub counts as connected, e.g. to rejoin groups. */
    private readonly onConnected: () => Promise<void>,
    private readonly changed: () => void,
  ) {
    connection.onreconnecting(() => this.set('reconnecting'));
    connection.onreconnected(() => void this.connected());
    connection.onclose(() => {
      if (this.stopped) return;
      this.set('reconnecting');
      this.schedule();
    });
  }

  start(): Promise<void> {
    this.set('connecting');
    return this.tryStart();
  }

  /** Retries now instead of waiting out the backoff; for `online` and the tab becoming visible. */
  async kick(): Promise<void> {
    if (this.stopped) return;
    switch (this.connection.state) {
      case signalR.HubConnectionState.Disconnected:
        this.clearTimer();
        this.attempt = 0;
        await this.tryStart();
        break;
      case signalR.HubConnectionState.Reconnecting:
        // The library sleeps through its own backoff; stopping it lands in onclose, which restarts
        // straight away.
        this.attempt = 0;
        await this.connection.stop().catch(() => undefined);
        break;
    }
  }

  async stop(): Promise<void> {
    this.stopped = true;
    this.clearTimer();
    await this.connection.stop().catch(() => undefined);
    this.state.set('idle');
  }

  private async tryStart(): Promise<void> {
    if (this.stopped || this.connection.state !== signalR.HubConnectionState.Disconnected) return;
    try {
      await this.connection.start();
    } catch {
      if (this.stopped) return;
      this.set('reconnecting');
      this.schedule();
      return;
    }
    await this.connected();
  }

  private async connected(): Promise<void> {
    if (this.stopped) return;
    this.attempt = 0;
    await this.onConnected().catch(() => undefined);
    this.set('connected');
  }

  private schedule() {
    this.clearTimer();
    const delay = retryDelay(this.attempt++);
    this.timer = setTimeout(() => {
      this.timer = null;
      void this.tryStart();
    }, delay);
  }

  private clearTimer() {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }

  private set(state: HubState) {
    this.state.set(state);
    this.changed();
  }
}

/**
 * The two hubs: `/hubs/chat` for everything inside a chat, `/hubs/user` for the chat list. Handlers
 * registered before the connections exist are kept and attached once they do.
 *
 * Events sent while a client is disconnected are gone for good, so after any outage `reconnected$`
 * fires once both hubs are back; whatever shows live data subscribes to it and reloads what it shows.
 */
@Service()
export class SignalRService {
  private readonly oidc = inject(OidcSecurityService);
  private readonly createConnection = inject(HUB_CONNECTION_FACTORY);
  private chatHub: LiveHub | null = null;
  private userHub: LiveHub | null = null;

  private readonly hubStates = signal<readonly HubState[]>([]);
  private readonly online = signal(typeof navigator === 'undefined' ? true : navigator.onLine);

  /** For the connection banner: the worse of the two hubs, or offline when the browser says so. */
  readonly state = computed<ConnectionState>(() => {
    const states = this.hubStates();
    if (states.length === 0) return 'idle';
    // A socket can look open for a while after the network is gone; the browser knows sooner.
    if (!this.online()) return 'offline';
    for (const state of ['reconnecting', 'connecting', 'idle'] as const)
      if (states.includes(state)) return state;
    return 'connected';
  });
  readonly connected = computed(() => this.state() === 'connected');

  private readonly reconnectedSubject = new Subject<Reconnected>();
  /** Fires when both hubs are connected again after any outage (not on the first connect). */
  readonly reconnected$: Observable<Reconnected> = this.reconnectedSubject.asObservable();

  private wasConnected = false;
  /** When the current outage began (`Date.now()`), or null while connected. */
  private downSince: number | null = null;

  /** Joined chats, rejoined after a reconnect since the server forgets a dropped connection's groups. */
  private readonly joined = new Set<string>();

  /** Guards against concurrent callers building duplicate connections. */
  private starting: Promise<void> | null = null;

  constructor() {
    if (typeof window === 'undefined') return;
    const onOnline = () => {
      this.online.set(true);
      void this.kick();
    };
    const onOffline = () => this.online.set(false);
    const onVisible = () => {
      if (document.visibilityState === 'visible') void this.kick();
    };
    window.addEventListener('online', onOnline);
    window.addEventListener('offline', onOffline);
    document.addEventListener('visibilitychange', onVisible);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('online', onOnline);
      window.removeEventListener('offline', onOffline);
      document.removeEventListener('visibilitychange', onVisible);
    });
  }

  async start(): Promise<void> {
    if (this.chatHub && this.userHub) return;
    this.starting ??= this.connect().finally(() => (this.starting = null));
    return this.starting;
  }

  /** Retries right away: a hub waiting out its backoff, or a start that had no token yet. */
  async kick(): Promise<void> {
    if (!this.chatHub || !this.userHub) {
      if (this.wasConnected || this.starting) await this.start();
      return;
    }
    await Promise.all([this.chatHub.kick(), this.userHub.kick()]);
  }

  private async connect(): Promise<void> {
    const token = await this.getToken();
    if (!token) return;

    const tokenFactory = () => this.getToken();
    this.chatHub = new LiveHub(
      this.attach(this.createConnection(CHAT_HUB, tokenFactory), CHAT_HUB),
      () => this.rejoin(),
      () => this.update(),
    );
    this.userHub = new LiveHub(
      this.attach(this.createConnection(USER_HUB, tokenFactory), USER_HUB),
      async () => undefined,
      () => this.update(),
    );
    await Promise.all([this.chatHub.start(), this.userHub.start()]);
  }

  /** The server forgets a dropped connection's groups; chats and post threads joined are joined again. */
  private async rejoin(): Promise<void> {
    const connection = this.chatHub?.connection;
    if (!connection) return;
    await Promise.all([
      ...[...this.joined].map((chatId) => connection.invoke('JoinChat', chatId).catch(() => undefined)),
      ...[...this.posts].map((postId) => connection.invoke('JoinPost', postId).catch(() => undefined)),
    ]);
  }

  private update() {
    const hubs = [this.chatHub, this.userHub].filter((h): h is LiveHub => !!h);
    const states = hubs.map((h) => h.state());
    this.hubStates.set(states);
    const allUp = states.length === 2 && states.every((s) => s === 'connected');
    if (allUp) {
      const since = this.downSince;
      this.wasConnected = true;
      this.downSince = null;
      if (since !== null) this.reconnectedSubject.next({ since });
    } else if (this.wasConnected) {
      this.downSince ??= Date.now();
    }
  }

  private attach(connection: HubConnectionLike, path: string) {
    const isChat = path === CHAT_HUB;
    for (const [event, handlers] of this.handlers)
      if (isChat === this.chatEvents.has(event)) handlers.forEach((h) => connection.on(event, h));
    return connection;
  }

  async stop(): Promise<void> {
    const hubs = [this.chatHub, this.userHub];
    this.chatHub = null;
    this.userHub = null;
    this.joined.clear();
    this.posts.clear();
    this.wasConnected = false;
    this.downSince = null;
    await Promise.all(hubs.map((hub) => hub?.stop()));
    this.hubStates.set([]);
  }

  async joinChat(chatId: string): Promise<void> {
    await this.start();
    this.joined.add(chatId);
    const connection = this.chatHub?.connection;
    if (connection?.state === signalR.HubConnectionState.Connected)
      await connection.invoke('JoinChat', chatId);
  }

  async leaveChat(chatId: string): Promise<void> {
    this.joined.delete(chatId);
    const connection = this.chatHub?.connection;
    if (connection?.state === signalR.HubConnectionState.Connected)
      await connection.invoke('LeaveChat', chatId);
  }

  /** Joined post threads, for the typing indicator; rejoined after a reconnect like chats. */
  private readonly posts = new Set<string>();

  async joinPost(postId: string): Promise<void> {
    await this.start();
    this.posts.add(postId);
    const connection = this.chatHub?.connection;
    if (connection?.state === signalR.HubConnectionState.Connected)
      await connection.invoke('JoinPost', postId).catch(() => undefined);
  }

  async leavePost(postId: string): Promise<void> {
    this.posts.delete(postId);
    const connection = this.chatHub?.connection;
    if (connection?.state === signalR.HubConnectionState.Connected)
      await connection.invoke('LeavePost', postId).catch(() => undefined);
  }

  /** Someone is writing a comment under a post. Throttled by the caller. */
  startFeedTyping(postId: string): void {
    const connection = this.chatHub?.connection;
    if (connection?.state !== signalR.HubConnectionState.Connected) return;
    void connection.send('StartFeedTyping', postId).catch(() => undefined);
  }

  /** Fire-and-forget; callers throttle, and a dropped ping only hides the indicator early. */
  startTyping(chatId: string): void {
    const connection = this.chatHub?.connection;
    if (connection?.state !== signalR.HubConnectionState.Connected) return;
    void connection.send('StartTyping', chatId).catch(() => undefined);
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

  private add(hub: LiveHub | null, event: string, callback: (...args: unknown[]) => void) {
    let set = this.handlers.get(event);
    if (!set) this.handlers.set(event, (set = new Set()));
    set.add(callback);
    hub?.connection.on(event, callback);
  }

  private remove(hub: LiveHub | null, event: string, callback: (...args: unknown[]) => void) {
    this.handlers.get(event)?.delete(callback);
    hub?.connection.off(event, callback);
  }

  private async getToken(): Promise<string> {
    return firstValueFrom(this.oidc.getAccessToken());
  }
}
