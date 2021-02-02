A compatible .Net implementation of the [Yjs](https://github.com/yjs/yjs) CRDT framework.
With this, you can host CRDTs in your .Net application and sycnhronize them with the existing Yjs models running elsewhere.

Supports [Y.Array, Y.Map, Y.Text](https://github.com/yjs/yjs#shared-types), but does not support Y.Xml types. Feel free to submit a PR!

Latest tested Yjs version: [3.4.12](https://github.com/yjs/yjs/releases/tag/v13.4.12).

Short demo
-------

Client: [Yjs](https://github.com/yjs/yjs), [Monaco editor](https://github.com/microsoft/monaco-editor), [SignalR](https://github.com/dotnet/aspnetcore/tree/master/src/SignalR).
Server: Ycs, [AspNetCore](https://github.com/dotnet/aspnetcore), [SignalR](https://github.com/dotnet/aspnetcore/tree/master/src/SignalR).

![img](https://github.com/yjs/ycs/blob/main/docs/ycs.gif)

Known issues
-------
* There is an issue serializing Yjs IDs from Number to System.Int32. If the YDoc.clientId is larger than 'int.MaxValue', it's deserialized as a negative integer and corrupt the state.
As a workaround, you may want to overwrite the 'clientID' property after you create the YDoc: 'this.yDoc.clientID = Math.floor(Math.random() * Math.floor(1000000000);'
