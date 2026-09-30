import { TestBed } from '@angular/core/testing';
import { PkMarkdown } from '@prompt-kit/markdown/pk-markdown';

describe('message markdown', () => {
  async function render(content: string, allowImage?: (src: string) => boolean): Promise<HTMLElement> {
    await TestBed.configureTestingModule({ imports: [PkMarkdown] }).compileComponents();
    const fixture = TestBed.createComponent(PkMarkdown);
    fixture.componentRef.setInput('content', content);
    if (allowImage) fixture.componentRef.setInput('allowImage', allowImage);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('drops a login form someone pastes into a message', async () => {
    const el = await render(
      'hi <form action="https://evil.example"><input name="p"><button>Login</button></form>',
    );

    expect(el.querySelector('form')).toBeNull();
    expect(el.querySelector('button')).toBeNull();
    expect(el.querySelector('[action]')).toBeNull();
    expect(el.textContent).toContain('hi');
  });

  it('still renders markdown', async () => {
    const el = await render('**bold** and `code`');

    expect(el.querySelector('strong')?.textContent).toBe('bold');
  });
});

describe('remote images in messages', () => {
  async function render(content: string, allowImage?: (src: string) => boolean): Promise<HTMLElement> {
    await TestBed.configureTestingModule({ imports: [PkMarkdown] }).compileComponents();
    const fixture = TestBed.createComponent(PkMarkdown);
    fixture.componentRef.setInput('content', content);
    if (allowImage) fixture.componentRef.setInput('allowImage', allowImage);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('turns a markdown image into a link instead of loading it', async () => {
    const el = await render('look ![a cat](https://tracker.example/cat.png)');

    expect(el.querySelector('img')).toBeNull();
    const link = el.querySelector('a')!;
    expect(link.getAttribute('href')).toBe('https://tracker.example/cat.png');
    expect(link.textContent).toContain('a cat');
    expect(link.getAttribute('rel')).toContain('noreferrer');
  });

  it('turns an html img into a link too', async () => {
    const el = await render('<img src="https://tracker.example/pixel.gif" width="1">');

    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('a')?.getAttribute('href')).toBe('https://tracker.example/pixel.gif');
  });

  it('strips the other ways html can fetch a remote resource', async () => {
    const el = await render(
      '<div style="background:url(https://t.example/a)">x</div>' +
        '<video src="https://t.example/v.mp4" poster="https://t.example/p.png"></video>' +
        '<input type="image" src="https://t.example/i.png">' +
        '<style>body{background:url(https://t.example/b)}</style>' +
        '<svg><image href="https://t.example/s.png"></image></svg>' +
        '<picture><source srcset="https://t.example/x.png"></picture>',
    );

    expect(el.innerHTML).not.toContain('t.example');
  });

  it("still renders the app's own attachments as images when allowed", async () => {
    const own = '/api/chats/00000000-0000-0000-0000-000000000001/attachments/00000000-0000-0000-0000-000000000002';
    const el = await render(`![photo](${own}) ![x](https://tracker.example/x.png)`, (src) => src === own);

    expect(el.querySelectorAll('img').length).toBe(1);
    expect(el.querySelector('img')?.getAttribute('src')).toBe(own);
  });
});
