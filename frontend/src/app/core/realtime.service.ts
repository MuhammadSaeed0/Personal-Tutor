import { Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { AuthService } from './auth.service';
import { ProcessingStatus } from './models';

/** SignalR connection for live document-processing progress. Falls back silently to polling in the UI. */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;

  /** Latest processing update per book id. */
  readonly processing = signal<Record<string, ProcessingStatus>>({});

  async connect(): Promise<void> {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) return;
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/realtime', { accessTokenFactory: () => this.auth.token ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection.on('processingUpdated', (status: ProcessingStatus) =>
      this.processing.update(map => ({ ...map, [status.bookId]: status })));
    try {
      await this.connection.start();
    } catch {
      // Real-time updates are an enhancement; screens also poll.
    }
  }

  async disconnect(): Promise<void> {
    await this.connection?.stop();
    this.connection = null;
  }
}
