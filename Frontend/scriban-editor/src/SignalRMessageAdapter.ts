import {
  HubConnection,
  HubConnectionState,
} from '@microsoft/signalr';
import type { Message } from 'vscode-languageserver-protocol';
import {
  AbstractMessageReader,
  type DataCallback,
  type MessageReader,
} from 'vscode-jsonrpc/lib/common/messageReader';
import {
  AbstractMessageWriter,
  type MessageWriter,
} from 'vscode-jsonrpc/lib/common/messageWriter';

/**
 * # Scriban Language Server: Specification 14 (Updated for monaco-languageclient v10+)
 * ## SignalR Transport Adapter
 *
 * Provides MessageReader/MessageWriter implementation for SignalR transport.
 */

/**
 * MessageReader implementation that reads JSON-RPC messages from SignalR
 */
export class SignalRMessageReader extends AbstractMessageReader implements MessageReader {
  private readonly hubConnection: HubConnection;
  private callback: DataCallback | undefined;

  constructor(hubConnection: HubConnection) {
    super();
    this.hubConnection = hubConnection;
  }

  listen(callback: DataCallback): { dispose: () => void } {
    this.callback = callback;

    // Listen for messages from the server
    this.hubConnection.on('ReceiveMessage', (message: Message) => {
      if (this.callback) {
        this.callback(message);
      }
    });

    // Handle connection errors
    this.hubConnection.onclose((error?: Error) => {
      if (error) {
        this.fireError(error);
      }
      this.fireClose();
    });

    return {
      dispose: () => {
        this.callback = undefined;
      }
    };
  }
}

/**
 * MessageWriter implementation that writes JSON-RPC messages to SignalR
 */
export class SignalRMessageWriter extends AbstractMessageWriter implements MessageWriter {
  private readonly hubConnection: HubConnection;

  constructor(hubConnection: HubConnection) {
    super();
    this.hubConnection = hubConnection;
  }

  async write(message: Message): Promise<void> {
    try {
      await this.hubConnection.invoke('SendMessage', message);
    } catch (error) {
      this.fireError(error as Error, message, 1);
      throw error;
    }
  }

  end(): void {
    this.hubConnection.stop();
  }
}

/**
 * Creates and starts a SignalR connection for LSP communication
 */
export async function createSignalRConnection(hubUrl: string): Promise<HubConnection> {
  const { HubConnectionBuilder } = await import('@microsoft/signalr');

  const connection = new HubConnectionBuilder()
    .withUrl(hubUrl)
    .withAutomaticReconnect()
    .build();

  if (connection.state === HubConnectionState.Disconnected) {
    await connection.start();
  }

  return connection;
}
