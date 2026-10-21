import { TestBed } from '@angular/core/testing';
import { HubConnectionState } from '@microsoft/signalr';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of } from 'rxjs';
import {
  HUB_CONNECTION_FACTORY,
  HubConnectionLike,
  Reconnected,
  RETRY_CEILING,
  SignalRService,
} from './signalr.service';

/** Stands in for a HubConnection: the test decides when it drops, recovers or gives up. */
class FakeHub {
  state = HubConnectionState.Disconnected;
  /** Outcomes of the next start() calls; an empty queue means it succeeds. */
  failNextStarts = 0;
  private reconnectingCb: () => void = () => undefined;
  private reconnectedCb: () => void = () => undefined;
  private closeCb: () => void = () => undefined;

  readonly start = vi.fn(async () => {
    if (this.failNextStarts > 0) {
      this.failNextStarts--;
      throw new Error('API down');
    }
    this.state = HubConnectionState.Connected;
  });
  readonly stop = vi.fn(async () => {
    const was = this.state;
    this.state = HubConnectionState.Disconnected;
    if (was !== HubConnectionState.Disconnected) this.closeCb();
  });
  readonly invoke = vi.fn(async () => undefined);
  readonly send = vi.fn(async () => undefined);
  readonly on = vi.fn();
  readonly off = vi.fn();
  onreconnecting(cb: () => void) {
    this.reconnectingCb = cb;
  }
  onreconnected(cb: () => void) {
    this.reconnectedCb = cb;
  }
  onclose(cb: () => void) {
    this.closeCb = cb;
  }

  /** The socket died; the library's automatic reconnect takes over. */
  drop() {
    this.state = HubConnectionState.Reconnecting;
    this.reconnectingCb();
  }
  /** The library's automatic reconnect got through. */
  recover() {
    this.state = HubConnectionState.Connected;
    this.reconnectedCb();
  }
  /** The library stopped reconnecting (or the server refused a reconnect). */
  giveUp() {
    this.state = HubConnectionState.Disconnected;
    this.closeCb();
  }
}

describe('SignalRService', () => {
  let chat: FakeHub;
  let user: FakeHub;
  let service: SignalRService;
  let reconnects: Reconnected[];

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    chat = new FakeHub();
    user = new FakeHub();
    TestBed.configureTestingModule({
      providers: [
        { provide: OidcSecurityService, useValue: { getAccessToken: () => of('token') } },
        {
          provide: HUB_CONNECTION_FACTORY,
          useValue: (path: string) => (path === '/hubs/chat' ? chat : user) as unknown as HubConnectionLike,
        },
      ],
    });
    service = TestBed.inject(SignalRService);
    reconnects = [];
    service.reconnected$.subscribe((event) => reconnects.push(event));
    await service.start();
  });

  afterEach(async () => {
    await service.stop();
    vi.useRealTimers();
  });

  it('connects both hubs, and the first connect is not a reconnect', () => {
    expect(service.state()).toBe('connected');
    expect(reconnects).toEqual([]);
  });

  it('shows reconnecting while a hub is down and connected once it is back', async () => {
    await service.joinChat('c1');
    chat.invoke.mockClear();

    chat.drop();
    expect(service.state()).toBe('reconnecting');

    chat.recover();
    await vi.advanceTimersByTimeAsync(0);

    expect(service.state()).toBe('connected');
    expect(chat.invoke).toHaveBeenCalledWith('JoinChat', 'c1');
    expect(reconnects).toHaveLength(1);
  });

  it('says when the outage began, and fires once when both hubs went down together', async () => {
    const downAt = Date.now();
    chat.drop();
    user.drop();
    await vi.advanceTimersByTimeAsync(5_000);
    chat.recover();
    await vi.advanceTimersByTimeAsync(0);
    expect(reconnects).toEqual([]);

    user.recover();
    await vi.advanceTimersByTimeAsync(0);

    expect(reconnects).toEqual([{ since: downAt }]);
  });

  it('starts the connection again when automatic reconnect gives up, and keeps trying', async () => {
    chat.failNextStarts = 2;
    chat.giveUp();
    expect(service.state()).toBe('reconnecting');

    await vi.advanceTimersByTimeAsync(0); // first retry fails at once
    expect(chat.start).toHaveBeenCalledTimes(2);
    expect(service.state()).toBe('reconnecting');

    await vi.advanceTimersByTimeAsync(2_000); // second fails after 2 s
    expect(chat.start).toHaveBeenCalledTimes(3);

    await vi.advanceTimersByTimeAsync(5_000); // third gets through
    expect(chat.start).toHaveBeenCalledTimes(4);
    expect(service.state()).toBe('connected');
    expect(reconnects).toHaveLength(1);
  });

  it('never stops retrying, settling at the ceiling', async () => {
    chat.failNextStarts = 50;
    chat.giveUp();
    await vi.advanceTimersByTimeAsync(10 * 60_000);

    // 0, 2, 5 and 10 s, then every 30 s.
    const expected = 1 + 4 + Math.floor((10 * 60_000 - 17_000) / RETRY_CEILING);
    expect(chat.start.mock.calls.length).toBeGreaterThanOrEqual(expected);
    expect(service.state()).toBe('reconnecting');
  });

  it('shows offline while the browser has no network, and retries the moment it is back', async () => {
    chat.failNextStarts = 100;
    window.dispatchEvent(new Event('offline'));
    chat.giveUp();
    await vi.advanceTimersByTimeAsync(20_000);
    expect(service.state()).toBe('offline');

    const calls = chat.start.mock.calls.length;
    chat.failNextStarts = 0;
    window.dispatchEvent(new Event('online'));
    await vi.advanceTimersByTimeAsync(0);

    expect(chat.start.mock.calls.length).toBe(calls + 1);
    expect(service.state()).toBe('connected');
    expect(reconnects).toHaveLength(1);
  });

  it('shows offline as soon as the browser says so, before the socket notices', () => {
    window.dispatchEvent(new Event('offline'));
    expect(service.state()).toBe('offline');

    window.dispatchEvent(new Event('online'));
    expect(service.state()).toBe('connected');
  });

  it('cuts short the automatic reconnect backoff when the network comes back', async () => {
    chat.drop();
    window.dispatchEvent(new Event('online'));
    await vi.advanceTimersByTimeAsync(0);

    expect(chat.stop).toHaveBeenCalled();
    expect(chat.start).toHaveBeenCalledTimes(2);
    expect(service.state()).toBe('connected');
  });

  it('retries straight away when the tab becomes visible again', async () => {
    chat.failNextStarts = 100;
    chat.giveUp();
    await vi.advanceTimersByTimeAsync(60_000); // now waiting out a 30 s backoff
    const calls = chat.start.mock.calls.length;

    chat.failNextStarts = 0;
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(0);

    expect(chat.start.mock.calls.length).toBe(calls + 1);
    expect(service.state()).toBe('connected');
  });

  it('ignores the tab being hidden', async () => {
    chat.giveUp();
    chat.failNextStarts = 100;
    await vi.advanceTimersByTimeAsync(60_000);
    const calls = chat.start.mock.calls.length;

    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(0);

    expect(chat.start.mock.calls.length).toBe(calls);
  });

  it('does not restart a connection it was asked to stop', async () => {
    await service.stop();
    await vi.advanceTimersByTimeAsync(60_000);

    expect(chat.start).toHaveBeenCalledTimes(1);
    expect(service.state()).toBe('idle');
  });
});
