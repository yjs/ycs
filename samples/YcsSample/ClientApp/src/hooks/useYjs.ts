import React from 'react';
import * as Y from 'yjs';
import { YjsContext } from '../context/yjsContext';
import { YjsSignalrConnector } from '../impl/yjsSignalrConnector';

export function useYjs(): { yDoc: Y.Doc, yjsConnector: YjsSignalrConnector } {
  const yjsContext = React.useContext(YjsContext);
  if (yjsContext === undefined) {
    throw new Error('useYjs() should be called with the YjsContext defined.');
  }

  return {
    yDoc: yjsContext.yDoc,
    yjsConnector: yjsContext.yjsConnector
  };
}
