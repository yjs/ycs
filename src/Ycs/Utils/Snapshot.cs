// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ycs
{
    public sealed class Snapshot : IEquatable<Snapshot>
    {
        internal readonly DeleteSet DeleteSet;
        internal readonly IDictionary<long, long> StateVector;

        internal Snapshot(DeleteSet ds, IDictionary<long, long> stateMap)
        {
            DeleteSet = ds;
            StateVector = stateMap;
        }

        public YDoc RestoreDocument(YDoc originDoc, YDocOptions opts = null)
        {
            if (originDoc.Gc)
            {
                // We should try to restore a GC-ed document, because some of the restored items might have their content deleted.
                throw new Exception("originDoc must not be garbage collected");
            }

            using (var encoder = new UpdateEncoderV2())
            {
                originDoc.Transact(tr =>
                {
                    int size = StateVector.Count(kvp => kvp.Value /* clock */ > 0);
                    encoder.RestWriter.WriteVarUint((uint)size);

                    // Splitting the structs before writing them to the encoder.
                    foreach (var kvp in StateVector)
                    {
                        var client = kvp.Key;
                        var clock = kvp.Value;

                        if (clock == 0)
                        {
                            continue;
                        }

                        if (clock < originDoc.Store.GetState(client))
                        {
                            tr.Doc.Store.GetItemCleanStart(tr, new ID(client, clock));
                        }

                        var structs = originDoc.Store.Clients[client];
                        var lastStructIndex = StructStore.FindIndexSS(structs, clock - 1);

                        // Write # encoded structs.
                        encoder.RestWriter.WriteVarUint((uint)(lastStructIndex + 1));
                        encoder.WriteClient(client);

                        // First clock written is 0.
                        encoder.RestWriter.WriteVarUint(0);

                        for (int i = 0; i <= lastStructIndex; i++)
                        {
                            structs[i].Write(encoder, 0);
                        }
                    }

                    DeleteSet.Write(encoder);
                });

                var newDoc = new YDoc(opts ?? originDoc.CloneOptionsWithNewGuid());
                newDoc.ApplyUpdateV2(encoder.ToArray(), transactionOrigin: "snapshot");
                return newDoc;
            }
        }

        public bool Equals(Snapshot other)
        {
            if (other == null)
            {
                return false;
            }

            var ds1 = DeleteSet.Clients;
            var ds2 = other.DeleteSet.Clients;
            var sv1 = StateVector;
            var sv2 = other.StateVector;

            if (sv1.Count != sv2.Count || ds1.Count != ds2.Count)
            {
                return false;
            }

            foreach (var kvp in sv1)
            {
                if (!sv2.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
                {
                    return false;
                }
            }

            foreach (var kvp in ds1)
            {
                var client = kvp.Key;
                var dsItems1 = kvp.Value;

                if (!ds2.TryGetValue(client, out var dsItems2))
                {
                    return false;
                }

                if (dsItems1.Count != dsItems2.Count)
                {
                    return false;
                }

                for (int i = 0; i < dsItems1.Count; i++)
                {
                    var dsItem1 = dsItems1[i];
                    var dsItem2 = dsItems2[i];
                    if (dsItem1.Clock != dsItem2.Clock || dsItem1.Length != dsItem2.Length)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public byte[] EncodeSnapshotV2()
        {
            using (var encoder = new DSEncoderV2())
            {
                DeleteSet.Write(encoder);
                EncodingUtils.WriteStateVector(encoder, StateVector);
                return encoder.ToArray();
            }
        }

        public static Snapshot DecodeSnapshot(Stream input)
        {
            using (var decoder = new DSDecoderV2(input))
            {
                var ds = DeleteSet.Read(decoder);
                var sv = EncodingUtils.ReadStateVector(decoder);
                return new Snapshot(ds, sv);
            }
        }
    }
}
