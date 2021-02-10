using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Ycs;
using YcsSample.Middleware;

namespace YcsSample.Yjs
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum YjsCommandType
    {
        GetMissing,
        Update
    }

    public class MessageToProcess
    {
        public YjsCommandType Command;
        public YjsCommandType? InReplyTo;
        public string Data;
    }

    public class YcsManager
    {
        private class ClientContext
        {
            private long _synced = 0;
            private long _serverClock = -1;
            private long _clientClock = -1;

            public bool Synced
            {
                get => _synced != 0;
                set => Interlocked.Exchange(ref _synced, value ? 1 : 0);
            }

            public long ServerClock => _serverClock;
            public long ClientClock => _clientClock;
            public SortedList<long, MessageToProcess> Messages { get; } = new SortedList<long, MessageToProcess>();

            public long IncrementAndGetServerClock() => Interlocked.Increment(ref _serverClock);
            public long IncrementAndGetClientClock() => Interlocked.Increment(ref _clientClock);
            public void ReassignClientClock(long clock) => Interlocked.Exchange(ref _clientClock, clock);
        }

        private class MessageToClient
        {
            [JsonPropertyName("clock")]
            public long Clock { get; set; }

            [JsonPropertyName("data")]
            public string Data { get; set; }

            [JsonPropertyName("inReplyTo")]
            public YjsCommandType? InReplyTo { get; set; }
        }

        private static readonly Lazy<YcsManager> _instance = new Lazy<YcsManager>(() => new YcsManager());

        private readonly IDictionary<string, ClientContext> _clients = new ConcurrentDictionary<string, ClientContext>();
        private readonly object _syncRoot = new object();

        private YcsManager()
        {
            YDoc = new YDoc();

            // Prepopulate document with the data.
            YDoc.GetText("monaco").Insert(0, "Hello, world!");

            YDoc.UpdateV2 += (sender, e) =>
            {
                if (e.data == null || e.data.Length == 0)
                {
                    return;
                }

                var encodedUpdate = EncodeBytes(e.data);

                // Send update to all connected clients.
                foreach (var clientId in _clients.Keys.ToList())
                {
                    // Don't send update messages to the clients that are not yet synced.
                    if (_clients.TryGetValue(clientId, out var context) && context.Synced)
                    {
                        var msg = new MessageToClient
                        {
                            Clock = context.IncrementAndGetServerClock(),
                            Data = encodedUpdate
                        };

                        YcsHubAccessor.Instance.YcsHub.Clients.Client(clientId)
                            .SendAsync(YjsCommandType.Update.ToString(), JsonSerializer.Serialize(msg))
                            .ContinueWith((t) => { /* Ignore SendAsync() exceptions. */ });
                    }
                }
            };
        }

        public static YcsManager Instance => _instance.Value;

        public YDoc YDoc { get; }

        public void HandleClientConnected(string connectionId) => _clients[connectionId] = new ClientContext();

        public void HandleClientDisconnected(string connectionId) => _clients.Remove(connectionId);

        public async Task EnqueueAndProcessMessagesAsync(string connectionId, long clock, MessageToProcess messageToEnqueue, CancellationToken cancellationToken = default)
        {
            var context = _clients[connectionId];
            context.Messages.Add(clock, messageToEnqueue);

            // We can fast-forward client clock if we're 'stuck' and/or have pending
            // GetMissing (SyncStep1) messages - they indicate the request for the initial/periodic
            // sync that will eventually make previous updates no-op.
            Func<MessageToProcess, bool> isInitialSyncMessage = msg => msg.Command == YjsCommandType.GetMissing;
            if (context.Messages.Values.Any(isInitialSyncMessage))
            {
                while (!isInitialSyncMessage(context.Messages[context.Messages.Keys[0]]))
                {
                    context.Messages.Remove(context.Messages.Keys[0]);
                }

                context.ReassignClientClock(context.Messages.Keys[0] - 1);
            }

            foreach (var key in context.Messages.Keys.ToList())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Check for potential duplicates (unlikely to happen).
                if (key == context.ClientClock)
                {
                    continue;
                }

                // Skip if we haven't received the next message yet.
                if (key != context.ClientClock + 1)
                {
                    break;
                }

                var message = context.Messages[key];

                switch (message.Command)
                {
                    case YjsCommandType.GetMissing:
                        {
                            byte[] decodedStateVector = DecodeString(message.Data);

                            // Reply with SyncStep2 immediately followed by the SyncStep1.
                            MessageToClient syncStep1Message;
                            MessageToClient syncStep2Message;

                            lock (_syncRoot)
                            {
                                var update = YDoc.EncodeStateAsUpdateV2(decodedStateVector);
                                var stateVector = YDoc.EncodeStateVectorV2();

                                // IMPORTANT: The order of constructors matter as they call the 'context.IncrementAndGetServerClock()'.
                                syncStep2Message = new MessageToClient
                                {
                                    Clock = context.IncrementAndGetServerClock(),
                                    Data = EncodeBytes(update),
                                    InReplyTo = YjsCommandType.GetMissing
                                };

                                syncStep1Message = new MessageToClient
                                {
                                    Clock = context.IncrementAndGetServerClock(),
                                    Data = EncodeBytes(stateVector),
                                    InReplyTo = YjsCommandType.GetMissing
                                };
                            }

                            await YcsHubAccessor.Instance.YcsHub.Clients.Client(connectionId)
                                .SendAsync(YjsCommandType.Update.ToString(), JsonSerializer.Serialize(syncStep2Message), cancellationToken);

                            await YcsHubAccessor.Instance.YcsHub.Clients.Client(connectionId)
                                .SendAsync(YjsCommandType.GetMissing.ToString(), JsonSerializer.Serialize(syncStep1Message), cancellationToken);
                        }
                        break;

                    case YjsCommandType.Update:
                        {
                            // Ignore all updates until the client is synced.
                            if (context.Synced || message.InReplyTo == YjsCommandType.GetMissing)
                            {
                                var update = DecodeString(message.Data);

                                lock (_syncRoot)
                                {
                                    // YDoc's update handler will broadcast update to all affected clients.
                                    YDoc.ApplyUpdateV2(update, this);
                                }

                                if (message.InReplyTo == YjsCommandType.GetMissing)
                                {
                                    context.Synced = true;
                                }
                            }
                        }
                        break;

                    default:
                        throw new NotSupportedException();
                }

                // We've processed the current message, remove it from the queue.
                context.IncrementAndGetClientClock();
                context.Messages.Remove(key);
            }
        }

        private static string EncodeBytes(byte[] arr) => Convert.ToBase64String(arr);
        private static byte[] DecodeString(string str) => Convert.FromBase64String(str);
    }
}
