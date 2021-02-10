import * as Y from 'yjs';
import * as awarenessProtocol from 'y-protocols/awareness.js';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { byteArrayToString, stringToByteArray } from '../util/encodingUtils';

interface YjsMessage {
  readonly clock: number;
  readonly data: string;
  readonly inReplyTo?: string;
}

interface YjsPendingReceivedMessage {
  readonly clock: number;
  readonly data: string;
  readonly type: YjsMessageType;
  readonly inReplyTo?: string;
}

enum YjsMessageType {
  GetMissing,        // SyncStep1
  Update,            // SyncStep2
  QueryAwareness,    // Other clients will broadcast their awareness info.
  UpdateAwareness    // Broadcast awareness info.
}

export class YjsSignalrConnector {
  private _doc: Y.Doc;
  private _client: HubConnection;
  private _awareness: awarenessProtocol.Awareness;

  private _synced: boolean = false;

  private _receiveQueue: YjsPendingReceivedMessage[] = [];
  private _clientClock: number = -1;
  private _serverClock: number = -1;
  private _resyncInterval?: number = undefined;

  constructor(doc: Y.Doc, client: HubConnection, connectedPromise: Promise<void>) {
    this._doc = doc;
    this._client = client;

    this._awareness = new awarenessProtocol.Awareness(this._doc);
    this._awareness.on('update', this._awarenessUpdateHandler);

    this._doc.on('updateV2', this._yDocUpdateV2Handler);

    this._client.onreconnected(() => this._resetConnectionAsync());
    this._client.onclose(() => this._resetConnectionAsync());

    this._client.on(YjsMessageType[YjsMessageType.GetMissing], this._onGetMissingReceived);
    this._client.on(YjsMessageType[YjsMessageType.Update], this._onUpdateReceived);
    this._client.on(YjsMessageType[YjsMessageType.QueryAwareness], this._onQueryAwareness);
    this._client.on(YjsMessageType[YjsMessageType.UpdateAwareness], this._onUpdateAwareness);

    // Resync every 10 seconds.
    this._resyncInterval = window.setInterval(async () => {
      await this._requestMissingAsync();
      await this._requestAndBroadcastAwareness();
    }, 10000);

    // Start initial sync as soon as the SignalR connection is established.
    connectedPromise.then(
      () => this._resetConnectionAsync(),
      reason => console.error('Unable to connect to the SignalR hub', reason));
  }

  public get awareness(): awarenessProtocol.Awareness {
    return this._awareness;
  }

  private get connected(): boolean {
    return this._client.state === HubConnectionState.Connected;
  }

  public destroy(): void {
    this._doc.off('updateV2', this._yDocUpdateV2Handler);

    this._client.off(YjsMessageType[YjsMessageType.GetMissing], this._onGetMissingReceived);
    this._client.off(YjsMessageType[YjsMessageType.Update], this._onUpdateReceived);
    this._client.off(YjsMessageType[YjsMessageType.QueryAwareness], this._onQueryAwareness);
    this._client.off(YjsMessageType[YjsMessageType.UpdateAwareness], this._onUpdateAwareness);

    awarenessProtocol.removeAwarenessStates(this._awareness, [this._doc.clientID], this);

    if (this._resyncInterval) {
      window.clearInterval(this._resyncInterval);
      this._resyncInterval = undefined;
    }
  }

  private _yDocUpdateV2Handler = (updateMessage: Uint8Array, origin: object | undefined): void => {
    if (origin !== this || origin === null) {
      this._sendMessageAsync(YjsMessageType.Update, updateMessage, undefined);
    }
  };

  private _awarenessUpdateHandler = ({ added, updated, removed }): void => {
    const changedClients = added.concat(updated).concat(removed);
    const update = awarenessProtocol.encodeAwarenessUpdate(this._awareness, changedClients);
    this._sendMessageAsync(YjsMessageType.UpdateAwareness, update, undefined);
  };

  // SyncStep1
  private _onGetMissingReceived = (data: string): void => {
    if (data) {
      this._enqueueAndProcessMessages(data, YjsMessageType.GetMissing);
    }
  };

  // SyncStep2
  private _onUpdateReceived = (data: string): void => {
    if (data) {
      this._enqueueAndProcessMessages(data, YjsMessageType.Update);
    }
  };

  private _onQueryAwareness = (): void => {
    const update = awarenessProtocol.encodeAwarenessUpdate(this._awareness, Array.from(this._awareness.getStates().keys()));
    this._sendMessageAsync(YjsMessageType.UpdateAwareness, update, undefined);
  };

  private _onUpdateAwareness = (data: string): void => {
    if (data) {
      const update = stringToByteArray(data);
      awarenessProtocol.applyAwarenessUpdate(this._awareness, update, this);
    }
  };

  private async _resetConnectionAsync(): Promise<void> {
    this._synced = false;
    this._clientClock = -1;
    this._serverClock = -1;
    this._receiveQueue = [];

    if (this.connected) {
      await this._requestMissingAsync();
      await this._requestAndBroadcastAwareness();
    } else {
      // Update awareness (all users except local left).
      awarenessProtocol.removeAwarenessStates(
        this._awareness,
        Array.from(this._awareness.getStates().keys()).filter(client => client !== this._doc.clientID),
        this);
    }
  }

  private async _requestMissingAsync(): Promise<void> {
    if (!this.connected) {
      return;
    }

    const stateVector = Y.encodeStateVectorV2(this._doc);
    await this._sendMessageAsync(YjsMessageType.GetMissing, stateVector, undefined);
  }

  private async _requestAndBroadcastAwareness(): Promise<void> {
    if (!this.connected) {
      return;
    }

    await this._sendMessageAsync(YjsMessageType.QueryAwareness, undefined, undefined);

    if (this._awareness.getLocalState() !== null) {
      const update = awarenessProtocol.encodeAwarenessUpdate(this._awareness, [this._doc.clientID]);
      await this._sendMessageAsync(YjsMessageType.UpdateAwareness, update, undefined);
    }
  }

  private async _sendMessageAsync(
    type: YjsMessageType,
    data: Uint8Array | undefined,
    inReplyTo: YjsMessageType | undefined
  ): Promise<void> {
    if (!this.connected) {
      return;
    }

    if (data === undefined) {
      data = new Uint8Array();
    }

    switch (type) {
      case YjsMessageType.GetMissing:
      case YjsMessageType.Update:
        const message: YjsMessage = {
          clock: ++this._clientClock,
          data: byteArrayToString(data),
          inReplyTo: inReplyTo !== undefined ? YjsMessageType[inReplyTo] : undefined
        };

        await this._client.send(YjsMessageType[type], JSON.stringify(message));
        break;
      case YjsMessageType.QueryAwareness:
      case YjsMessageType.UpdateAwareness:
        // Awareness has its own internal clock, no need to duplicate it in the message.
        await this._client.send(YjsMessageType[type], byteArrayToString(data));
        break;
      default:
        throw new Error(`Unknown message type: ${type}`);
    }
  }

  private _enqueueAndProcessMessages(data: string, type: YjsMessageType): void {
    let parsedMessage: YjsMessage;

    try {
      parsedMessage = JSON.parse(data) as YjsMessage;
    } catch {
      // Invalid input.
      return;
    }

    // Invalid data.
    if (!parsedMessage || parsedMessage.clock === undefined || !parsedMessage.data) {
      return;
    }

    this._receiveQueue.push({
      clock: parsedMessage.clock,
      data: parsedMessage.data,
      inReplyTo: parsedMessage.inReplyTo,
      type
    });

    // Remove messages that we should've processed already.
    this._receiveQueue = this._receiveQueue.filter(msg => msg.clock > this._serverClock);

    // Sort queue by sequence number.
    this._receiveQueue.sort((a, b) => a.clock - b.clock);

    // We can fast-forward server clock if we're 'stuck' and/or have pending
    // UpdateV2 (SyncStep2) messages - they indicate the reply on the initial/periodic
    // sync that will eventually make previous updates no-op.
    const isInitialSyncMessage = (msg: YjsPendingReceivedMessage) =>
      msg.type === YjsMessageType.Update && msg.inReplyTo === YjsMessageType[YjsMessageType.GetMissing];
    if (this._receiveQueue.some(isInitialSyncMessage)) {
      while (!isInitialSyncMessage(this._receiveQueue[0])) {
        this._receiveQueue.shift();
      }
      this._serverClock = this._receiveQueue[0].clock - 1;
    }

    this._doc.transact(() => {
      while (this._receiveQueue.length > 0) {
        const msg = this._receiveQueue[0];

        // Check for potential duplicates (unlikely to happen).
        if (msg.clock === this._serverClock) {
          continue;
        }

        // Check whether the next message is something we can apply now.
        if (msg.clock !== this._serverClock + 1) {
          break;
        }

        try {
          switch (msg.type) {
            // SyncStep1
            case YjsMessageType.GetMissing:
              // Reply with SyncStep2 on SyncStep1.
              const targetStateVector = stringToByteArray(msg.data);
              const update = Y.encodeStateAsUpdateV2(this._doc, targetStateVector);
              this._sendMessageAsync(YjsMessageType.Update, update, YjsMessageType.GetMissing);
              break;
            // SyncStep2
            case YjsMessageType.Update:
              // Skip all updates received until the missing blocks are applied.
              if (this._synced || msg.inReplyTo === YjsMessageType[YjsMessageType.GetMissing]) {
                const update = stringToByteArray(msg.data);
                Y.applyUpdateV2(this._doc, update, this);

                if (msg.inReplyTo === YjsMessageType[YjsMessageType.GetMissing]) {
                  this._synced = true;
                }
              }
              break;
            default:
              throw new Error(`Unsupported Yjs message type: ${msg.type}`);
          }
        } catch (e) {
          console.error(e);
          throw e;
        } finally {
          // Remove the message from the queue.
          this._receiveQueue.shift();
          this._serverClock++;
        }
      }
    }, this);
  }
}
