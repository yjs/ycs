using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Ycs;

namespace YcsSample.Hubs
{
    public class YcsHub : Hub
    {
        private static readonly YDoc _doc = new YDoc();

        public async Task GetMissing(string data)
        {
            var encodedStateVector = DecodeString(data);
            var update = _doc.EncodeStateAsUpdateV2(encodedStateVector);
            var encodedUpdate = EncodeBytes(update);

            await Clients.Caller.SendAsync("getMissing_result_v2", encodedUpdate);
        }

        public async Task UpdateV2(string data)
        {
            var update = DecodeString(data);
            _doc.ApplyUpdateV2(update, this);

            await Clients.Others.SendAsync("updateV2", data);
        }

        private static string EncodeBytes(byte[] arr) => Convert.ToBase64String(arr);
        private static byte[] DecodeString(string str) => Convert.FromBase64String(str);
    }
}
