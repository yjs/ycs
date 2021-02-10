import './styles/index.css';
import 'bootstrap/dist/css/bootstrap.min.css';
import React from 'react';
import ReactDOM from 'react-dom';
import { BrowserRouter } from 'react-router-dom';
import { App } from './app';
import { YjsContextProvider } from './context/yjsContext';
import reportWebVitals from './util/reportWebVitals';

const baseUrl = document.getElementsByTagName('base')[0].getAttribute('href') ?? undefined;
const rootElement = document.getElementById('root');

ReactDOM.render(
  <YjsContextProvider baseUrl={'https://localhost:5001/hubs/ycs'}>
    <React.StrictMode>
      <BrowserRouter basename={baseUrl}>
        <App />
      </BrowserRouter>
    </React.StrictMode>
  </YjsContextProvider>,
  rootElement
);

// If you want to start measuring performance in your app, pass a function
// to log results (for example: reportWebVitals(console.log))
// or send to an analytics endpoint. Learn more: https://bit.ly/CRA-vitals
reportWebVitals();
