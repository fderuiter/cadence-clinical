import * as signalR from "@microsoft/signalr";

export interface AwarenessState {
  userId?: string;
  userName: string;
  userRole: string;
  color: string;
  cursorX?: number | null;
  cursorY?: number | null;
  activeSectionId?: string | null;
  selectedFieldId?: string | null;
  timestamp?: number;
}

export interface SnapshotSaveResult {
  formId: string;
  savedAt: string;
  success: boolean;
  compilationResult?: any;
  compilationError?: string;
}

export type ConnectionState = "disconnected" | "connecting" | "connected" | "reconnecting";

export class EcrfCollaborationService {
  private connection: signalR.HubConnection | null = null;
  private currentFormId: string | null = null;
  private currentStudyId: string | null = null;
  public connectionState: ConnectionState = "disconnected";

  // Event callbacks
  public onUpdateReceived?: (update: Uint8Array) => void;
  public onStateChunksReceived?: (chunks: Uint8Array[]) => void;
  public onAwarenessReceived?: (userId: string, state: AwarenessState) => void;
  public onActiveUsersReceived?: (users: AwarenessState[]) => void;
  public onUserLeft?: (connectionId: string) => void;
  public onSnapshotSaved?: (result: SnapshotSaveResult) => void;
  public onConnectionStateChanged?: (state: ConnectionState) => void;

  // Offline queued updates
  private offlineUpdates: Uint8Array[] = [];

  constructor(private hubUrl: string = "/hubs/ecrf-collaboration") {}

  public async connect(): Promise<void> {
    if (this.connection && this.connection.state !== signalR.HubConnectionState.Disconnected) {
      return;
    }

    this.setConnectionState("connecting");

    let resolvedUrl = this.hubUrl;
    if (resolvedUrl.startsWith("/")) {
      const origin =
        typeof window !== "undefined" && window.location && window.location.origin && window.location.origin !== "null"
          ? window.location.origin
          : "http://localhost";
      resolvedUrl = `${origin}${resolvedUrl}`;
    }

    try {
      this.connection = new signalR.HubConnectionBuilder()
        .withUrl(resolvedUrl)
        .withAutomaticReconnect({
          nextRetryDelayInMilliseconds: (retryContext) => {
            if (retryContext.previousRetryCount >= 10) return null;
            return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 10000);
          },
        })
        .configureLogging(signalR.LogLevel.Warning)
        .build();

      this.connection.on("ReceiveUpdate", (updateVector: number[] | Uint8Array) => {
        const bytes = updateVector instanceof Uint8Array ? updateVector : new Uint8Array(updateVector);
        if (this.onUpdateReceived) {
          this.onUpdateReceived(bytes);
        }
      });

      this.connection.on("ReceiveStateChunks", (chunks: (number[] | Uint8Array)[]) => {
        const uint8Chunks = chunks.map((chunk) =>
          chunk instanceof Uint8Array ? chunk : new Uint8Array(chunk)
        );
        if (this.onStateChunksReceived) {
          this.onStateChunksReceived(uint8Chunks);
        }
      });

      this.connection.on("ReceiveAwareness", (userId: string, state: AwarenessState) => {
        if (this.onAwarenessReceived) {
          this.onAwarenessReceived(userId, state);
        }
      });

      this.connection.on("ReceiveActiveUsers", (users: AwarenessState[]) => {
        if (this.onActiveUsersReceived) {
          this.onActiveUsersReceived(users);
        }
      });

      this.connection.on("UserLeft", (connectionId: string) => {
        if (this.onUserLeft) {
          this.onUserLeft(connectionId);
        }
      });

      this.connection.on("SnapshotSaved", (result: SnapshotSaveResult) => {
        if (this.onSnapshotSaved) {
          this.onSnapshotSaved(result);
        }
      });

      this.connection.onreconnecting(() => {
        this.setConnectionState("reconnecting");
      });

      this.connection.onreconnected(async () => {
        this.setConnectionState("connected");
        if (this.currentFormId) {
          await this.connection?.invoke("RequestSync", this.currentFormId);
          await this.flushOfflineUpdates();
        }
      });

      this.connection.onclose(() => {
        this.setConnectionState("disconnected");
      });

      await this.connection.start();
      this.setConnectionState("connected");
      if (this.currentFormId) {
        await this.joinSession(this.currentFormId, this.currentStudyId);
      }
    } catch (err) {
      // Gracefully run in offline local CRDT mode when backend SignalR server is not running
      this.setConnectionState("disconnected");
    }
  }

  public async joinSession(formId: string, studyId?: string | null): Promise<void> {
    this.currentFormId = formId;
    this.currentStudyId = studyId || null;

    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
      try {
        await this.connection.invoke("JoinSession", formId, studyId);
        await this.flushOfflineUpdates();
      } catch (err) {
        // Ignore join errors when offline
      }
    }
  }

  public async leaveSession(): Promise<void> {
    if (this.currentFormId && this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
      try {
        await this.connection.invoke("LeaveSession", this.currentFormId);
      } catch (err) {
        // Ignore leave errors
      }
    }
    this.currentFormId = null;
  }

  public async sendUpdate(update: Uint8Array): Promise<void> {
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected && this.currentFormId) {
      try {
        await this.connection.invoke("SendUpdate", this.currentFormId, Array.from(update));
      } catch (err) {
        this.offlineUpdates.push(update);
      }
    } else {
      this.offlineUpdates.push(update);
    }
  }

  public async sendAwareness(awareness: AwarenessState): Promise<void> {
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected && this.currentFormId) {
      try {
        await this.connection.invoke("SendAwareness", this.currentFormId, awareness);
      } catch (err) {
        // Silently swallow awareness send errors when offline
      }
    }
  }

  public async saveSnapshot(usdmJson: string): Promise<void> {
    if (this.connection && this.connection.state === signalR.HubConnectionState.Connected && this.currentFormId) {
      try {
        await this.connection.invoke("SaveSnapshot", this.currentFormId, usdmJson);
      } catch (err) {
        // Silently swallow snapshot save errors when offline
      }
    }
  }

  public async disconnect(): Promise<void> {
    if (this.connection) {
      try {
        await this.leaveSession();
        await this.connection.stop();
      } catch (err) {
        // Ignore disconnect errors
      }
      this.connection = null;
    }
    this.setConnectionState("disconnected");
  }

  private setConnectionState(state: ConnectionState): void {
    this.connectionState = state;
    if (this.onConnectionStateChanged) {
      this.onConnectionStateChanged(state);
    }
  }

  private async flushOfflineUpdates(): Promise<void> {
    if (!this.currentFormId || this.offlineUpdates.length === 0) return;
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) return;

    const updatesToFlush = [...this.offlineUpdates];
    this.offlineUpdates = [];

    for (const update of updatesToFlush) {
      try {
        await this.connection.invoke("SendUpdate", this.currentFormId, Array.from(update));
      } catch (err) {
        this.offlineUpdates.push(update);
      }
    }
  }
}
