// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Ycs
{
    /// <summary>
    /// A transaction is created for every change on the Yjs model. It is possible
    /// to bundle changes on the Yjs model in a single transaction to minimize
    /// the number of messages sent and the number of observer calls.
    /// If possible the user of this library should bundle as many changes as possible.
    /// </summary>
    public class Transaction
    {
        // TODO: [alekseyk] To private?
        internal readonly IList<AbstractStruct> _mergeStructs;

        internal Transaction(YDoc doc, object origin, bool local)
        {
            Doc = doc;
            DeleteSet = new DeleteSet();
            BeforeState = Doc.Store.GetStateVector();
            AfterState = new Dictionary<long, long>();
            Changed = new Dictionary<AbstractType, ISet<string>>();
            ChangedParentTypes = new Dictionary<AbstractType, IList<YEvent>>();
            _mergeStructs = new List<AbstractStruct>();
            Origin = origin;
            Meta = new Dictionary<string, object>();
            Local = local;
            SubdocsAdded = new HashSet<YDoc>();
            SubdocsRemoved = new HashSet<YDoc>();
            SubdocsLoaded = new HashSet<YDoc>();
        }

        /// <summary>
        /// The Yjs instance.
        /// </summary>
        public YDoc Doc { get; }

        public object Origin { get; }

        /// <summary>
        /// Holds the state before the transaction started.
        /// </summary>
        public IDictionary<long, long> BeforeState { get; }

        /// <summary>
        /// Holds the state after the transaction.
        /// </summary>
        public IDictionary<long, long> AfterState { get; private set; }

        /// <summary>
        /// All types that were directly modified (property added or child
        /// inserted/deleted). New types are not included in this Set.
        /// Maps from type to parentSubs ('item.parentSub = null' for YArray).
        /// </summary>
        public IDictionary<AbstractType, ISet<string>> Changed { get; }

        /// <summary>
        /// Stores the events for the types that observe also child elements.
        /// It is mainly used by 'observeDeep'.
        /// </summary>
        public IDictionary<AbstractType, IList<YEvent>> ChangedParentTypes { get; }

        /// <summary>
        /// Stores meta information on the transaction.
        /// </summary>
        public IDictionary<string, object> Meta { get; }

        /// <summary>
        /// Whether this change originates from this doc.
        /// </summary>
        public bool Local { get; }

        public ISet<YDoc> SubdocsAdded { get; }

        public ISet<YDoc> SubdocsRemoved { get; }

        public ISet<YDoc> SubdocsLoaded { get; }

        /// <summary>
        /// Describes the set of deleted items by Ids.
        /// </summary>
        internal DeleteSet DeleteSet { get; }

        internal ID GetNextId()
        {
            return new ID(Doc.ClientId, Doc.Store.GetState(Doc.ClientId));
        }

        /// <summary>
        /// If 'type.parent' was added in current transaction, 'type' technically did not change,
        /// it was just added and we should not fire events for 'type'.
        /// </summary>
        internal void AddChangedTypeToTransaction(AbstractType type, string parentSub)
        {
            var item = type._item;
            if (item == null || (BeforeState.TryGetValue(item.Id.Client, out var clock) && item.Id.Clock < clock && !item.Deleted))
            {
                if (!Changed.TryGetValue(type, out var set))
                {
                    set = new HashSet<string>();
                    Changed[type] = set;
                }

                set.Add(parentSub);
            }
        }

        internal static void CleanupTransactions(IList<Transaction> transactionCleanups, int i)
        {
            if (i < transactionCleanups.Count)
            {
                var transaction = transactionCleanups[i];
                var doc = transaction.Doc;
                var store = doc.Store;
                var ds = transaction.DeleteSet;
                var mergeStructs = transaction._mergeStructs;
                var actions = new List<Action>();

                try
                {
                    ds.SortAndMergeDeleteSet();
                    transaction.AfterState = store.GetStateVector();
                    doc._transaction = null;

                    actions.Add(() =>
                    {
                        doc.InvokeOnBeforeObserverCalls(transaction);
                    });

                    actions.Add(() =>
                    {
                        foreach (var kvp in transaction.Changed)
                        {
                            var itemType = kvp.Key;
                            var subs = kvp.Value;

                            if (itemType._item == null || !itemType._item.Deleted)
                            {
                                itemType.CallObserver(transaction, subs);
                            }
                        }
                    });

                    actions.Add(() =>
                    {
                        // Deep observe events.
                        foreach (var kvp in transaction.ChangedParentTypes)
                        {
                            var type = kvp.Key;
                            var events = kvp.Value;

                            // We need to think about the possibility that the user transforms the YDoc in the event.
                            if (type._item == null || !type._item.Deleted)
                            {
                                foreach (var evt in events)
                                {
                                    if (evt.Target._item == null || !evt.Target._item.Deleted)
                                    {
                                        evt.CurrentTarget = type;
                                    }
                                }

                                // Sort events by path length so that top-level events are fired first.
                                var sortedEvents = events.ToList();
                                sortedEvents.Sort((a, b) => a.Path.Count - b.Path.Count);
                                Debug.Assert(sortedEvents.Count > 0);

                                actions.Add(() =>
                                {
                                    type.CallDeepEventHandlerListeners(sortedEvents, transaction);
                                });
                            }
                        }
                    });

                    actions.Add(() =>
                    {
                        doc.InvokeOnAfterTransaction(transaction);
                    });

                    CallAll(actions);
                }
                finally
                {
                    // Replace deleted items with ItemDeleted / GC.
                    // This is where content is actually removed from the Yjs Doc.
                    if (doc.Gc)
                    {
                        ds.TryGcDeleteSet(store, doc.GcFilter);
                    }

                    ds.TryMergeDeleteSet(store);

                    // On all affected store.clients props, try to merge.
                    foreach (var kvp in transaction.AfterState)
                    {
                        var client = kvp.Key;
                        var clock = kvp.Value;

                        if (!transaction.BeforeState.TryGetValue(client, out var beforeClock))
                        {
                            beforeClock = 0;
                        }

                        if (beforeClock != clock)
                        {
                            var structs = store.Clients[client];
                            var firstChangePos = Math.Max(StructStore.FindIndexSS(structs, beforeClock), 1);
                            for (int j = structs.Count - 1; j >= firstChangePos; j--)
                            {
                                DeleteSet.TryToMergeWithLeft(structs, j);
                            }
                        }
                    }

                    // Try to merge mergeStructs.
                    // TODO: It makes more sense to transform mergeStructs to a DS, sort it, and merge from right to left
                    //       but at the moment DS does not handle duplicates.
                    for (int j = 0; j < mergeStructs.Count; j++)
                    {
                        var client = mergeStructs[j].Id.Client;
                        var clock = mergeStructs[j].Id.Clock;
                        var structs = store.Clients[client];
                        var replacedStructPos = StructStore.FindIndexSS(structs, clock);

                        if (replacedStructPos + 1 < structs.Count)
                        {
                            DeleteSet.TryToMergeWithLeft(structs, replacedStructPos + 1);
                        }

                        if (replacedStructPos > 0)
                        {
                            DeleteSet.TryToMergeWithLeft(structs, replacedStructPos);
                        }
                    }

                    if (!transaction.Local)
                    {
                        if (!transaction.AfterState.TryGetValue(doc.ClientId, out var afterClock))
                        {
                            afterClock = -1;
                        }

                        if (!transaction.BeforeState.TryGetValue(doc.ClientId, out var beforeClock))
                        {
                            beforeClock = -1;
                        }

                        if (afterClock != beforeClock)
                        {
                            doc.ClientId = YDoc.GenerateNewClientId();
                            // Debug.WriteLine($"{nameof(Transaction)}: Changed the client-id because another client seems to be using it.");
                        }
                    }

                    // @todo: Merge all the transactions into one and provide send the data as a single update message.
                    doc.InvokeOnAfterTransactionCleanup(transaction);

                    doc.InvokeUpdateV2(transaction);

                    foreach (var subDoc in transaction.SubdocsAdded)
                    {
                        doc.Subdocs.Add(subDoc);
                    }

                    foreach (var subDoc in transaction.SubdocsRemoved)
                    {
                        doc.Subdocs.Remove(subDoc);
                    }

                    doc.InvokeSubdocsChanged(transaction.SubdocsLoaded, transaction.SubdocsAdded, transaction.SubdocsRemoved);

                    foreach (var subDoc in transaction.SubdocsRemoved)
                    {
                        subDoc.Destroy();
                    }

                    if (transactionCleanups.Count <= i + 1)
                    {
                        doc._transactionCleanups.Clear();
                        doc.InvokeAfterAllTransactions(transactionCleanups);
                    }
                    else
                    {
                        CleanupTransactions(transactionCleanups, i + 1);
                    }
                }
            }
        }

        /// <summary>
        /// Redoes the effect of this operation.
        /// </summary>
        internal AbstractStruct RedoItem(Item item, ISet<Item> redoItems)
        {
            var doc = Doc;
            var store = doc.Store;
            var ownClientId = doc.ClientId;
            var redone = item.Redone;

            if (redone != null)
            {
                return store.GetItemCleanStart(this, redone.Value);
            }

            var parentItem = (item.Parent as AbstractType)?._item;
            AbstractStruct left;
            AbstractStruct right;

            if (item.ParentSub == null)
            {
                // Is an array item. Insert at the old position.
                left = item.Left;
                right = item;
            }
            else
            {
                // Is a map item. Insert at current value.
                left = item;
                while ((left as Item)?.Right != null)
                {
                    left = (left as Item).Right;
                    if (left.Id.Client != ownClientId)
                    {
                        // It is not possible to redo this item because it conflicts with a change from another client.
                        return null;
                    }
                }

                if ((left as Item)?.Right != null)
                {
                    left = (item.Parent as AbstractType)?._map[item.ParentSub];
                }

                right = null;
            }

            // Make sure that parent is redone.
            if (parentItem != null && parentItem.Deleted && parentItem.Redone == null)
            {
                // Try to undo parent if it will be undone anyway.
                if (!redoItems.Contains(parentItem) || RedoItem(parentItem, redoItems) == null)
                {
                    return null;
                }
            }

            if (parentItem != null && parentItem.Redone != null)
            {
                while (parentItem.Redone != null)
                {
                    parentItem = (Item)store.GetItemCleanStart(this, parentItem.Redone.Value);
                }

                // Find next cloned_redo items.
                while (left != null)
                {
                    var leftTrace = left;
                    while (leftTrace != null && ((leftTrace as Item)?.Parent as AbstractType)?._item != parentItem)
                    {
                        leftTrace = (leftTrace as Item).Redone == null ? null : store.GetItemCleanStart(this, (leftTrace as Item).Redone.Value);
                    }

                    if (leftTrace != null && ((leftTrace as Item)?.Parent as AbstractType)?._item == parentItem)
                    {
                        left = leftTrace;
                        break;
                    }

                    left = (left as Item)?.Left;
                }

                while (right != null)
                {
                    var rightTrace = right;
                    while (rightTrace != null && ((rightTrace as Item)?.Parent as AbstractType)?._item != parentItem)
                    {
                        rightTrace = (rightTrace as Item).Redone == null ? null : store.GetItemCleanStart(this, (rightTrace as Item).Redone.Value);
                    }

                    if (rightTrace != null && ((rightTrace as Item)?.Parent as AbstractType)?._item == parentItem)
                    {
                        right = rightTrace;
                        break;
                    }

                    right = (right as Item)?.Right;
                }
            }

            var nextClock = store.GetState(ownClientId);
            var nextId = new ID(ownClientId, nextClock);

            var redoneItem = new Item(
                nextId,
                left,
                (left as Item)?.LastId,
                right,
                right?.Id,
                parentItem == null ? item.Parent : (parentItem.Content as ContentType)?.Type,
                item.ParentSub,
                item.Content.Copy());

            item.Redone = nextId;

            redoneItem.KeepItemAndParents(true);
            redoneItem.Integrate(this, 0);

            return redoneItem;
        }

        internal static void SplitSnapshotAffectedStructs(Transaction transaction, Snapshot snapshot)
        {
            if (!transaction.Meta.TryGetValue("splitSnapshotAffectedStructs", out var metaObj))
            {
                metaObj = new HashSet<Snapshot>();
                transaction.Meta["splitSnapshotAffectedStructs"] = metaObj;
            }

            var meta = metaObj as ISet<Snapshot>;
            var store = transaction.Doc.Store;

            // Check if we already split for this snapshot.
            if (!meta.Contains(snapshot))
            {
                foreach (var kvp in snapshot.StateVector)
                {
                    var client = kvp.Key;
                    var clock = kvp.Value;

                    if (clock < store.GetState(client))
                    {
                        store.GetItemCleanStart(transaction, new ID(client, clock));
                    }
                }

                snapshot.DeleteSet.IterateDeletedStructs(transaction, item => true);
                meta.Add(snapshot);
            }
        }

        /// <returns>Whether the data was written.</returns>
        internal bool WriteUpdateMessageFromTransaction(IUpdateEncoder encoder)
        {
            if (DeleteSet.Clients.Count == 0 && !AfterState.Any(kvp => !BeforeState.TryGetValue(kvp.Key, out var clockB) || kvp.Value != clockB))
            {
                return false;
            }

            DeleteSet.SortAndMergeDeleteSet();
            EncodingUtils.WriteClientsStructs(encoder, Doc.Store, BeforeState);
            DeleteSet.Write(encoder);

            return true;
        }

        private static void CallAll(IList<Action> funcs, int index = 0)
        {
            try
            {
                for (; index < funcs.Count; index++)
                {
                    funcs[index]();
                }
            }
            finally
            {
                if (index < funcs.Count)
                {
                    CallAll(funcs, index + 1);
                }
            }
        }
    }
}
