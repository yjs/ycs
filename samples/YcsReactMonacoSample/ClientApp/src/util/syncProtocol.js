import * as Y from 'yjs';
import { EncodingUtils } from './encodingUtils.js';

const GetMissingCommand = 'GetMissing';
const UpdateCommand = 'Update';

export class SyncProtocol {
    _ydoc = null;
    _connection = null;
    _origin = null;

    _connected = false;
    _synced = false;

    _clientClock = null;
    _serverClock = null;
    _receiveQueue = null;

    _resyncInterval = null;

    constructor(ydoc, connection, origin) {
        this._ydoc = ydoc;
        this._connection = connection;
        this._origin = origin;

        this._ydoc.on('updateV2', (update, origin) => this._onYDocUpdate(update, origin));

        this._connection.onreconnected(() => this._handleNewConnection());
        this._connection.onclose(() => this._handleConnectionClosed());

        this._connection.on(GetMissingCommand, message => this._enqueueAndProcessMessages(message, GetMissingCommand));
        this._connection.on(UpdateCommand, message => this._enqueueAndProcessMessages(message, UpdateCommand));

        // Initialize connection.
        this._connection
            .start()
            .then(() => this._handleNewConnection())
            .catch(e => console.error('Connection failed: ', e));
    }

    _onYDocUpdate(updateMessage, origin) {
        if (origin !== this._origin || origin === null) {
            this._sendMessage(UpdateCommand, updateMessage, undefined);
        }
    };

    _requestMissing() {
        if (!this._connected) {
            return;
        }

        const stateVector = Y.encodeStateVectorV2(this._ydoc);
        this._sendMessage(GetMissingCommand, stateVector, undefined);
    }

    _sendMessage(type, data, inReplyTo) {
        if (!this._connected) {
            return;
        }

        const message = {
            clock: ++this._clientClock,
            data: EncodingUtils.byteArrayToString(data),
            inReplyTo
        };

        try {
            this._connection.send(type, JSON.stringify(message));
        } catch (e) {
            this._handleConnectionClosed();
            console.error(e);
            throw e;
        }
    }

    _handleNewConnection() {
        console.info("SignalR connected / reconnected");

        this._connected = true;
        this._synced = false;
        this._receiveQueue = [];
        this._clientClock = -1;
        this._serverClock = -1;
        this._resyncInterval = window.setInterval(() => this._requestMissing(), 15000);

        this._requestMissing();
    }

    _handleConnectionClosed() {
        console.info("SignalR disconnected");

        this._connected = false;
        this._synced = false;
        this._receiveQueue = [];
        this._clientClock = null;
        this._serverClock = null;

        window.clearInterval(this._resyncInterval);
    }

    _enqueueAndProcessMessages(data, type) {
        let parsedMessage;

        try {
            parsedMessage = JSON.parse(data);
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
        const isInitialSyncMessage = (msg) => msg.type === UpdateCommand && msg.inReplyTo === GetMissingCommand;
        if (this._receiveQueue.some(isInitialSyncMessage)) {
            while (!isInitialSyncMessage(this._receiveQueue[0])) {
                this._receiveQueue.shift();
            }

            this._serverClock = this._receiveQueue[0].clock - 1;
        }

        this._ydoc.transact(() => {
            while (this._receiveQueue.length > 0) {
                const msg = this._receiveQueue[0];

                // Check whether the next message is something we can apply now.
                if (msg.clock !== this._serverClock + 1) {
                    break;
                }

                try {
                    switch (msg.type) {
                        case GetMissingCommand:
                            {
                                // Reply with SyncStep2 on SyncStep1.
                                const targetStateVector = EncodingUtils.stringToByteArray(msg.data);
                                const update = Y.encodeStateAsUpdateV2(this._ydoc, targetStateVector);
                                this._sendMessage(UpdateCommand, update, GetMissingCommand);
                            }
                            break;

                        case UpdateCommand:
                            {
                                // Skip all updates received until the missing blocks are applied.
                                if (this._synced || msg.inReplyTo === GetMissingCommand) {
                                    const update = EncodingUtils.stringToByteArray(msg.data);
                                    Y.applyUpdateV2(this._ydoc, update, this._origin);

                                    if (msg.inReplyTo === GetMissingCommand) {
                                        this._synced = true;
                                    }
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
        }, this._origin);
    }
}

export default SyncProtocol;
