import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth.service';
import { TutorEvent, TutorStreamService } from './tutor-stream';

function streamOf(chunks: string[]): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder();
  return new ReadableStream({
    start(controller) {
      for (const c of chunks) controller.enqueue(encoder.encode(c));
      controller.close();
    }
  });
}

describe('TutorStreamService', () => {
  const auth = { token: 'abc', refresh: vi.fn(async () => true) };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [{ provide: AuthService, useValue: auth }] });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('parses server-sent events split across network chunks and sends the bearer token', async () => {
    const fetchMock = vi.fn(async () => new Response(streamOf([
      'event: start\ndata: {"assistantMessageId":"m1","userMessage":{},"searchedBookIds":[]}\n\n',
      'event: delta\ndata: {"text":"Hel',
      'lo"}\n\nevent: delta\ndata: {"text":" world"}\n\n',
      'event: done\ndata: {"message":{"id":"m1","content":"Hello world"}}\n\n'
    ]), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const events: TutorEvent[] = [];
    await TestBed.inject(TutorStreamService).stream('/api/x', { question: 'q' }, new AbortController().signal, e => events.push(e));

    expect(events.map(e => e.type)).toEqual(['start', 'delta', 'delta', 'done']);
    expect(events.filter(e => e.type === 'delta').map(e => (e as { text: string }).text).join('')).toBe('Hello world');
    const headers = (fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].headers as Record<string, string>;
    expect(headers['Authorization']).toBe('Bearer abc');
  });

  it('surfaces problem-details errors returned before streaming starts', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ code: 'ai_quota_exceeded' }), { status: 429 })));
    await expect(TestBed.inject(TutorStreamService).stream('/api/x', {}, new AbortController().signal, () => undefined))
      .rejects.toMatchObject({ code: 'ai_quota_exceeded' });
  });

  it('refreshes the token once on 401 and retries', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response('', { status: 401 }))
      .mockResolvedValueOnce(new Response(streamOf(['event: delta\ndata: {"text":"ok"}\n\n']), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    const events: TutorEvent[] = [];
    await TestBed.inject(TutorStreamService).stream('/api/x', {}, new AbortController().signal, e => events.push(e));
    expect(auth.refresh).toHaveBeenCalled();
    expect(events).toHaveLength(1);
  });
});
