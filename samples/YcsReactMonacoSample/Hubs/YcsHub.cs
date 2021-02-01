using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Ycs;

namespace YcsSample.Hubs
{
    public class YcsHub : Hub
    {
        private class ClientContext
        {
            public int ServerMessageId = -1;
            public int LastClientMessageId = -1;
            public readonly IDictionary<int, StoredMessage> Messages = new SortedList<int, StoredMessage>();
        }

        private class YcsMessage
        {
            [JsonPropertyName("seq")]
            public int Seq { get; set; }

            [JsonPropertyName("data")]
            public string Data { get; set; }
        }

        private enum MessageType
        {
            GetMissing,
            Update
        }

        private class StoredMessage
        {
            public MessageType Type;
            public string Data;
        }

        private class MessageToClient
        {
            [JsonPropertyName("seq")]
            public int Seq { get; set; }

            [JsonPropertyName("data")]
            public string Data { get; set; }
        }

        private static readonly YDoc _doc = new YDoc();
        private static readonly object _syncRoot = new object();
        private static readonly IDictionary<string, ClientContext> _clients = new ConcurrentDictionary<string, ClientContext>();

        public override async Task OnConnectedAsync()
        {
            if (!_clients.TryGetValue(Context.ConnectionId, out var context))
            {
                context = new ClientContext();
                _clients[Context.ConnectionId] = context;
            }

            context.LastClientMessageId = -1;
            context.ServerMessageId = -1;
            context.Messages.Clear();

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _clients.Remove(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        public async Task GetMissing(string data)
        {
            var msg = JsonSerializer.Deserialize<YcsMessage>(data);
            _clients[Context.ConnectionId].Messages.Add(msg.Seq, new StoredMessage { Data = msg.Data, Type = MessageType.GetMissing });
            await ProcessMessageQueue();
        }

        public async Task UpdateV2(string data)
        {
            var msg = JsonSerializer.Deserialize<YcsMessage>(data);
            _clients[Context.ConnectionId].Messages.Add(msg.Seq, new StoredMessage { Data = msg.Data, Type = MessageType.Update });
            await ProcessMessageQueue();
        }

        private async Task ProcessMessageQueue()
        {
            var context = _clients[Context.ConnectionId];

            foreach (var key in context.Messages.Keys.ToList())
            {
                // Skip if we haven't received the next message yet.
                if (key != context.LastClientMessageId + 1)
                {
                    break;
                }

                var message = context.Messages[key];

                switch (message.Type)
                {
                    case MessageType.GetMissing:
                        {
                            byte[] encodedStateVector = DecodeString(message.Data);

                            byte[] update;
                            lock (_syncRoot)
                            {
                                update = _doc.EncodeStateAsUpdateV2(encodedStateVector);
                            }

                            var encodedUpdate = EncodeBytes(update);

                            var msg = new MessageToClient { Seq = ++context.ServerMessageId, Data = encodedUpdate };
                            await Clients.Caller.SendAsync("getMissing_v2", JsonSerializer.Serialize(msg));
                        }
                        break;
                    case MessageType.Update:
                        {
                            var update = DecodeString(message.Data);

                            string localUpdate = null;
                            EventHandler<(byte[] data, object origin, Transaction transaction)> updateHandler = (sender, e) => localUpdate = EncodeBytes(e.data);

                            lock (_syncRoot)
                            {
                                _doc.UpdateV2 += updateHandler;
                                try
                                {
                                    _doc.ApplyUpdateV2(update, this);
                                }
                                finally
                                {
                                    _doc.UpdateV2 -= updateHandler;
                                }
                            }

                            if (localUpdate != null)
                            {
                                // Send update to all connected clients.
                                foreach (var connectionId in _clients.Keys)
                                {
                                    var msg = new MessageToClient { Seq = ++_clients[connectionId].ServerMessageId, Data = localUpdate };
                                    await Clients.Client(connectionId).SendAsync("update_v2", JsonSerializer.Serialize(msg));
                                }
                            }
                        }
                        break;
                    default:
                        throw new NotSupportedException();
                }

                // We've processed the current message, remove it from the queue.
                context.LastClientMessageId++;
                context.Messages.Remove(key);
            }
        }

        private static string EncodeBytes(byte[] arr) => Convert.ToBase64String(arr);
        private static byte[] DecodeString(string str) => Convert.FromBase64String(str);
    }
}
