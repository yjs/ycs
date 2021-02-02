import React, { Component } from 'react';
import MonacoEditor from 'react-monaco-editor';
import * as monaco from 'monaco-editor/esm/vs/editor/editor.api';
import { HubConnectionBuilder, HttpTransportType } from '@microsoft/signalr';
import * as Y from 'yjs';
import { createMutex } from 'lib0/mutex.js';
import { SyncProtocol } from '../util/syncProtocol.js';

const createRelativeSelection = (editor, monacoModel, type) => {
  const sel = editor.getSelection();
  if (sel !== null) {
    const startPos = sel.getStartPosition();
    const endPos = sel.getEndPosition();
    const start = Y.createRelativePositionFromTypeIndex(type, monacoModel.getOffsetAt(startPos));
    const end = Y.createRelativePositionFromTypeIndex(type, monacoModel.getOffsetAt(endPos));
    return { start, end, direction: sel.getDirection() };
  }
  return null;
}

const createMonacoSelectionFromRelativeSelection = (editor, type, relSel, doc) => {
  const start = Y.createAbsolutePositionFromRelativePosition(relSel.start, doc);
  const end = Y.createAbsolutePositionFromRelativePosition(relSel.end, doc);
  if (start !== null && end !== null && start.type === type && end.type === type) {
    const model = editor.getModel();
    const startPos = model.getPositionAt(start.index);
    const endPos = model.getPositionAt(end.index);
    return monaco.Selection.createWithDirection(startPos.lineNumber, startPos.column, endPos.lineNumber, endPos.column, relSel.direction);
  }
  return null;
}

export class Home extends Component {
  maxClientId = 1000000000;

  _ydoc = null;
  _ytext = null;
  _monacoEditor = null;
  _monacoModel = null;
  _connection = null;
  _mux = null;
  _protocol = null;
  _selection = null;

  constructor(props) {
    super(props);

    this._mux = createMutex();
    this._ydoc = new Y.Doc();

    // We need to limit the ID to max int for uint decoder to work.
    this._ydoc.clientID = Math.floor(Math.random() * Math.floor(this.maxClientId));
    this._ytext = this._ydoc.getText("monaco");

    this._ydoc.on('beforeAllTransactions', () => this._onDocBeforeTransactions());

    this.initConnection();
  }

  initConnection() {
    this._connection = new HubConnectionBuilder()
      .withUrl('https://localhost:5001/hubs/ycs')
      .withAutomaticReconnect()
      .build();

    this._protocol = new SyncProtocol(this._ydoc, this._connection, this);
  }

  editorDidMount(editor, monaco) {
    this._monacoEditor = editor;
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
        }, this._monacoModel);
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

        if (this._selection) {
          let sel = createMonacoSelectionFromRelativeSelection(this._monacoEditor, this._ytext, this._selection, this._ydoc);
          if (sel !== null) {
            this._monacoEditor.setSelection(sel);
          }
        }
      });
    });

    editor.focus();
  }

  _onDocBeforeTransactions() {
    this._mux(() => {
      if (!this._monacoEditor || !this._monacoModel || !this._ytext) {
        return;
      }

      const rsel = createRelativeSelection(this._monacoEditor, this._monacoModel, this._ytext);
      if (rsel !== null) {
        this._selection = rsel;
      } else {
        this._selection = null;
      }
    });
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
