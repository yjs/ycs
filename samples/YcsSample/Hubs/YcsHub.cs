using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using YcsSample.Yjs;

namespace YcsSample.Hubs
{
    public class YcsHub : Hub
    {
        private class YjsMessage
        {
            [JsonPropertyName("clock")]
            public long Clock { get; set; }

            [JsonPropertyName("data")]
            public string Data { get; set; }

            [JsonPropertyName("inReplyTo")]
            public CommandType? InReplyTo { get; set; }
        }

        public override async Task OnConnectedAsync()
        {
            YcsManager.Instance.HandleClientConnected(Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            YcsManager.Instance.HandleClientDisconnected(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        public async Task GetMissing(string data)
        {
            var yjsMessage = JsonSerializer.Deserialize<YjsMessage>(data);

            var messageToProcess = new MessageToProcess
            {
                Command = CommandType.GetMissing,
                InReplyTo = yjsMessage.InReplyTo,
                Data = yjsMessage.Data
            };

            await YcsManager.Instance.EnqueueAndProcessMessagesAsync(Context.ConnectionId, yjsMessage.Clock, messageToProcess, Context.ConnectionAborted);
        }

        public async Task Update(string data)
        {
            var yjsMessage = JsonSerializer.Deserialize<YjsMessage>(data);

            var messageToProcess = new MessageToProcess
            {
                Command = CommandType.Update,
                InReplyTo = yjsMessage.InReplyTo,
                Data = yjsMessage.Data
            };

            await YcsManager.Instance.EnqueueAndProcessMessagesAsync(Context.ConnectionId, yjsMessage.Clock, messageToProcess, Context.ConnectionAborted);
        }
    }
}
