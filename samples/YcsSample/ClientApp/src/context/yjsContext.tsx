import * as React from 'react';
import * as Y from 'yjs';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { YjsSignalrConnector } from '../impl/yjsSignalrConnector';

export interface IYjsContext {
  readonly yDoc: Y.Doc;
  readonly yjsConnector: YjsSignalrConnector;
}

export interface IOptions extends React.PropsWithChildren<{}> {
  readonly baseUrl: string;
}

export const YjsContextProvider: React.FunctionComponent<IOptions> = (props: IOptions) => {
  const { baseUrl } = props;

  const contextProps: IYjsContext = React.useMemo(() => {
    const yDoc = new Y.Doc();

    // Initiate the connection. Doing it here to simplify the example code.
    const connection = new HubConnectionBuilder()
      .withUrl(baseUrl)
      .withAutomaticReconnect()
      .build();
    const connectedPromise = connection.start();
    const yjsConnector = new YjsSignalrConnector(yDoc, connection, connectedPromise);

    return { yDoc, yjsConnector };
  }, [baseUrl]);

  return <YjsContext.Provider value={contextProps}>{props.children}</YjsContext.Provider>;
};

export const YjsContext = React.createContext<IYjsContext | undefined>(undefined);
