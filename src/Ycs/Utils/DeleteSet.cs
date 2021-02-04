// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Ycs
{
    /// <summary>
    /// DeleteSet is a temporary object that is created when needed.
    /// - When created in a transaction, it must only be accessed after sorting and merging.
    ///   - This DeleteSet is sent to other clients.
    /// - We do not create a DeleteSet when we send a sync message. The DeleteSet message is created
    ///   directly from StructStore.
    /// - We read a DeleteSet as a apart of sync/update message. In this case the DeleteSet is already
    ///   sorted and merged.
    /// </summary>
    internal class DeleteSet
    {
        public struct DeleteItem
        {
            public readonly long Clock;
            public readonly long Length;

            public DeleteItem(long clock, long length)
            {
                Clock = clock;
                Length = length;
            }
        }

        public DeleteSet()
        {
            Clients = new Dictionary<long, List<DeleteItem>>();
        }

        public DeleteSet(IList<DeleteSet> dss)
            : this()
        {
            MergeDeleteSets(dss);
        }

        public DeleteSet(StructStore ss)
            : this()
        {
            CreateDeleteSetFromStructStore(ss);
        }

        public IDictionary<long, List<DeleteItem>> Clients { get; }

        public void Add(long client, long clock, long length)
        {
            if (!Clients.TryGetValue(client, out var deletes))
            {
                deletes = new List<DeleteItem>(2);
                Clients[client] = deletes;
            }

            deletes.Add(new DeleteItem(clock, length));
        }

        /// <summary>
        /// Iterate over all structs that the DeleteSet gc'd.
        /// </summary>
        public void IterateDeletedStructs(Transaction transaction, Predicate<AbstractStruct> fun)
        {
            foreach (var kvp in Clients)
            {
                var structs = transaction.Doc.Store.Clients[kvp.Key];
                foreach (var del in kvp.Value)
                {
                    transaction.Doc.Store.IterateStructs(transaction, structs, del.Clock, del.Length, fun);
                }
            }
        }

        public int? FindIndexSS(IList<DeleteItem> dis, long clock)
        {
            var left = 0;
            var right = dis.Count - 1;

            while (left <= right)
            {
                var midIndex = (left + right) / 2;
                var mid = dis[midIndex];
                var midClock = mid.Clock;

                if (midClock <= clock)
                {
                    if (clock < midClock + mid.Length)
                    {
                        return midIndex;
                    }

                    left = midIndex + 1;
                }
                else
                {
                    right = midIndex - 1;
                }
            }

            return null;
        }

        public bool IsDeleted(ID id)
        {
            return Clients.TryGetValue(id.Client, out var dis)
                   && FindIndexSS(dis, id.Clock) != null;
        }

        public void SortAndMergeDeleteSet()
        {
            foreach (var dels in Clients.Values)
            {
                dels.Sort((a, b) => a.Clock.CompareTo(b.Clock));

                // Merge items without filtering or splicing the array.
                // i is the current pointer.
                // j refers to the current insert position for the pointed item.
                // Try to merge dels[i] into dels[j-1] or set dels[j]=dels[i].
                int i, j;
                for (i = 1, j = 1; i < dels.Count; i++)
                {
                    var left = dels[j - 1];
                    var right = dels[i];

                    if (left.Clock + left.Length == right.Clock)
                    {
                        left = dels[j - 1] = new DeleteItem(left.Clock, left.Length + right.Length);
                    }
                    else
                    {
                        if (j < i)
                        {
                            dels[j] = right;
                        }

                        j++;
                    }
                }

                // Trim the collection.
                if (j < dels.Count)
                {
                    dels.RemoveRange(j, dels.Count - j);
                }
            }
        }

        public void TryGc(StructStore store, Predicate<Item> gcFilter)
        {
            TryGcDeleteSet(store, gcFilter);
            TryMergeDeleteSet(store);
        }

        public void TryGcDeleteSet(StructStore store, Predicate<Item> gcFilter)
        {
            foreach (var kvp in Clients)
            {
                var client = kvp.Key;
                var deleteItems = kvp.Value;
                var structs = store.Clients[client];

                for (int di = deleteItems.Count - 1; di >= 0; di--)
                {
                    var deleteItem = deleteItems[di];
                    var endDeleteItemClock = deleteItem.Clock + deleteItem.Length;

                    for (int si = StructStore.FindIndexSS(structs, deleteItem.Clock); si < structs.Count; si++)
                    {
                        var str = structs[si];
                        if (str.Id.Clock >= endDeleteItemClock)
                        {
                            break;
                        }

                        if (str is Item strItem && strItem.Deleted && !strItem.Keep && gcFilter(strItem))
                        {
                            strItem.Gc(store, parentGCd: false);
                        }
                    }
                }
            }
        }

        public void TryMergeDeleteSet(StructStore store)
        {
            // Try to merge deleted / gc'd items.
            // Merge from right to left for better efficiency and so we don't miss any merge targets.
            foreach (var kvp in Clients)
            {
                var client = kvp.Key;
                var deleteItems = kvp.Value;
                var structs = store.Clients[client];

                for (int di = deleteItems.Count - 1; di >= 0; di--)
                {
                    var deleteItem = deleteItems[di];

                    // Start with merging the item next to the last deleted item.
                    var mostRightIndexToCheck = Math.Min(structs.Count - 1, 1 + StructStore.FindIndexSS(structs, deleteItem.Clock + deleteItem.Length - 1));
                    for (int si = mostRightIndexToCheck; si > 0 && structs[si].Id.Clock >= deleteItem.Clock; si--)
                    {
                        TryToMergeWithLeft(structs, si);
                    }
                }
            }
        }

        public static void TryToMergeWithLeft(IList<AbstractStruct> structs, int pos)
        {
            var left = structs[pos - 1];
            var right = structs[pos];

            if (left.Deleted == right.Deleted && left.GetType().IsAssignableFrom(right.GetType()))
            {
                if (left.MergeWith(right))
                {
                    structs.RemoveAt(pos);

                    if (right is Item rightItem && rightItem.ParentSub != null)
                    {
                        if ((rightItem.Parent as AbstractType)._map.TryGetValue(rightItem.ParentSub, out var value) && value == right)
                        {
                            (rightItem.Parent as AbstractType)._map[rightItem.ParentSub] = left as Item;
                        }
                    }
                }
            }
        }

        private void MergeDeleteSets(IList<DeleteSet> dss)
        {
            for (int dssI = 0; dssI < dss.Count; dssI++)
            {
                foreach (var kvp in dss[dssI].Clients)
                {
                    var client = kvp.Key;
                    var delsLeft = kvp.Value;

                    if (!Clients.ContainsKey(client))
                    {
                        // Write all missing keys from current ds and all following.
                        // If merged already contains 'client' current ds has already been added.
                        var dels = new List<DeleteItem>(delsLeft);

                        for (int i = dssI + 1; i < dss.Count; i++)
                        {
                            if (dss[i].Clients.TryGetValue(client, out var appends))
                            {
                                dels.AddRange(appends);
                            }
                        }

                        Clients[client] = dels;
                    }
                }
            }

            SortAndMergeDeleteSet();
        }

        private void CreateDeleteSetFromStructStore(StructStore ss)
        {
            foreach (var kvp in ss.Clients)
            {
                var client = kvp.Key;
                var structs = kvp.Value;
                var dsItems = new List<DeleteItem>();

                for (int i = 0; i < structs.Count; i++)
                {
                    var str = structs[i];
                    if (str.Deleted)
                    {
                        var clock = str.Id.Clock;
                        var len = str.Length;

                        while (i + 1 < structs.Count)
                        {
                            var next = structs[i + 1];
                            if ((next.Id.Clock == clock + len) && next.Deleted)
                            {
                                len += next.Length;
                                i++;
                            }
                            else
                            {
                                break;
                            }
                        }

                        dsItems.Add(new DeleteItem(clock, len));
                    }
                }

                if (dsItems.Count > 0)
                {
                    Clients[client] = dsItems;
                }
            }
        }

        public void Write(IDSEncoder encoder)
        {
            encoder.RestWriter.WriteVarUint((uint)Clients.Count);

            foreach (var kvp in Clients)
            {
                var client = kvp.Key;
                var dsItems = kvp.Value;
                var len = dsItems.Count;

                encoder.ResetDsCurVal();
                encoder.RestWriter.WriteVarUint((uint)client);
                encoder.RestWriter.WriteVarUint((uint)len);

                for (int i = 0; i < len; i++)
                {
                    var item = dsItems[i];
                    encoder.WriteDsClock(item.Clock);
                    encoder.WriteDsLength(item.Length);
                }
            }
        }

        public static DeleteSet Read(IDSDecoder decoder)
        {
            var ds = new DeleteSet();

            var numClients = decoder.Reader.ReadVarUint();
            Debug.Assert(numClients >= 0);

            for (var i = 0; i < numClients; i++)
            {
                decoder.ResetDsCurVal();

                var client = decoder.Reader.ReadVarUint();
                var numberOfDeletes = decoder.Reader.ReadVarUint();

                if (numberOfDeletes > 0)
                {
                    if (!ds.Clients.TryGetValue(client, out var dsField))
                    {
                        dsField = new List<DeleteItem>((int)numberOfDeletes);
                        ds.Clients[client] = dsField;
                    }

                    for (var j = 0; j < numberOfDeletes; j++)
                    {
                        var deleteItem = new DeleteItem(decoder.ReadDsClock(), decoder.ReadDsLength());
                        dsField.Add(deleteItem);
                    }
                }
            }

            return ds;
        }
    }
}
