import { TestBed } from '@angular/core/testing';
import { Composer } from './composer';

describe('Composer', () => {
  async function setup(draft: string) {
    await TestBed.configureTestingModule({ imports: [Composer] }).compileComponents();
    const fixture = TestBed.createComponent(Composer);
    fixture.componentRef.setInput('draft', draft);
    await fixture.whenStable();
    const sent: unknown[] = [];
    fixture.componentInstance.send.subscribe((v) => sent.push(v));
    const textarea = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    return { fixture, sent, textarea };
  }

  it('sends on Enter', async () => {
    const { sent, textarea } = await setup('hola');
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
    expect(sent.length).toBe(1);
  });

  it('does not send on Shift+Enter', async () => {
    const { sent, textarea } = await setup('hola');
    const event = new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, cancelable: true });
    textarea.dispatchEvent(event);
    expect(sent.length).toBe(0);
    expect(event.defaultPrevented).toBe(false);
  });

  it('blocks blank and over-limit drafts', async () => {
    const blank = await setup('  ');
    blank.textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
    expect(blank.sent.length).toBe(0);

    TestBed.resetTestingModule();
    const long = await setup('x'.repeat(4097));
    long.textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
    expect(long.sent.length).toBe(0);
    expect(long.fixture.nativeElement.textContent).toContain('4097 / 4096');
  });
});
