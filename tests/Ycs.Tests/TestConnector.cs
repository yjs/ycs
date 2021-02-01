// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ycs
{
    public class TestConnector
    {
        public ISet<TestYInstance> _allConns = new HashSet<TestYInstance>();
        public ISet<TestYInstance> _onlineConns = new HashSet<TestYInstance>();
        public Random _prng = new Random();

        public TestYInstance CreateY(int clientId, YDocOptions options)
        {
            return new TestYInstance(this, clientId, options);
        }

        public bool FlushNextMessage(TestYInstance sender, TestYInstance receiver)
        {
            Assert.AreNotEqual(sender, receiver);

            var messages = receiver._receiving[sender];
            if (messages.Count == 0)
            {
                receiver._receiving.Remove(sender);
                return false;
            }

            var m = messages.Dequeue();
            // Debug.WriteLine($"MSG {sender.ClientId} -> {receiver.ClientId}, len {m.Length}:");
            // Debug.WriteLine(string.Join(",", m));

            using (var writer = new MemoryStream())
            {
                using (var reader = new MemoryStream(m))
                {
                    SyncProtocol.ReadSyncMessage(reader, writer, receiver, receiver._tc);
                }

                if (writer.Length > 0)
                {
                    // Send reply message.
                    var replyMessage = writer.ToArray();
                    // Debug.WriteLine($"REPLY {receiver.ClientId} -> {sender.ClientId}, len {replyMessage.Length}:");
                    // Debug.WriteLine(string.Join(",", replyMessage));
                    sender.Receive(replyMessage, receiver);
                }
            }

            return true;
        }

        public bool FlushRandomMessage()
        {
            var conns = _onlineConns.Where(conn => conn._receiving.Count > 0).ToList();
            if (conns.Count > 0)
            {
                var receiver = conns[_prng.Next(0, conns.Count)];
                var keys = receiver._receiving.Keys.ToList();
                var sender = keys[_prng.Next(0, keys.Count)];

                if (!FlushNextMessage(sender, receiver))
                {
                    return FlushRandomMessage();
                }

                return true;
            }

            return false;
        }

        public bool FlushAllMessages()
        {
            var didSomething = false;

            while (FlushRandomMessage())
            {
                didSomething = true;
            }

            return didSomething;
        }

        public void ReconnectAll()
        {
            foreach (var conn in _allConns)
            {
                conn.Connect();
            }
        }

        public void DisconnectAll()
        {
            foreach (var conn in _allConns)
            {
                conn.Disconnect();
            }
        }

        public void SyncAll()
        {
            ReconnectAll();
            FlushAllMessages();
        }

        public bool DisconnectRandom()
        {
            if (_onlineConns.Count == 0)
            {
                return false;
            }

            _onlineConns.ToList()[_prng.Next(0, _onlineConns.Count)].Disconnect();
            return true;
        }

        public bool ReconnectRandom()
        {
            var reconnectable = new List<TestYInstance>();
            foreach (var conn in _allConns)
            {
                if (!_onlineConns.Contains(conn))
                {
                    reconnectable.Add(conn);
                }
            }

            if (reconnectable.Count == 0)
            {
                return false;
            }

            reconnectable[_prng.Next(0, reconnectable.Count)].Connect();
            return true;
        }
    }
}
