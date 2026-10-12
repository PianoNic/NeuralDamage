import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AttachmentsService } from '../../api/api/attachments.service';
import { AppConfig } from '../../core/app-config';
import { ChatMember } from '../../core/models';
import { Composer, MAX_MESSAGE_LENGTH, OutgoingMessage } from './composer';
import { loadEmojis, recentEmojis } from './emoji';

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

  it('offers /compact and /summarize', async () => {
    const { component, type } = await setup();
    await type('/c');
    expect(component.suggestions().map((s) => s.key)).toEqual(['/clear', '/compact']);
    await type('/su');
    expect(component.suggestions().map((s) => s.result)).toEqual(['/summarize']);
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

  it('uses the limits the server serves instead of its own', async () => {
    const uploaded: string[] = [];
    const { component } = await setup((file) => {
      uploaded.push((file as File).name);
      return of({ id: (file as File).name, url: '/x', contentType: 'image/png', sizeBytes: 1 });
    });
    TestBed.inject(AppConfig).apply({
      attachments: { maxBytes: 100, maxPerMessage: 2, contentTypes: ['image/png'] },
    });

    component.addImages([png('big.png', 101)]);
    component.addImages([png('1.png'), png('2.png'), png('3.png')]);

    expect(uploaded).toEqual(['1.png', '2.png']);
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

describe('Composer mention chips', () => {
  async function setup() {
    await TestBed.configureTestingModule({
      imports: [Composer],
      providers: [{ provide: AttachmentsService, useValue: {} }],
    }).compileComponents();
    const fixture = TestBed.createComponent(Composer);
    fixture.componentRef.setInput('members', [
      member('u1', 'Alice', 'user'),
      member('me', 'Nic', 'user'),
      member('u2', 'Maggus Rühl', 'user'),
      member('b1', 'Gemini', 'bot'),
      member('b2', 'Grok', 'bot'),
    ]);
    fixture.componentRef.setInput('currentUserId', 'me');
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const textarea = host.querySelector('textarea')!;
    const component = fixture.componentInstance;
    const sent: string[] = [];
    component.send.subscribe((m) => sent.push(m.content));

    /** Types (or pastes) text the way the browser reports it: the new value, then an input event. */
    const type = async (value: string, inputType = 'insertText') => {
      textarea.value = value;
      textarea.dispatchEvent(new InputEvent('input', { inputType }));
      await fixture.whenStable();
    };
    const press = async (key: string, init: KeyboardEventInit = {}) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
      textarea.dispatchEvent(event);
      await fixture.whenStable();
      return event;
    };
    const select = (start: number, end = start) => {
      textarea.focus();
      textarea.setSelectionRange(start, end);
    };
    const chips = () =>
      [...host.querySelectorAll('[data-testid=composer-mirror] [data-mention]')].map((c) => c.textContent);
    return { fixture, component, textarea, sent, type, press, select, chips };
  }

  it('turns a picked member into a chip and sends the plain @Name text', async () => {
    const { component, textarea, sent, type, press, chips } = await setup();
    await type('hey @Ge');
    await press('Tab');
    expect(component.content()).toBe('hey @Gemini ');
    expect(chips()).toEqual(['@Gemini']);
    // The caret sits after the chip and its space, ready to type on.
    expect(textarea.selectionStart).toBe('hey @Gemini '.length);

    await type('hey @Gemini what now?');
    await press('Enter');
    expect(sent).toEqual(['hey @Gemini what now?']);
  });

  it('keeps the list open through a name with a space and an umlaut', async () => {
    const { component, type, press, chips } = await setup();
    await type('@Maggus R');
    expect(component.suggestions().map((s) => s.label)).toEqual(['Maggus Rühl']);
    await type('@Maggus Rü');
    expect(component.suggestions().map((s) => s.label)).toEqual(['Maggus Rühl']);
    await press('Enter');
    expect(component.content()).toBe('@Maggus Rühl ');
    expect(chips()).toEqual(['@Maggus Rühl']);
  });

  it('makes a chip of an exact name typed or pasted after @, and of two in a row', async () => {
    const { type, chips } = await setup();
    await type('@alice', 'insertFromPaste');
    expect(chips()).toEqual(['@alice']);
    await type('@Alice @Grok ');
    expect(chips()).toEqual(['@Alice', '@Grok']);
    // Not a name: "@Alicex" is no mention, to the server either.
    await type('@Alicex');
    expect(chips()).toEqual([]);
  });

  it('does not reopen the list for a longer name once one is picked', async () => {
    const { fixture, component, type, press } = await setup();
    fixture.componentRef.setInput('members', [member('r1', 'Rex', 'bot'), member('r2', 'Rex Jr', 'bot')]);
    await type('@Re');
    await press('Enter');
    expect(component.content()).toBe('@Rex ');
    expect(component.suggestions()).toEqual([]);
  });

  it('selects a whole chip on Backspace after it and Delete before it', async () => {
    const { textarea, press, select, type } = await setup();
    await type('@Alice @Maggus Rühl hi');
    select(6);
    await press('Backspace');
    expect([textarea.selectionStart, textarea.selectionEnd]).toEqual([0, 6]);

    select(7);
    await press('Delete');
    expect([textarea.selectionStart, textarea.selectionEnd]).toEqual([7, 19]);

    // Plain text is left to the browser.
    select(22);
    const event = await press('Backspace');
    expect([textarea.selectionStart, textarea.selectionEnd, event.defaultPrevented]).toEqual([22, 22, false]);
  });

  it('grows a selection that cuts into chips to cover them', async () => {
    const { textarea, press, select, type } = await setup();
    await type('@Alice and @Grok');
    select(3, 13);
    await press('Backspace');
    expect([textarea.selectionStart, textarea.selectionEnd]).toEqual([0, 16]);
  });

  it('removes all of a chip when a phone keyboard deletes one letter of it', async () => {
    const { component, textarea, type } = await setup();
    await type('hi @Alice ok');
    await type('hi @Alic ok', 'deleteContentBackward');
    expect(component.content()).toBe('hi  ok');
    expect(textarea.selectionStart).toBe(3);
    // Undo puts the text back, and with it the chip.
    await type('hi @Alice ok', 'historyUndo');
    expect(component.mentions().map((m) => m.name)).toEqual(['Alice']);
  });

  it('steps over a chip with the arrow keys and moves a caret out of one', async () => {
    const { component, textarea, press, select, type } = await setup();
    await type('x @Maggus Rühl y');
    select(14);
    expect((await press('ArrowLeft')).defaultPrevented).toBe(true);
    expect(textarea.selectionStart).toBe(2);
    expect((await press('ArrowRight')).defaultPrevented).toBe(true);
    expect(textarea.selectionStart).toBe(14);

    // A click lands on the nearer edge.
    select(4);
    component.snapSelection(true);
    expect(textarea.selectionStart).toBe(2);
    select(12);
    component.snapSelection(true);
    expect(textarea.selectionStart).toBe(14);
  });

  it('chips nicknames too', async () => {
    const { fixture, type, chips } = await setup();
    fixture.componentRef.setInput('mentionNames', ['Gemini', 'gem']);
    await type('@gem hi');
    expect(chips()).toEqual(['@gem']);
  });

  it('appends a mention from the People panel as a chip', async () => {
    const { fixture, component, chips, type } = await setup();
    await type('hello');
    component.insertMention('Maggus Rühl');
    await fixture.whenStable();
    expect(component.content()).toBe('hello @Maggus Rühl ');
    expect(chips()).toEqual(['@Maggus Rühl']);
    expect(component.suggestions()).toEqual([]);
  });

  it('sends on Enter but not on Shift+Enter or while composing', async () => {
    const { sent, type, press } = await setup();
    await type('@Alice hi');
    await press('Enter', { shiftKey: true });
    await press('Enter', { isComposing: true });
    expect(sent).toEqual([]);
    await press('Enter');
    expect(sent).toEqual(['@Alice hi']);
  });
});

describe('Composer emoji shortcodes', () => {
  beforeEach(() => localStorage.clear());

  async function setup() {
    await TestBed.configureTestingModule({
      imports: [Composer],
      providers: [{ provide: AttachmentsService, useValue: {} }],
    }).compileComponents();
    const fixture = TestBed.createComponent(Composer);
    fixture.componentRef.setInput('members', [member('u1', 'Alice', 'user'), member('b1', 'Gemini', 'bot')]);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const textarea = host.querySelector('textarea')!;
    const component = fixture.componentInstance;
    const sent: string[] = [];
    component.send.subscribe((m) => sent.push(m.content));

    /** Types (or pastes) text the way the browser reports it: the new value with the caret at the end. */
    const type = async (value: string, inputType = 'insertText') => {
      textarea.value = value;
      textarea.setSelectionRange(value.length, value.length);
      textarea.dispatchEvent(new InputEvent('input', { inputType }));
      // The first ":" loads the dataset; let that land.
      await loadEmojis();
      await fixture.whenStable();
    };
    const press = async (key: string) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
      textarea.dispatchEvent(event);
      await fixture.whenStable();
      return event;
    };
    const chips = () =>
      [...host.querySelectorAll('[data-testid=composer-mirror] [data-mention]')].map((c) => c.textContent);
    return { fixture, component, textarea, sent, type, press, chips };
  }

  it('suggests emoji after a colon and two letters, the exact shortcode first', async () => {
    const { component, fixture, type } = await setup();
    await type('so sad :s');
    expect(component.suggestions()).toEqual([]);
    await type('so sad :sob');
    const list = component.suggestions();
    expect(list[0]).toMatchObject({ kind: 'emoji', emoji: '😭', label: 'sob', result: 'so sad 😭' });
    expect(list.length).toBeLessThanOrEqual(8);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(':sob:');
  });

  it('finds emoji by keyword, so :laugh offers 😂', async () => {
    const { component, type } = await setup();
    await type(':laugh');
    expect(component.suggestions().map((s) => s.emoji)).toContain('😂');
  });

  it('leaves times, URLs, chips and words with a colon alone', async () => {
    const { component, type } = await setup();
    for (const text of ['meet at 10:30', 'see https://example.com', 'hey:so', '@Alice:so', 'at 10:30:']) {
      await type(text);
      expect(component.suggestions()).toEqual([]);
      expect(component.content()).toBe(text);
    }
  });

  it('inserts the pick with Enter or Tab, without sending', async () => {
    const { component, sent, type, press } = await setup();
    await type('ugh :sob');
    await press('Enter');
    expect(component.content()).toBe('ugh 😭');
    expect(sent).toEqual([]);

    await type('ugh 😭 :fir');
    await press('ArrowDown');
    await press('ArrowUp');
    await press('Tab');
    expect(component.content()).toBe('ugh 😭 🔥');
    expect(recentEmojis()).toEqual(['🔥', '😭']);

    await press('Enter');
    expect(sent).toEqual(['ugh 😭 🔥']);
  });

  it('closes the list on Escape and keeps the text', async () => {
    const { component, type, press } = await setup();
    await type(':sob');
    const event = await press('Escape');
    expect(event.defaultPrevented).toBe(true);
    expect(component.suggestions()).toEqual([]);
    expect(component.content()).toBe(':sob');
  });

  it('turns a complete known :code: into its emoji as it is typed, and leaves unknown ones', async () => {
    const { component, textarea, type } = await setup();
    await type('lol :joy');
    await type('lol :joy:');
    expect(component.content()).toBe('lol 😂');
    expect(textarea.selectionStart).toBe('lol 😂'.length);

    await type('lol 😂 :nope:');
    expect(component.content()).toBe('lol 😂 :nope:');
  });

  it('converts the codes in pasted text, keeping the caret after the paste', async () => {
    const { component, textarea, type } = await setup();
    await type('hi :x');
    await type('hi :fire::skull: at 10:30 :nope: end', 'insertFromPaste');
    expect(component.content()).toBe('hi 🔥💀 at 10:30 :nope: end');
    expect(textarea.selectionStart).toBe(component.content().length);
  });

  it('keeps chips and emoji together in one message', async () => {
    const { component, sent, type, press, chips } = await setup();
    await type('@Gemini :skull');
    await type('@Gemini :skull:');
    expect(component.content()).toBe('@Gemini 💀');
    expect(chips()).toEqual(['@Gemini']);
    await press('Enter');
    expect(sent).toEqual(['@Gemini 💀']);
  });

  it('leaves text being composed by an IME alone', async () => {
    const { component, type } = await setup();
    await type('x :joy:', 'insertCompositionText');
    expect(component.content()).toBe('x :joy:');
  });

  it('inserts a picked emoji at the caret', async () => {
    const { component, textarea, type } = await setup();
    await type('ab');
    textarea.focus();
    textarea.setSelectionRange(1, 1);
    component.insertEmoji('🔥');
    expect(component.content()).toBe('a🔥b');
  });
});
