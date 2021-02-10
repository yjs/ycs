Ycs
-------

A compatible `.Net` implementation of the [Yjs](https://github.com/yjs/yjs) CRDT framework.

With this, you can host CRDTs in your `.Net` application and synchronize them with the existing `Yjs` models running elsewhere.

#### Latest tested Yjs version: [3.4.14](https://github.com/yjs/yjs/releases/tag/v13.4.14).

Supports [Y.Array, Y.Map, Y.Text](https://github.com/yjs/yjs#shared-types), but does not yet support `Y.Xml` types.

Demo
-------

Client: [`Yjs`](https://github.com/yjs/yjs), [`MonacoEditor`](https://github.com/microsoft/monaco-editor), [`SignalR`](https://github.com/dotnet/aspnetcore/tree/master/src/SignalR).

Server: `Ycs`, `SignalR`, [`AspNetCore`](https://github.com/dotnet/aspnetcore).

![img](https://github.com/yjs/ycs/blob/main/docs/ycs.gif)
