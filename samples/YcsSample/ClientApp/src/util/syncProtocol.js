import * as Y from 'yjs';
import { EncodingUtils } from './encodingUtils.js';

export class SyncProtocol {
    static writeSyncStep1(ydoc, connection) {
        const sv = Y.encodeStateVectorV2(ydoc);
        const encodedSv = EncodingUtils.byteArrayToString(sv);
        connection.send("getMissing", encodedSv);
    }

    static readSyncStep2(ydoc, data, origin) {
        const update = EncodingUtils.stringToByteArray(data);
        Y.applyUpdateV2(ydoc, update, origin);
    }
}

export default SyncProtocol;
