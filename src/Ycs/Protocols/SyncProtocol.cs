// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.IO;

namespace Ycs
{
    internal static class SyncProtocol
    {
        public const uint MessageYjsSyncStep1 = 0;
        public const uint MessageYjsSyncStep2 = 1;
        public const uint MessageYjsUpdate = 2;

        public static void WriteSyncStep1(Stream stream, YDoc doc)
        {
            stream.WriteVarUint(MessageYjsSyncStep1);
            var sv = doc.EncodeStateVectorV2();
            stream.WriteVarUint8Array(sv);
        }

        public static void WriteSyncStep2(Stream stream, YDoc doc, byte[] encodedStateVector)
        {
            stream.WriteVarUint(MessageYjsSyncStep2);
            var update = doc.EncodeStateAsUpdateV2(encodedStateVector);
            stream.WriteVarUint8Array(update);
        }

        public static void ReadSyncStep1(Stream reader, Stream writer, YDoc doc)
        {
            var encodedStateVector = reader.ReadVarUint8Array();
            WriteSyncStep2(writer, doc, encodedStateVector);
        }

        public static void ReadSyncStep2(Stream stream, YDoc doc, object transactionOrigin)
        {
            var update = stream.ReadVarUint8Array();
            doc.ApplyUpdateV2(update, transactionOrigin);
        }

        public static void WriteUpdate(Stream stream, byte[] update)
        {
            stream.WriteVarUint(MessageYjsUpdate);
            stream.WriteVarUint8Array(update);
        }

        public static void ReadUpdate(Stream stream, YDoc doc, object transactionOrigin)
        {
            ReadSyncStep2(stream, doc, transactionOrigin);
        }

        public static uint ReadSyncMessage(Stream reader, Stream writer, YDoc doc, object transactionOrigin)
        {
            var messageType = reader.ReadVarUint();

            switch (messageType)
            {
                case MessageYjsSyncStep1:
                    ReadSyncStep1(reader, writer, doc);
                    break;
                case MessageYjsSyncStep2:
                    ReadSyncStep2(reader, doc, transactionOrigin);
                    break;
                case MessageYjsUpdate:
                    ReadUpdate(reader, doc, transactionOrigin);
                    break;
                default:
                    throw new Exception($"Unknown message type: {messageType}");
            }

            return messageType;
        }
    }
}
