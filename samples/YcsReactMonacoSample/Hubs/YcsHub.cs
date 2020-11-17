using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Ycs;

namespace YcsSample.Hubs
{
    public class YcsHub : Hub
    {
        private static readonly YDoc _doc = new YDoc();
        private static readonly object _syncRoot = new object();

        public async Task GetMissing(string data)
        {
            byte[] encodedStateVector = DecodeString(data);

            byte[] update;
            lock (_syncRoot)
            {
                update = _doc.EncodeStateAsUpdateV2(encodedStateVector);
            }
            var encodedUpdate = EncodeBytes(update);

            await Clients.Caller.SendAsync("getMissing_result_v2", encodedUpdate);
        }

        public async Task UpdateV2(string data)
        {
            var update = DecodeString(data);

            lock (_syncRoot)
            {
                _doc.ApplyUpdateV2(update, this);
            }

            await Clients.Others.SendAsync("updateV2", data);
        }

        private static string EncodeBytes(byte[] arr) => Convert.ToBase64String(arr);
        private static byte[] DecodeString(string str) => Convert.FromBase64String(str);
    }
}
