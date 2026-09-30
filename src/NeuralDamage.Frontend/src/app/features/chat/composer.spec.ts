import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AttachmentsService } from '../../api/api/attachments.service';
import { ChatMember } from '../../core/models';
import { Composer, MAX_MESSAGE_LENGTH, OutgoingMessage } from './composer';

function member(id: string, displayName: string, memberType: 'user' | 'bot'): ChatMember {
  return {
    id,
    chatId: 'chat',
    userId: memberType === 'user' ? id : null,
    botId: memberType === 'bot' ? id : null,
    role: 'member',
    joinedAt: '',
    displayName,
    avatarUrl: null,
    memberType,
    modelId: null,
    isMuted: false,
    modelStatus: null,
    modelStatusReason: null,
  };
}

describe('Composer', () => {
  const uploads: Blob[] = [];
  let failUpload: HttpErrorResponse | null = null;
  const attachments = {
    apiChatsChatIdAttachmentsPost: (_chatId: string, file: Blob) => {
      if (failUpload) return throwError(() => failUpload);
      uploads.push(file);
      return of({ id: `att-${uploads.length}`, url: '/x', contentType: file.type, sizeBytes: file.size });
    },
  };

  beforeEach(() => {
    uploads.length = 0;
    failUpload = null;
  });

  async function setup() {
    await TestBed.configureTestingModule({
      imports: [Composer],
      providers: [{ provide: AttachmentsService, useValue: attachments }],
    }).compileComponents();
    const fixture = TestBed.createComponent(Composer);
    fixture.componentRef.setInput('members', [
      member('u1', 'Alice', 'user'),
      member('me', 'Nic', 'user'),
      member('b1', 'Gemini', 'bot'),
      member('b2', 'Grok', 'bot'),
    ]);
    fixture.componentRef.setInput('currentUserId', 'me');
    fixture.componentRef.setInput('chatId', 'chat');
    await fixture.whenStable();
    const textarea = (fixture.nativeElement as HTMLElement).querySelector('textarea')!;
    const type = async (value: string) => {
      textarea.value = value;
      textarea.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const press = async (key: string) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
      textarea.dispatchEvent(event);
      await fixture.whenStable();
      return event;
    };
    return { fixture, component: fixture.componentInstance, type, press };
  }

  it('suggests slash commands by prefix', async () => {
    const { component, type } = await setup();
    await type('/mu');
    expect(component.suggestions().map((s) => s.key)).toEqual(['/mute']);
  });

  it('completes bot names after a command that takes one', async () => {
    const { component, type } = await setup();
    await type('/kick g');
    expect(component.suggestions().map((s) => s.result)).toEqual(['/kick Gemini', '/kick Grok']);
  });

  it('never offers yourself as a mention', async () => {
    const { component, type } = await setup();
    await type('@');
    expect(component.suggestions().map((s) => s.label)).toEqual(['Alice', 'Gemini', 'Grok']);
  });

  it('navigates suggestions with the keyboard and picks with Tab', async () => {
    const { component, type, press } = await setup();
    await type('@');
    await press('ArrowDown');
    expect(component.activeIndex()).toBe(1);
    await press('Tab');
    expect(component.content()).toBe('@Gemini ');
  });

  it('picks with Enter instead of sending while the popup is open', async () => {
    const { component, type, press } = await setup();
    const sent: string[] = [];
    component.send.subscribe((e) => sent.push(e.content));
    await type('/st');
    await press('Enter');
    expect(component.content()).toBe('/stop');
    expect(sent).toEqual([]);
  });

  it('sends on Enter once there is nothing to pick', async () => {
    const { component, type, press } = await setup();
    const sent: string[] = [];
    component.send.subscribe((e) => sent.push(e.content));
    await type('hello there');
    await press('Enter');
    expect(sent).toEqual(['hello there']);
    expect(component.content()).toBe('');
  });

  it('refuses to send a message over the limit and says so', async () => {
    const { fixture, component, type, press } = await setup();
    const sent: string[] = [];
    component.send.subscribe((e) => sent.push(e.content));
    await type('x'.repeat(MAX_MESSAGE_LENGTH + 1));
    await press('Enter');
    expect(sent).toEqual([]);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(`${MAX_MESSAGE_LENGTH + 1} / ${MAX_MESSAGE_LENGTH}`);
  });

  it('closes the popup on Esc, then Esc cancels a reply', async () => {
    const { fixture, component, type, press } = await setup();
    let cancelled = 0;
    component.cancelReply.subscribe(() => cancelled++);
    fixture.componentRef.setInput('replyingTo', { id: 'm1', senderName: 'Alice', content: 'hi' });
    await type('@');
    await press('Escape');
    expect(component.suggestions()).toEqual([]);
    expect(cancelled).toBe(0);
    await press('Escape');
    expect(cancelled).toBe(1);
  });

  it('announces typing only for non-empty input', async () => {
    const { component, type } = await setup();
    let typing = 0;
    component.typing.subscribe(() => typing++);
    await type('h');
    await type('');
    expect(typing).toBe(1);
  });
});

describe('Composer images', () => {
  beforeEach(() => {
    URL.createObjectURL = () => 'blob:preview';
    URL.revokeObjectURL = () => undefined;
  });

  function png(name = 'cat.png', size = 10): File {
    return new File([new Uint8Array(size)], name, { type: 'image/png' });
  }

  async function setup(upload: (file: Blob) => unknown = (file) =>
    of({ id: `att-${(file as File).name}`, url: '/x', contentType: 'image/png', sizeBytes: 1 })) {
    await TestBed.configureTestingModule({
      imports: [Composer],
      providers: [{ provide: AttachmentsService, useValue: { apiChatsChatIdAttachmentsPost: (_: string, f: Blob) => upload(f) } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(Composer);
    fixture.componentRef.setInput('members', []);
    fixture.componentRef.setInput('chatId', 'chat');
    await fixture.whenStable();
    const sent: OutgoingMessage[] = [];
    fixture.componentInstance.send.subscribe((m) => sent.push(m));
    return { fixture, component: fixture.componentInstance, sent };
  }

  it('uploads a pasted image and sends it, even without text', async () => {
    const { fixture, component, sent } = await setup();
    const textarea = (fixture.nativeElement as HTMLElement).querySelector('textarea')!;
    const paste = new Event('paste', { bubbles: true, cancelable: true }) as ClipboardEvent;
    Object.defineProperty(paste, 'clipboardData', { value: { files: [png()] } });
    textarea.dispatchEvent(paste);
    await fixture.whenStable();

    expect(paste.defaultPrevented).toBe(true);
    expect(component.images().map((i) => i.id)).toEqual(['att-cat.png']);
    component.onSend();
    expect(sent).toEqual([{ content: '', mentions: [], attachmentIds: ['att-cat.png'] }]);
    expect(component.images()).toEqual([]);
  });

  it('takes dropped images', async () => {
    const { fixture, component } = await setup();
    const box = (fixture.nativeElement as HTMLElement).querySelector('textarea')!.parentElement!;
    const drop = new Event('drop', { bubbles: true, cancelable: true }) as DragEvent;
    Object.defineProperty(drop, 'dataTransfer', { value: { types: ['Files'], files: [png('a.png'), png('b.png')] } });
    box.dispatchEvent(drop);
    await fixture.whenStable();

    expect(component.images().map((i) => i.id)).toEqual(['att-a.png', 'att-b.png']);
  });

  it('refuses other file types, oversized images and a fifth image before uploading', async () => {
    const uploaded: string[] = [];
    const { component } = await setup((file) => {
      uploaded.push((file as File).name);
      return of({ id: (file as File).name, url: '/x', contentType: 'image/png', sizeBytes: 1 });
    });

    component.addImages([new File(['<svg/>'], 'x.svg', { type: 'image/svg+xml' })]);
    component.addImages([png('huge.png', 10 * 1024 * 1024 + 1)]);
    component.addImages([png('1.png'), png('2.png'), png('3.png'), png('4.png'), png('5.png')]);

    expect(uploaded).toEqual(['1.png', '2.png', '3.png', '4.png']);
  });

  it('holds the send button while an upload is in flight', async () => {
    const { component } = await setup(() => new Promise(() => undefined) as never);
    component.addImages([png()]);
    expect(component.canSend()).toBe(false);
  });

  it('drops an image whose upload was refused', async () => {
    const { component } = await setup(() =>
      throwError(() => new HttpErrorResponse({ status: 429, error: "You're uploading images too fast." })),
    );
    component.addImages([png()]);
    await Promise.resolve();
    expect(component.images()).toEqual([]);
  });
});
