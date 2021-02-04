// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Ycs
{
    public class StackItem
    {
        public IDictionary<long, long> BeforeState;
        public IDictionary<long, long> AfterState;
        // Use this to save and restore metadata like selection range.
        public IDictionary<string, object> Meta;

        internal DeleteSet DeleteSet;

        internal StackItem(DeleteSet ds, IDictionary<long, long> beforeState, IDictionary<long, long> afterState)
        {
            DeleteSet = ds;
            BeforeState = beforeState;
            AfterState = afterState;
            // TODO: [alekseyk] Always needed?
            Meta = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Fires 'stack-item-added' event when a stack item was added to either the undo- or
    /// the redo-stack. You may store additional stack information via the metadata property
    /// on 'event.stackItem.meta' (it is a collection of metadata properties).
    /// Fires 'stack-item-popped' event when a stack item was popped from either the undo- or
    /// the redo-stack. You may restore the saved stack information from 'event.stackItem.Meta'.
    /// </summary>
    public class UndoManager
    {
        public enum OperationType
        {
            Undo,
            Redo
        }

        public class StackEventArgs : EventArgs
        {
            // TODO: [alekseyk] To Read-Only.
            public StackEventArgs(StackItem item, OperationType type, IDictionary<AbstractType, IList<YEvent>> changedParentTypes, object origin)
            {
                StackItem = item;
                Type = type;
                ChangedParentTypes = changedParentTypes;
                Origin = origin;
            }

            public StackItem StackItem { get; }
            public OperationType Type { get; }
            public IDictionary<AbstractType, IList<YEvent>> ChangedParentTypes { get; }
            public object Origin { get; }
        }

        private IList<AbstractType> _scope;
        private Func<Item, bool> _deleteFilter;
        private ISet<object> _trackedOrigins;
        private Stack<StackItem> _undoStack;
        private Stack<StackItem> _redoStack;

        // Whether the client is currently undoing (calling UndoManager.Undo()).
        private bool _undoing;
        private bool _redoing;
        private YDoc _doc;
        private DateTime _lastChange;
        private int _captureTimeout;

        public UndoManager(AbstractType typeScope)
            : this(new[] { typeScope }, 500, it => true, new HashSet<object> { null })
        {
            // Do nothing.
        }

        // TODO: [alekseyk] Set default parameters (not for the Func<>), or create options, like YDocOptions.
        public UndoManager(IList<AbstractType> typeScopes, int captureTimeout, Func<Item, bool> deleteFilter, ISet<object> trackedOrigins)
        {
            _scope = typeScopes;
            _deleteFilter = deleteFilter ?? (_ => true);
            _trackedOrigins = trackedOrigins ?? new HashSet<object>();
            _trackedOrigins.Add(this);
            _undoStack = new Stack<StackItem>();
            _redoStack = new Stack<StackItem>();
            _undoing = false;
            _redoing = false;
            _doc = typeScopes[0].Doc;
            _lastChange = DateTime.MinValue;
            _captureTimeout = captureTimeout;

            _doc.AfterTransaction += OnAfterTransaction;
        }

        public event EventHandler<StackEventArgs> StackItemAdded;
        public event EventHandler<StackEventArgs> StackItemPopped;

        public int Count => _undoStack.Count;

        public void Clear()
        {
            _doc.Transact(tr =>
            {
                void clearItem(StackItem stackItem)
                {
                    stackItem.DeleteSet.IterateDeletedStructs(tr, i =>
                    {
                        if (i is Item item && _scope.Any(type => IsParentOf(type, item)))
                        {
                            item.KeepItemAndParents(false);
                        }

                        return true;
                    });
                }

                foreach (var item in _undoStack)
                {
                    clearItem(item);
                }

                foreach (var item in _redoStack)
                {
                    clearItem(item);
                }
            });

            _undoStack.Clear();
            _redoStack.Clear();
        }

        /// <summary>
        /// UndoManager merges Undo-StackItem if they are created within time-gap
        /// smaller than 'captureTimeout'. Call this method so that the next StackItem
        /// won't be merged.
        /// </summary>
        public void StopCapturing()
        {
            _lastChange = DateTime.MinValue;
        }

        /// <summary>
        /// Undo last changes on type.
        /// </summary>
        /// <returns>
        /// Returns stack item if a change was applied.
        /// </returns>
        public StackItem Undo()
        {
            _undoing = true;
            StackItem res;

            try
            {
                res = PopStackItem(_undoStack, OperationType.Undo);
            }
            finally
            {
                _undoing = false;
            }

            return res;
        }

        /// <summary>
        /// Redo last changes on type.
        /// </summary>
        /// <returns>
        /// Returns stack item if a change was applied.
        /// </returns>
        public StackItem Redo()
        {
            _redoing = true;
            StackItem res;

            try
            {
                res = PopStackItem(_redoStack, OperationType.Redo);
            }
            finally
            {
                _redoing = false;
            }

            return res;
        }

        private void OnAfterTransaction(object sender, Transaction transaction)
        {
            // Only track certain transactions.
            if (!_scope.Any(type => transaction.ChangedParentTypes.ContainsKey(type)) ||
                (!_trackedOrigins.Contains(transaction.Origin) &&
                    (transaction.Origin == null || !_trackedOrigins.Any(to => (to as Type)?.IsAssignableFrom(transaction.Origin.GetType()) ?? false))))
            {
                return;
            }

            var undoing = _undoing;
            var redoing = _redoing;
            var stack = undoing ? _redoStack : _undoStack;

            if (undoing)
            {
                // Next undo should not be appended to last stack item.
                StopCapturing();
            }
            else if (!redoing)
            {
                // Neither undoing nor redoing: delete redoStack.
                _redoStack.Clear();
            }

            var beforeState = transaction.BeforeState;
            var afterState = transaction.AfterState;

            var now = DateTime.UtcNow;
            if ((now - _lastChange).TotalMilliseconds < _captureTimeout && stack.Count > 0 && !undoing && !redoing)
            {
                // Append change to last stack op.
                var lastOp = stack.Peek();
                lastOp.DeleteSet = new DeleteSet(new List<DeleteSet>() { lastOp.DeleteSet, transaction.DeleteSet });
                lastOp.AfterState = afterState;
            }
            else
            {
                // Create a new stack op.
                var item = new StackItem(transaction.DeleteSet, beforeState, afterState);
                stack.Push(item);
            }

            if (!undoing && !redoing)
            {
                _lastChange = now;
            }

            // Make sure that deleted structs are not GC'd.
            transaction.DeleteSet.IterateDeletedStructs(transaction, i =>
            {
                if (i is Item item && _scope.Any(type => IsParentOf(type, item)))
                {
                    item.KeepItemAndParents(true);
                }

                return true;
            });

            StackItemAdded?.Invoke(this, new StackEventArgs(stack.Peek(), undoing ? OperationType.Redo : OperationType.Undo, transaction.ChangedParentTypes, transaction.Origin));
        }

        private StackItem PopStackItem(Stack<StackItem> stack, OperationType eventType)
        {
            StackItem result = null;

            // Keep a reference to the transaction so we can fire the event with the 'changedParentTypes'.
            Transaction tr = null;

            _doc.Transact(transaction =>
            {
                tr = transaction;

                while (stack.Count > 0 && result == null)
                {
                    var stackItem = stack.Pop();
                    var itemsToRedo = new HashSet<Item>();
                    var itemsToDelete = new List<Item>();
                    var performedChange = false;

                    foreach (var kvp in stackItem.AfterState)
                    {
                        var client = kvp.Key;
                        var endClock = kvp.Value;

                        if (!stackItem.BeforeState.TryGetValue(client, out var startClock))
                        {
                            startClock = 0;
                        }

                        var len = endClock - startClock;
                        var structs = _doc.Store.Clients[client];

                        if (startClock != endClock)
                        {
                            // Make sure structs don't overlap with the range of created operations [stackItem.start, stackItem.start + stackItem.end).
                            // This must be executed before deleted structs are iterated.
                            _doc.Store.GetItemCleanStart(transaction, new ID(client, startClock));

                            if (endClock < _doc.Store.GetState(client))
                            {
                                _doc.Store.GetItemCleanStart(transaction, new ID(client, endClock));
                            }

                            _doc.Store.IterateStructs(transaction, structs, startClock, len, str =>
                            {
                                if (str is Item it)
                                {
                                    if (it.Redone != null)
                                    {
                                        var redoneResult = _doc.Store.FollowRedone(str.Id);
                                        var diff = redoneResult.diff;
                                        var item = redoneResult.item;

                                        if (diff > 0)
                                        {
                                            item = _doc.Store.GetItemCleanStart(transaction, new ID(item.Id.Client, item.Id.Clock + diff)) as Item;
                                        }

                                        if (item.Length > len)
                                        {
                                            _doc.Store.GetItemCleanStart(transaction, new ID(item.Id.Client, endClock));
                                        }

                                        str = it = item as Item;
                                    }

                                    if (!it.Deleted && _scope.Any(type => IsParentOf(type, it)))
                                    {
                                        itemsToDelete.Add(it);
                                    }
                                }

                                return true;
                            });
                        }
                    }

                    stackItem.DeleteSet.IterateDeletedStructs(transaction, str =>
                    {
                        var id = str.Id;
                        var clock = id.Clock;
                        var client = id.Client;

                        if (!stackItem.BeforeState.TryGetValue(client, out var startClock))
                        {
                            startClock = 0;
                        }

                        if (!stackItem.AfterState.TryGetValue(client, out var endClock))
                        {
                            endClock = 0;
                        }

                        if (str is Item item &&
                            _scope.Any(type => IsParentOf(type, item)) &&
                            // Never redo structs in [stackItem.start, stackItem.start + stackItem.end), because they were created and deleted in the same capture interval.
                            !(clock >= startClock && clock < endClock))
                        {
                            itemsToRedo.Add(item);
                        }

                        return true;
                    });

                    foreach (var str in itemsToRedo)
                    {
                        performedChange |= transaction.RedoItem(str, itemsToRedo) != null;
                    }

                    // We want to delete in reverse order so that children are deleted before
                    // parents, so we have more information available when items are filtered.
                    for (int i = itemsToDelete.Count - 1; i >= 0; i--)
                    {
                        var item = itemsToDelete[i];
                        if (_deleteFilter(item))
                        {
                            item.Delete(transaction);
                            performedChange = true;
                        }
                    }

                    result = stackItem;
                }

                foreach (var kvp in transaction.Changed)
                {
                    var type = kvp.Key;
                    var subProps = kvp.Value;

                    // Destroy search marker if necessary.
                    if (subProps.Contains(null) && type is YArrayBase arr)
                    {
                        arr.ClearSearchMarkers();
                    }
                }
            }, origin: this);

            if (result != null)
            {
                StackItemPopped?.Invoke(this, new StackEventArgs(result, eventType, tr.ChangedParentTypes, tr.Origin));
            }

            return result;
        }

        private static bool IsParentOf(AbstractType parent, Item child)
        {
            while (child != null)
            {
                if (child.Parent == parent)
                {
                    return true;
                }

                child = (child.Parent as AbstractType)._item;
            }

            return false;
        }
    }
}
