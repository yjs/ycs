import * as Y from 'yjs';
import { createMutex } from 'lib0/mutex.js';
import { EncodingUtils } from './encodingUtils.js';

export class SyncProtocol {
    _ydoc = null;
    _connection = null;
    _origin = null;
    _mux = null;
    _resyncInterval = null;
    _connected = false;
    _lastSentMsgId = null;
    _lastProcessedMsgId = null;
    _receiveQueue = null;

    constructor(ydoc, connection, origin) {
        this._ydoc = ydoc;
        this._connection = connection;
        this._origin = origin;
        this._mux = createMutex();

        this._connection.onreconnected(() => this._handleNewConnection());

        this._connection.onclose(() => {
            this._mux(() => {
                this._connected = false;
                this._receiveQueue = [];
                this._lastSentMsgId = null;
                this._lastProcessedMsgId = null;

                clearInterval(this._resyncInterval);
            });
        });

        this._connection.on('getMissing_v2', message => {
            this._mux(() => {
                if (this._connected) {
                    this._readUpdateV2(message);
                }
            });
        });

        this._connection.on('update_v2', message => {
            this._mux(() => {
                if (this._connected) {
                    this._readUpdateV2(message);
                }
            });
        });

        this._ydoc.on('updateV2', (update, origin) => {
            this._mux(() => {
                if (origin !== this._origin || origin === null) {
                    this.sendUpdateV2(update);
                }
            });
        });

        // Initialize connection.
        this._connection
            .start()
            .then(() => setTimeout(() => this._handleNewConnection(), 10000))
            .catch(e => console.log('Connection failed: ', e));
    }

    sendMissing() {
        const update = Y.encodeStateAsUpdateV2(this._ydoc);
        this.sendUpdateV2(update);
    }

    requestMissing() {
        const sv = Y.encodeStateVectorV2(this._ydoc);
        const encodedSv = EncodingUtils.byteArrayToString(sv);
        this._sendMessage('getMissing', encodedSv);
    }

    sendUpdateV2(update) {
        const encodedUpdate = EncodingUtils.byteArrayToString(update);
        this._sendMessage('updatev2', encodedUpdate);
    }

    _readUpdateV2(data) {
        const msg = JSON.parse(data);
        this._receiveQueue.push(msg);

        // Remove messages that we should've processed already.
        this._receiveQueue = this._receiveQueue.filter(msg => msg.seq > this._lastProcessedMsgId);
        // Sort queue by sequence number.
        this._receiveQueue.sort((a, b) => a.seq - b.seq);

        this._ydoc.transact(tr => {
            while (this._receiveQueue.length > 0) {
                const msg = this._receiveQueue[0];

                // Check whether the next message is something we can apply now.
                if (msg.seq !== this._lastProcessedMsgId + 1) {
                    break;
                }

                // Try to apply update.
                const update = EncodingUtils.stringToByteArray(msg.data);
                Y.applyUpdateV2(this._ydoc, update, this._origin);

                // Remove the message from the queue.
                this._receiveQueue.shift();
                this._lastProcessedMsgId++;
            }
        }, this._origin);
    }

    _sendMessage(type, data) {
        this._lastSentMsgId++;

        const msg = {
            seq: this._lastSentMsgId,
            data: data
        };

        this._connection.send(type, JSON.stringify(msg));
    }

    _handleNewConnection() {
        this._mux(() => {
            console.log("connected / reconnected");
            this._connected = true;
            this._receiveQueue = [];
            this._lastSentMsgId = -1;
            this._lastProcessedMsgId = -1;

            this.requestMissing();
            this.sendMissing();

            this._resyncInterval = setInterval(() => {
                this._mux(() => {
                    if (this._connected) {
                        this._receiveQueue = [];
                        this.requestMissing();
                    }
                });
            }, 10000);
        });
    }
}

export default SyncProtocol;
