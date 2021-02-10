import * as React from 'react';
import * as yMonaco from 'y-monaco';
import MonacoEditor, { monaco } from 'react-monaco-editor';
import { useYjs } from '../hooks/useYjs';

export const YjsMonacoEditor = () => {
  const { yDoc, yjsConnector } = useYjs();
  const yText = yDoc.getText('monaco');

  const setMonacoEditor = React.useState<monaco.editor.ICodeEditor>()[1];
  const setMonacoBinding = React.useState<monaco.editor.ICodeEditor>()[1];

  const _onEditorDidMount = React.useCallback(
    (editor: monaco.editor.ICodeEditor, monacoParam: typeof monaco): void => {
      editor.focus();
      editor.setValue('');

      setMonacoEditor(editor);
      setMonacoBinding(new yMonaco.MonacoBinding(yText, editor.getModel(), new Set([editor]), yjsConnector.awareness));
    },
    [yjsConnector.awareness, yText, setMonacoEditor, setMonacoBinding]
  );

  return (
    <div>
      <MonacoEditor
        width='100%'
        height='600px'
        theme='vs'
        options={{
          automaticLayout: true
        }}
        editorDidMount={(e, a) => _onEditorDidMount(e, a)}
      />
    </div>
  );
};
