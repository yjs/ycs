import React, { Component } from 'react';
import MonacoEditor from 'react-monaco-editor';
import { HubConnectionBuilder, HttpTransportType } from '@microsoft/signalr';
import * as Y from 'yjs';
import { createMutex } from 'lib0/mutex.js';
import { EncodingUtils } from '../util/encodingUtils.js';
import { SyncProtocol } from '../util/syncProtocol.js';

export class Home extends Component {
  static displayName = Home.name;
  static maxClientId = 1000000000;

  _ydoc = null;
  _ytext = null;
  _monacoModel = null;
  _connection = null;
  _mux = null;

  constructor(props) {
    super(props);

    this._mux = createMutex();
    this._ydoc = new Y.Doc();
    // We need to limit the ID to max int for uint decoder to work.
    this._ydoc.clientID = this._ydoc.clientID >= 0 && this._ydoc.client <= Home.maxClientId ? this._ydoc.clientID : Math.floor(Math.random() * Math.floor(Home.maxClientId));
    this._ytext = this._ydoc.getText("monaco");

    this.initConnection();
  }

  initConnection() {
    this._connection = new HubConnectionBuilder()
      .withUrl('https://localhost:5001/hubs/ycs')
      .withAutomaticReconnect()
      .build();

    this._connection
      .start()
      .then(() => {
          this._connection.on('getMissing_result_v2', message => {
            SyncProtocol.readSyncStep2(this._ydoc, message, this);
          });

          this._connection.on('updatev2', message => {
            SyncProtocol.readSyncStep2(this._ydoc, message, this);
          });

          // Always send step1 when connected.
          SyncProtocol.writeSyncStep1(this._ydoc, this._connection);
      })
      .catch(e => console.log('Connection failed: ', e));

    this._ydoc.on('updateV2', (update, origin) => {
      if (origin !== this || origin !== null) {
        var encodedUpdate = EncodingUtils.byteArrayToString(update);
        this._connection.send("updateV2", encodedUpdate);
      }
    });
  }

  editorDidMount(editor, monaco) {
    this._monacoModel = editor.getModel();
    this._monacoModel.setValue(this._ytext.toString());

    // Apply all changes done on the monaco model to the YText instance.
    this._monacoModel.onDidChangeContent(event => {
      this._mux(() => {
        this._ydoc.transact(() => {
          event.changes.sort((change1, change2) => change2.rangeOffset - change1.rangeOffset).forEach(change => {
            this._ytext.delete(change.rangeOffset, change.rangeLength);
            this._ytext.insert(change.rangeOffset, change.text);
          });
        }, this);
      });
    });

    // Apply all remote YText changes on the local monaco model.
    this._ytext.observe(event => {
      this._mux(() => {
        let index = 0

        event.delta.forEach(op => {
          if (op.retain !== undefined) {
            index += op.retain;
          } else if (op.insert !== undefined) {
            const pos = this._monacoModel.getPositionAt(index);
            const range = new monaco.Selection(pos.lineNumber, pos.column, pos.lineNumber, pos.column);
            this._monacoModel.pushEditOperations([], [{ range, text: op.insert }], () => null);
            index += op.insert.length;
          } else if (op.delete !== undefined) {
            const pos = this._monacoModel.getPositionAt(index);
            const endPos = this._monacoModel.getPositionAt(index + op.delete);
            const range = new monaco.Selection(pos.lineNumber, pos.column, endPos.lineNumber, endPos.column);
            this._monacoModel.pushEditOperations([], [{ range, text: '' }], () => null);
          } else {
            throw new Error();
          }
        })

        this._monacoModel.pushStackElement();
      });
    });

    editor.focus();
  }

  render () {
    return (
      <div>
        <MonacoEditor
          width="800"
          height="600"
          language="javascript"
          theme="vs-dark"
          editorDidMount={(e, m) => this.editorDidMount(e, m)}
        />
      </div>
    );
  }
}
