import { inject, Injectable } from '@angular/core';
import { ApiError } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ChatMessage } from '../../core/models';

export type TutorEvent =
  | { type: 'start'; userMessage: ChatMessage; assistantMessageId: string; searchedBookIds: string[] }
  | { type: 'delta'; text: string }
  | { type: 'done'; message: ChatMessage }
  | { type: 'error'; code: string; message: string; assistantMessageId: string | null };

/**
 * Reads the tutor's Server-Sent Events stream with fetch (EventSource cannot POST or send headers).
 * Aborting the signal stops generation server-side; the partial answer is kept as "stopped".
 */
@Injectable({ providedIn: 'root' })
export class TutorStreamService {
  private readonly auth = inject(AuthService);

  async stream(url: string, body: unknown, signal: AbortSignal, onEvent: (e: TutorEvent) => void): Promise<void> {
    let response = await this.send(url, body, signal);
    if (response.status === 401 && (await this.auth.refresh())) response = await this.send(url, body, signal);

    if (!response.ok || !response.body) {
      let problem: { code?: string } = {};
      try { problem = await response.json(); } catch { /* not JSON */ }
      throw new ApiError({ status: response.status, code: problem.code ?? (response.status === 429 ? 'rate_limited' : 'request_failed') });
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      let index: number;
      while ((index = buffer.indexOf('\n\n')) >= 0) {
        const block = buffer.slice(0, index);
        buffer = buffer.slice(index + 2);
        let type = '';
        let data = '';
        for (const line of block.split('\n')) {
          if (line.startsWith('event: ')) type = line.slice(7).trim();
          else if (line.startsWith('data: ')) data += line.slice(6);
        }
        if (type && data) {
          try { onEvent({ type, ...JSON.parse(data) } as TutorEvent); } catch { /* ignore malformed event */ }
        }
      }
    }
  }

  private send(url: string, body: unknown, signal: AbortSignal): Promise<Response> {
    return fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream', ...(this.auth.token ? { Authorization: `Bearer ${this.auth.token}` } : {}) },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal
    });
  }
}
