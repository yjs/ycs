// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Ycs
{
    /// <summary>
    /// We use first five bits in the info flag for determining the type of the struct.
    /// 0: GC
    /// 1: Deleted content
    /// 2: JSON content
    /// 3: Binary content
    /// 4: String content
    /// 5: Embed content (for richtext content)
    /// 6: Format content (a formatting marker for richtext content)
    /// 7: Type content
    /// 8: Any content
    /// 9: Doc content
    /// </summary>
    internal static class EncodingUtils
    {
        public static IContent ReadItemContent(IUpdateDecoder decoder, byte info)
        {
            switch (info & Bits.Bits5)
            {
                case 0: // GC
                    throw new Exception("GC is not ItemContent");
                case 1: // Deleted
                    return ContentDeleted.Read(decoder);
                case 2: // JSON
                    return ContentJson.Read(decoder);
                case 3: // Binary
                    return ContentBinary.Read(decoder);
                case 4: // String
                    return ContentString.Read(decoder);
                case 5: // Embed
                    return ContentEmbed.Read(decoder);
                case 6: // Format
                    return ContentFormat.Read(decoder);
                case 7: // Type
                    return ContentType.Read(decoder);
                case 8: // Any
                    return ContentAny.Read(decoder);
                case 9: // Doc
                    return ContentDoc.Read(decoder);
                default:
                    throw new InvalidOperationException($"Content type not recognized: {info}");
            }
        }

        /// <summary>
        /// Read the next Item in a Decoder and fill this Item with the read data.
        /// <br/>
        /// This is called when data is received from a remote peer.
        /// </summary>
        public static void ReadStructs(IUpdateDecoder decoder, Transaction transaction, StructStore store)
        {
            var clientStructRefs = ReadClientStructRefs(decoder, transaction.Doc);
            store.MergeReadStructsIntoPendingReads(clientStructRefs);
            store.ResumeStructIntegration(transaction);
            store.CleanupPendingStructs();
            store.TryResumePendingDeleteReaders(transaction);
        }

        /// <param name="structs">All structs by 'client'.</param>
        /// <param name="clock">Write structs starting with 'ID(client,clock)'.</param>
        public static void WriteStructs(IUpdateEncoder encoder, IList<AbstractStruct> structs, long client, long clock)
        {
            // Write first id.
            int startNewStructs = StructStore.FindIndexSS(structs, clock);

            // Write # encoded structs.
            encoder.RestWriter.WriteVarUint((uint)(structs.Count - startNewStructs));
            encoder.WriteClient(client);
            encoder.RestWriter.WriteVarUint((uint)clock);

            // Write first struct with offset.
            var firstStruct = structs[startNewStructs];
            firstStruct.Write(encoder, (int)(clock - firstStruct.Id.Clock));

            for (int i = startNewStructs + 1; i < structs.Count; i++)
            {
                structs[i].Write(encoder, 0);
            }
        }

        public static void WriteClientsStructs(IUpdateEncoder encoder, StructStore store, IDictionary<long, long> _sm)
        {
            // We filter all valid _sm entries into sm.
            var sm = new Dictionary<long, long>();
            foreach (var kvp in _sm)
            {
                var client = kvp.Key;
                var clock = kvp.Value;

                // Only write if new structs are available.
                if (store.GetState(client) > clock)
                {
                    sm[client] = clock;
                }
            }

            foreach (var kvp in store.GetStateVector())
            {
                var client = kvp.Key;
                if (!sm.ContainsKey(client))
                {
                    sm[client] = 0;
                }
            }

            // Write # states that were updated.
            encoder.RestWriter.WriteVarUint((uint)sm.Count);

            // Write items with higher client ids first.
            // This heavily improves the conflict resolution algorithm.
            var sortedClients = sm.Keys.ToList();
            sortedClients.Sort((a, b) => b.CompareTo(a));

            foreach (var client in sortedClients)
            {
                WriteStructs(encoder, store.Clients[client], client, sm[client]);
            }
        }

        public static IDictionary<long, List<AbstractStruct>> ReadClientStructRefs(IUpdateDecoder decoder, YDoc doc)
        {
            var clientRefs = new Dictionary<long, List<AbstractStruct>>();
            var numOfStateUpdates = decoder.Reader.ReadVarUint();

            for (var i = 0; i < numOfStateUpdates; i++)
            {
                var numberOfStructs = (int)decoder.Reader.ReadVarUint();
                Debug.Assert(numberOfStructs >= 0);

                var refs = new List<AbstractStruct>(numberOfStructs);
                long client = decoder.ReadClient();
                long clock = decoder.Reader.ReadVarUint();

                clientRefs[client] = refs;

                for (var j = 0; j < numberOfStructs; j++)
                {
                    var info = decoder.ReadInfo();
                    if ((Bits.Bits5 & info) != 0)
                    {
                        // The item that was originally to the left of this item.
                        var leftOrigin = (info & Bit.Bit8) == Bit.Bit8 ? (ID?)decoder.ReadLeftId() : null;
                        // The item that was originally to the right of this item.
                        var rightOrigin = (info & Bit.Bit7) == Bit.Bit7 ? (ID?)decoder.ReadRightId() : null;
                        var cantCopyParentInfo = (info & (Bit.Bit7 | Bit.Bit8)) == 0;
                        var hasParentYKey = cantCopyParentInfo ? decoder.ReadParentInfo() : false;

                        // If parent == null and neither left nor right are defined, then we know that 'parent' is child of 'y'
                        // and we read the next string as parentYKey.
                        // It indicates how we store/retrieve parent from 'y.share'.
                        var parentYKey = cantCopyParentInfo && hasParentYKey ? decoder.ReadString() : null;

                        var str = new Item(
                            new ID(client, clock),
                            null, // left
                            leftOrigin,
                            null, // right
                            rightOrigin, // rightOrigin
                            cantCopyParentInfo && !hasParentYKey ? decoder.ReadLeftId() : (parentYKey != null ? (object)doc.Get<AbstractType>(parentYKey) : null), // parent
                            cantCopyParentInfo && (info & Bit.Bit6) == Bit.Bit6 ? decoder.ReadString() : null, // parentSub
                            ReadItemContent(decoder, info) // content
                            );

                        refs.Add(str);
                        clock += str.Length;
                    }
                    else
                    {
                        var length = decoder.ReadLength();
                        refs.Add(new GC(new ID(client, clock), length));
                        clock += length;
                    }
                }
            }

            return clientRefs;
        }

        public static void WriteStateVector(IDSEncoder encoder, IDictionary<long, long> sv)
        {
            encoder.RestWriter.WriteVarUint((uint)sv.Count);

            foreach (var kvp in sv)
            {
                var client = kvp.Key;
                var clock = kvp.Value;

                encoder.RestWriter.WriteVarUint((uint)client);
                encoder.RestWriter.WriteVarUint((uint)clock);
            }
        }

        public static IDictionary<long, long> ReadStateVector(IDSDecoder decoder)
        {
            var ssLength = (int)decoder.Reader.ReadVarUint();
            var ss = new Dictionary<long, long>(ssLength);

            for (var i = 0; i < ssLength; i++)
            {
                var client = decoder.Reader.ReadVarUint();
                var clock = decoder.Reader.ReadVarUint();
                ss[client] = clock;
            }

            return ss;
        }

        public static IDictionary<long, long> DecodeStateVector(Stream input)
        {
            return ReadStateVector(new DSDecoderV2(input));
        }
    }
}
