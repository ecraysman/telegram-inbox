import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ContactSummaryDto, MessageDto } from '../../core/models';
import { ConversationPanel, isNearBottom } from './conversation-panel';

function msg(id: string, seq: number, contactId = 'c1'): MessageDto {
  return {
    id,
    seq,
    contactId,
    direction: 'inbound',
    text: `mensaje ${id}`,
    occurredAt: '2026-11-05T14:00:00Z',
    readAt: null,
    status: 'received',
    failureReason: null,
  };
}

function contact(id: string): ContactSummaryDto {
  return { id, displayName: `Contacto ${id}`, username: null, unreadCount: 0, lastMessage: null };
}

/** jsdom has no layout: give the history element fake, writable scroll metrics. */
function fakeLayout(element: HTMLElement, scrollHeight: number, clientHeight = 200): void {
  Object.defineProperty(element, 'scrollHeight', { configurable: true, value: scrollHeight });
  Object.defineProperty(element, 'clientHeight', { configurable: true, value: clientHeight });
  Object.defineProperty(element, 'scrollTop', { configurable: true, writable: true, value: 0 });
}

describe('isNearBottom', () => {
  it('is true within 80px of the bottom and false further up', () => {
    expect(isNearBottom({ scrollHeight: 1000, clientHeight: 200, scrollTop: 800 })).toBe(true);
    expect(isNearBottom({ scrollHeight: 1000, clientHeight: 200, scrollTop: 720 })).toBe(true);
    expect(isNearBottom({ scrollHeight: 1000, clientHeight: 200, scrollTop: 719 })).toBe(false);
  });
});

describe('ConversationPanel auto-scroll', () => {
  let fixture: ComponentFixture<ConversationPanel>;
  let history: HTMLElement;

  async function render(contactId: string, messages: MessageDto[]): Promise<void> {
    fixture.componentRef.setInput('contact', contact(contactId));
    fixture.componentRef.setInput('messages', messages);
    await fixture.whenStable();
  }

  function scrollTo(top: number): void {
    history.scrollTop = top;
    history.dispatchEvent(new Event('scroll'));
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ConversationPanel] }).compileComponents();
    fixture = TestBed.createComponent(ConversationPanel);
    fixture.componentRef.setInput('draft', 'hola');
    fixture.componentRef.setInput('messages', []);
    fixture.componentRef.setInput('contact', contact('c1'));
    await fixture.whenStable();
    history = fixture.nativeElement.querySelector('.history') as HTMLElement;
    fakeLayout(history, 1000);
  });

  it('scrolls to the bottom when the conversation is opened', async () => {
    await render('c1', [msg('a', 1)]);
    expect(history.scrollTop).toBe(1000);
  });

  it('does not move an operator who is reading older history', async () => {
    await render('c1', [msg('a', 1)]);
    scrollTo(100);
    fakeLayout(history, 1200);
    history.scrollTop = 100;
    await render('c1', [msg('a', 1), msg('b', 2)]);
    expect(history.scrollTop).toBe(100);
  });

  it('follows new messages when the operator is near the bottom', async () => {
    await render('c1', [msg('a', 1)]);
    scrollTo(790);
    fakeLayout(history, 1200);
    history.scrollTop = 790;
    await render('c1', [msg('a', 1), msg('b', 2)]);
    expect(history.scrollTop).toBe(1200);
  });

  it('scrolls to the bottom after the operator sends, even when scrolled up', async () => {
    await render('c1', [msg('a', 1)]);
    scrollTo(100);
    const button = fixture.nativeElement.querySelector('button.send') as HTMLButtonElement;
    button.click();
    fakeLayout(history, 1200);
    history.scrollTop = 100;
    await render('c1', [msg('a', 1), msg('r', 2)]);
    expect(history.scrollTop).toBe(1200);
  });

  it('scrolls to the bottom when switching to another conversation', async () => {
    await render('c1', [msg('a', 1)]);
    scrollTo(100);
    await render('c2', [msg('x', 3, 'c2')]);
    expect(history.scrollTop).toBe(1000);
  });
});
