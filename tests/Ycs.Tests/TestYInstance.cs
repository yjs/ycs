// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;

namespace Ycs
{
    public class TestYInstance : YDoc
    {
        public TestConnector _tc;
        public IDictionary<TestYInstance, Queue<byte[]>> _receiving = new Dictionary<TestYInstance, Queue<byte[]>>();

        public TestYInstance(TestConnector connector, int clientId, YDocOptions options)
            : base(options)
        {
            ClientId = clientId;

            _tc = connector;
            _tc._allConns.Add(this);

            // Setup observe on local model.
            UpdateV2 += (s, e) =>
            {
                if (e.origin != _tc)
                {
                    using (var stream = new MemoryStream())
                    {
                        SyncProtocol.WriteUpdate(stream, e.data);
                        BroadcastMessage(this, stream.ToArray());
                    }
                }
            };

            Connect();
        }

        private void BroadcastMessage(TestYInstance sender, byte[] data)
        {
            if (_tc._onlineConns.Contains(sender))
            {
                foreach (var conn in _tc._onlineConns)
                {
                    if (sender != conn)
                    {
                        conn.Receive(data, sender);
                    }
                }
            }
        }

        public void Connect()
        {
            if (!_tc._onlineConns.Contains(this))
            {
                _tc._onlineConns.Add(this);

                using (var stream = new MemoryStream())
                {
                    SyncProtocol.WriteSyncStep1(stream, this);

                    // Publish SyncStep1
                    BroadcastMessage(this, stream.ToArray());

                    foreach (var remoteYInstance in _tc._onlineConns)
                    {
                        if (remoteYInstance != this)
                        {
                            stream.SetLength(0);
                            SyncProtocol.WriteSyncStep1(stream, remoteYInstance);

                            Receive(stream.ToArray(), remoteYInstance);
                        }
                    }
                }
            }
        }

        public void Receive(byte[] data, TestYInstance remoteDoc)
        {
            if (!_receiving.TryGetValue(remoteDoc, out var messages))
            {
                messages = new Queue<byte[]>();
                _receiving[remoteDoc] = messages;
            }

            messages.Enqueue(data);
        }

        public void Disconnect()
        {
            _receiving.Clear();
            _tc._onlineConns.Remove(this);
        }
    }
}
