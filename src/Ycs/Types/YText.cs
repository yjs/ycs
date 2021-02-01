// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace Ycs
{
    /// <summary>
    /// Event that describes the changes on a YText type.
    /// </summary>
    public class YTextEvent : YEvent
    {
        private enum ChangeType
        {
            Insert,
            Delete,
            Retain
        }

        private readonly ISet<string> _subs;
        private IList<Delta> _delta = null;

        internal YTextEvent(YText arr, Transaction transaction, ISet<string> subs)
            : base(arr, transaction)
        {
            _subs = subs;
            KeysChanged = new HashSet<string>();

            if (_subs?.Count > 0)
            {
                foreach (var sub in _subs)
                {
                    if (sub == null)
                    {
                        ChildListChanged = true;
                    }
                    else
                    {
                        KeysChanged.Add(sub);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the changed attribute names.
        /// </summary>
        public ISet<string> KeysChanged { get; }

        /// <summary>
        /// Gets whether the children keys changed.
        /// </summary>
        public bool ChildListChanged { get; }

        /// <summary>
        /// Compute the changes in the delta format.
        /// </summary>
        public IList<Delta> Delta
        {
            get
            {
                if (_delta == null)
                {
                    var doc = Target.Doc;
                    _delta = new List<Delta>();

                    doc.Transact(transaction =>
                    {
                        var delta = _delta;

                        // Saves all current attributes for insert.
                        var currentAttributes = new Dictionary<string, object>();
                        var oldAttributes = new Dictionary<string, object>();
                        var item = Target._start;
                        ChangeType? action = null;
                        var attributes = new Dictionary<string, object>();

                        object insert = string.Empty;
                        int retain = 0;
                        int deleteLen = 0;

                        void addOp()
                        {
                            if (action != null)
                            {
                                Delta op;

                                switch (action)
                                {
                                    case ChangeType.Delete:
                                        op = new Delta { Delete = deleteLen };
                                        deleteLen = 0;
                                        break;
                                    case ChangeType.Insert:
                                        op = new Delta { Insert = insert };
                                        if (currentAttributes?.Count > 0)
                                        {
                                            op.Attributes = new Dictionary<string, object>();
                                            foreach (var kvp in currentAttributes)
                                            {
                                                if (kvp.Value != null)
                                                {
                                                    op.Attributes[kvp.Key] = kvp.Value;
                                                }
                                            }
                                        }
                                        break;
                                    case ChangeType.Retain:
                                        op = new Delta { Retain = retain };
                                        if (attributes.Count > 0)
                                        {
                                            op.Attributes = new Dictionary<string, object>();
                                            foreach (var kvp in attributes)
                                            {
                                                op.Attributes[kvp.Key] = attributes[kvp.Key];
                                            }
                                        }
                                        retain = 0;
                                        break;
                                    default:
                                        throw new InvalidOperationException($"Unexpected action: {action}");
                                }

                                delta.Add(op);
                                action = null;
                            }
                        }

                        while (item != null)
                        {
                            switch (item.Content)
                            {
                                case ContentEmbed _:
                                    if (Adds(item))
                                    {
                                        if (!Deletes(item))
                                        {
                                            addOp();
                                            action = ChangeType.Insert;
                                            insert = (item.Content as ContentEmbed).Embed;
                                            addOp();
                                        }
                                    }
                                    else if (Deletes(item))
                                    {
                                        if (action != ChangeType.Delete)
                                        {
                                            addOp();
                                            action = ChangeType.Delete;
                                        }

                                        deleteLen++;
                                    }
                                    else if (!item.Deleted)
                                    {
                                        if (action != ChangeType.Retain)
                                        {
                                            addOp();
                                            action = ChangeType.Retain;
                                        }

                                        retain++;
                                    }
                                    break;
                                case ContentString _:
                                    if (Adds(item))
                                    {
                                        if (!Deletes(item))
                                        {
                                            if (action != ChangeType.Insert)
                                            {
                                                addOp();
                                                action = ChangeType.Insert;
                                            }

                                            insert += (item.Content as ContentString).GetString();
                                        }
                                    }
                                    else if (Deletes(item))
                                    {
                                        if (action != ChangeType.Delete)
                                        {
                                            addOp();
                                            action = ChangeType.Delete;
                                        }

                                        deleteLen += item.Length;
                                    }
                                    else if (!item.Deleted)
                                    {
                                        if (action != ChangeType.Retain)
                                        {
                                            addOp();
                                            action = ChangeType.Retain;
                                        }

                                        retain += item.Length;
                                    }
                                    break;
                                case ContentFormat cf:
                                    if (Adds(item))
                                    {
                                        if (!Deletes(item))
                                        {
                                            if (!currentAttributes.TryGetValue(cf.Key, out var curVal))
                                            {
                                                curVal = null;
                                            }

                                            if (!YText.EqualAttrs(curVal, cf.Value))
                                            {
                                                if (action == ChangeType.Retain)
                                                {
                                                    addOp();
                                                }

                                                if (!oldAttributes.TryGetValue(cf.Key, out var oldVal))
                                                {
                                                    oldVal = null;
                                                }

                                                if (YText.EqualAttrs(cf.Value, oldVal))
                                                {
                                                    attributes.Remove(cf.Key);
                                                }
                                                else
                                                {
                                                    attributes[cf.Key] = cf.Value;
                                                }
                                            }
                                            else
                                            {
                                                item.Delete(Transaction);
                                            }
                                        }
                                    }
                                    else if (Deletes(item))
                                    {
                                        oldAttributes[cf.Key] = cf.Value;

                                        if (!currentAttributes.TryGetValue(cf.Key, out var curVal))
                                        {
                                            curVal = null;
                                        }

                                        if (!YText.EqualAttrs(curVal, cf.Value))
                                        {
                                            if (action == ChangeType.Retain)
                                            {
                                                addOp();
                                            }

                                            attributes[cf.Key] = curVal;
                                        }
                                    }
                                    else if (!item.Deleted)
                                    {
                                        oldAttributes[cf.Key] = cf.Value;

                                        if (attributes.TryGetValue(cf.Key, out var attr))
                                        {
                                            if (!YText.EqualAttrs(attr, cf.Value))
                                            {
                                                if (action == ChangeType.Retain)
                                                {
                                                    addOp();
                                                }

                                                if (cf.Value == null)
                                                {
                                                    attributes[cf.Key] = null;
                                                }
                                                else
                                                {
                                                    attributes.Remove(cf.Key);
                                                }
                                            }
                                            else
                                            {
                                                item.Delete(Transaction);
                                            }
                                        }
                                    }

                                    if (!item.Deleted)
                                    {
                                        if (action == ChangeType.Insert)
                                        {
                                            addOp();
                                        }

                                        YText.UpdateCurrentAttributes(currentAttributes, item.Content as ContentFormat);
                                    }
                                    break;
                            }

                            item = item.Right as Item;
                        }

                        addOp();

                        while (delta.Count > 0)
                        {
                            var lastOp = delta[delta.Count - 1];
                            if (lastOp.Retain != null && lastOp.Attributes != null)
                            {
                                // Retain delta's if they don't assign attributes.
                                delta.RemoveAt(delta.Count - 1);
                            }
                            else
                            {
                                break;
                            }
                        }
                    });
                }

                return _delta;
            }
        }
    }

    /// <summary>
    /// Type that represents text with formatting information.
    /// </summary>
    public class YText : YArrayBase
    {
        private class ItemTextListPosition
        {
            public Item Left;
            public Item Right;
            public int Index;
            public IDictionary<string, object> CurrentAttributes;

            public ItemTextListPosition(Item left, Item right, int index, IDictionary<string, object> currentAttributes)
            {
                Left = left;
                Right = right;
                Index = index;
                CurrentAttributes = currentAttributes;
            }

            public void Forward()
            {
                if (Right == null)
                {
                    throw new Exception("Unexpected");
                }

                switch (Right.Content)
                {
                    case ContentEmbed _:
                    case ContentString _:
                        if (!Right.Deleted)
                        {
                            Index += Right.Length;
                        }
                        break;
                    case ContentFormat cf:
                        if (!Right.Deleted)
                        {
                            UpdateCurrentAttributes(CurrentAttributes, cf);
                        }
                        break;
                }

                Left = Right;
                Right = Right.Right as Item;
            }

            public void FindNextPosition(Transaction transaction, int count)
            {
                while (Right != null && count > 0)
                {
                    switch (Right.Content)
                    {
                        case ContentEmbed _:
                        case ContentString _:
                            if (!Right.Deleted)
                            {
                                if (count < Right.Length)
                                {
                                    // Split right.
                                    transaction.Doc.Store.GetItemCleanStart(transaction, new ID(Right.Id.Client, Right.Id.Clock + count));
                                }

                                Index += Right.Length;
                                count -= Right.Length;
                            }
                            break;
                        case ContentFormat cf:
                            if (!Right.Deleted)
                            {
                                UpdateCurrentAttributes(CurrentAttributes, cf);
                            }
                            break;
                    }

                    Left = Right;
                    Right = Right.Right as Item;
                    // We don't forward() because that would halve the performance because we already do the checks above.
                }
            }

            /// <summary>
            /// Negate applied formats.
            /// </summary>
            public void InsertNegatedAttributes(Transaction transaction, AbstractType parent, IDictionary<string, object> negatedAttributes)
            {
                // Check if we really need to remove attributes.
                while (
                    Right != null && (
                        Right.Deleted || (
                            Right.Content is ContentFormat cf &&
                            negatedAttributes.ContainsKey(cf.Key) &&
                            EqualAttrs(negatedAttributes[cf.Key], cf.Value)
                        )
                    )
                )
                {
                    if (!Right.Deleted)
                    {
                        negatedAttributes.Remove((Right.Content as ContentFormat).Key);
                    }

                    Forward();
                }

                var doc = transaction.Doc;
                var ownClientId = doc.ClientId;
                var left = Left;
                var right = Right;

                foreach (var kvp in negatedAttributes)
                {
                    left = new Item(new ID(ownClientId, doc.Store.GetState(ownClientId)), left, left?.LastId, right, right?.Id, parent, null, new ContentFormat(kvp.Key, kvp.Value));
                    left.Integrate(transaction, 0);

                    CurrentAttributes[kvp.Key] = kvp.Value;
                    UpdateCurrentAttributes(CurrentAttributes, left.Content as ContentFormat);
                }
            }

            public void MinimizeAttributeChanges(IDictionary<string, object> attributes)
            {
                // Go right while attributes[right.Key] == right.Value (or right is deleted).
                while (Right != null)
                {
                    if (Right.Deleted || (Right.Content is ContentFormat cf && EqualAttrs(attributes.TryGetValue(cf.Key, out var val) ? val : null, cf.Value)))
                    {
                        Forward();
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        public enum YTextChangeType
        {
            Added,
            Removed
        }

        public class YTextChangeAttributes
        {
            public YTextChangeType Type { get; set; }
            public int User { get; set; }
            public YTextChangeType State { get; set; }
        }

        // TODO: [alekseyk] To util class? Might not be needed here.
        internal const int YTextRefId = 2;
        internal const string ChangeKey = "ychange";

        private IList<Action> _pending;

        public YText()
            : this(null)
        {
            // Do nothing.
        }

        public YText(string str)
        {
            _pending = str != null
                ? new List<Action>() { () => Insert(0, str) }
                : new List<Action>(0);
        }

        public void ApplyDelta(IList<Delta> delta, bool sanitize = true)
        {
            if (Doc != null)
            {
                Doc.Transact(tr =>
                {
                    var curPos = new ItemTextListPosition(null, _start, 0, new Dictionary<string, object>());
                    for (int i = 0; i < delta.Count; i++)
                    {
                        var op = delta[i];

                        if (op.Insert != null)
                        {
                            // Quill assumes that the content starts with an empty paragraph.
                            // Yjs/Y.Text assumes that it starts empty. We always hide that
                            // there is a newline at the end of the content.
                            // If we omit this step, clients will see a different number of paragraphs,
                            // but nothing bad will happen.
                            var insertStr = op.Insert as string;
                            var ins = (!sanitize && insertStr != null && i == delta.Count - 1 && curPos.Right == null && insertStr.EndsWith("\n")) ? insertStr.Substring(0, insertStr.Length - 1) : op.Insert;
                            if (!(ins is string) || ((string)ins).Length > 0)
                            {
                                // TODO: Null attributes by default to avoid unnecessary allocations?
                                InsertText(tr, curPos, ins, op.Attributes ?? new Dictionary<string, object>());
                            }
                        }
                        else if (op.Retain != null)
                        {
                            FormatText(tr, curPos, op.Retain.Value, op.Attributes ?? new Dictionary<string, object>());
                        }
                        else if (op.Delete != null)
                        {
                            DeleteText(tr, curPos, op.Delete.Value);
                        }
                    }
                });
            }
            else
            {
                _pending.Add(() => ApplyDelta(delta, sanitize));
            }
        }

        public IList<Delta> ToDelta(Snapshot snapshot = null, Snapshot prevSnapshot = null, Func<YTextChangeType, ID, YTextChangeAttributes> computeYChange = null)
        {
            var ops = new List<Delta>();
            var currentAttributes = new Dictionary<string, object>();
            var doc = Doc;
            var str = string.Empty;

            var n = _start;

            void packStr()
            {
                if (str.Length > 0)
                {
                    // Pack str with attributes to ops.
                    var attributes = new Dictionary<string, object>();
                    var addAttributes = false;

                    foreach (var kvp in currentAttributes)
                    {
                        addAttributes = true;
                        attributes[kvp.Key] = kvp.Value;
                    }

                    var op = new Delta { Insert = str };
                    if (addAttributes)
                    {
                        op.Attributes = attributes;
                    }

                    ops.Add(op);
                    str = string.Empty;
                }
            }

            // Snapshots are merged again after the transaction, so we need to keep the
            // transaction alive until we are done.
            doc.Transact(tr =>
            {
                if (snapshot != null)
                {
                    Transaction.SplitSnapshotAffectedStructs(tr, snapshot);
                }

                if (prevSnapshot != null)
                {
                    Transaction.SplitSnapshotAffectedStructs(tr, prevSnapshot);
                }

                while (n != null)
                {
                    if (n.IsVisible(snapshot) || (prevSnapshot != null && n.IsVisible(prevSnapshot)))
                    {
                        switch (n.Content)
                        {
                            case ContentString cs:
                                if (!currentAttributes.TryGetValue(ChangeKey, out var val))
                                {
                                    val = null;
                                }

                                var cur = val as YTextChangeAttributes;

                                if (snapshot != null && !n.IsVisible(snapshot))
                                {
                                    if (cur == null || cur.User != n.Id.Client || cur.State != YTextChangeType.Removed)
                                    {
                                        packStr();
                                        currentAttributes[ChangeKey] = computeYChange != null ? computeYChange(YTextChangeType.Removed, n.Id) : new YTextChangeAttributes { Type = YTextChangeType.Removed };
                                    }
                                }
                                else if (prevSnapshot != null && !n.IsVisible(prevSnapshot))
                                {
                                    if (cur == null || cur.User != n.Id.Client || cur.State != YTextChangeType.Added)
                                    {
                                        packStr();
                                        currentAttributes[ChangeKey] = computeYChange != null ? computeYChange(YTextChangeType.Added, n.Id) : new YTextChangeAttributes { Type = YTextChangeType.Added };
                                    }
                                }
                                else if (cur != null)
                                {
                                    packStr();
                                    currentAttributes.Remove(ChangeKey);
                                }

                                str += cs.GetString();
                                break;
                            case ContentEmbed ce:
                                packStr();
                                var op = new Delta { Insert = ce.Embed };
                                if (currentAttributes.Count > 0)
                                {
                                    var attrs = new Dictionary<string, object>();
                                    op.Attributes = attrs;
                                    foreach (var kvp in currentAttributes)
                                    {
                                        attrs[kvp.Key] = kvp.Value;
                                    }
                                }
                                ops.Add(op);
                                break;
                            case ContentFormat cf:
                                if (n.IsVisible(snapshot))
                                {
                                    packStr();
                                    UpdateCurrentAttributes(currentAttributes, cf);
                                }
                                break;
                        }
                    }

                    n = n.Right as Item;
                }

                packStr();
            }, "splitSnapshotAffectedStructs");

            return ops;
        }

        public void Insert(int index, string text, IDictionary<string, object> attributes = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var doc = Doc;
            if (doc != null)
            {
                doc.Transact(tr =>
                {
                    var pos = FindPosition(tr, index);
                    if (attributes == null)
                    {
                        attributes = new Dictionary<string, object>();
                        foreach (var kvp in pos.CurrentAttributes)
                        {
                            attributes[kvp.Key] = kvp.Value;
                        }
                    }

                    InsertText(tr, pos, text, attributes);
                });
            }
            else
            {
                _pending.Add(() => Insert(index, text, attributes));
            }
        }

        public void InsertEmbed(int index, object embed, IDictionary<string, object> attributes = null)
        {
            attributes = attributes ?? new Dictionary<string, object>();

            if (Doc != null)
            {
                Doc.Transact(tr =>
                {
                    var pos = FindPosition(tr, index);
                    InsertText(tr, pos, embed, attributes);
                });
            }
            else
            {
                _pending.Add(() => InsertEmbed(index, embed, attributes));
            }
        }

        public void Delete(int index, int length)
        {
            if (length == 0)
            {
                return;
            }

            if (Doc != null)
            {
                Doc.Transact(tr =>
                {
                    var pos = FindPosition(tr, index);
                    DeleteText(tr, pos, length);
                });
            }
            else
            {
                _pending.Add(() => Delete(index, length));
            }
        }

        public void Format(int index, int length, IDictionary<string, object> attributes)
        {
            if (length == 0)
            {
                return;
            }

            if (Doc != null)
            {
                Doc.Transact(tr =>
                {
                    var pos = FindPosition(tr, index);
                    if (pos.Right == null)
                    {
                        return;
                    }

                    FormatText(tr, pos, length, attributes);
                });
            }
            else
            {
                _pending.Add(() => Format(index, length, attributes));
            }
        }

        public override string ToString()
        {
            // TODO: [aleskeyk] Can use cached builder.
            var sb = new StringBuilder(Length);

            var n = _start;
            while (n != null)
            {
                if (!n.Deleted && n.Countable && n.Content is ContentString cs)
                {
                    cs.AppendToBuilder(sb);
                }

                n = n.Right as Item;
            }

            return sb.ToString();
        }

        public YText Clone() => InternalClone() as YText;

        public void RemoveAttribute(string name)
        {
            if (Doc != null)
            {
                Doc.Transact(tr =>
                {
                    TypeMapDelete(tr, name);
                });
            }
            else
            {
                _pending.Add(() => RemoveAttribute(name));
            }
        }

        public void SetAttribute(string name, object value)
        {
            if (Doc != null)
            {
                Doc.Transact(tr =>
                {
                    TypeMapSet(tr, name, value);
                });
            }
            else
            {
                _pending.Add(() => SetAttribute(name, value));
            }
        }

        public object GetAttribute(string name) => TryTypeMapGet(name, out var value) ? value : null;

        public IEnumerable<KeyValuePair<string, object>> GetAttributes() => TypeMapEnumerateValues();

        internal override void Integrate(YDoc doc, Item item)
        {
            base.Integrate(doc, item);

            foreach (var c in _pending)
            {
                c();
            }

            _pending = null;
        }

        internal override AbstractType InternalClone()
        {
            var text = new YText();
            text.ApplyDelta(ToDelta());
            return text;
        }

        internal override void CallObserver(Transaction transaction, ISet<string> parentSubs)
        {
            base.CallObserver(transaction, parentSubs);

            var evt = new YTextEvent(this, transaction, parentSubs);
            var doc = transaction.Doc;

            // If a remote change happened, we try to cleanup potential formatting duplicates.
            if (!transaction.Local)
            {
                // Check if another formatting item was inserted.
                var foundFormattingItem = false;
                foreach (var kvp in transaction.AfterState)
                {
                    var client = kvp.Key;
                    var afterClock = kvp.Value;

                    if (!transaction.BeforeState.TryGetValue(kvp.Key, out var clock))
                    {
                        clock = 0;
                    }

                    if (afterClock == clock)
                    {
                        continue;
                    }

                    transaction.Doc.Store.IterateStructs(transaction, doc.Store.Clients[client], clock, afterClock, item =>
                    {
                        if (item is Item it && !it.Deleted && it.Content is ContentFormat)
                        {
                            foundFormattingItem = true;

                            // Stop loop.
                            return false;
                        }

                        return true;
                    });

                    if (foundFormattingItem)
                    {
                        break;
                    }
                }

                if (!foundFormattingItem)
                {
                    transaction.DeleteSet.IterateDeletedStructs(transaction, item =>
                    {
                        var it = item as Item;
                        if (it != null && it.Parent == this && it.Content is ContentFormat)
                        {
                            foundFormattingItem = true;

                            // Don't iterate further.
                            return false;
                        }

                        return true;
                    });
                }

                doc.Transact(tr =>
                {
                    if (foundFormattingItem)
                    {
                        // If a formatting item was inserted, we simply clean the whole type.
                        // We need to compuyte currentAttributes for the current position anyway.
                        CleanupFormatting();
                    }
                    else
                    {
                        // If no formatting attribute was inserted, we can make due with contextless formatting cleanups.
                        // Contextless: it is not necessary to compute currentAttributes for the affected position.
                        tr.DeleteSet.IterateDeletedStructs(tr, item =>
                        {
                            var it = item as Item;
                            if (it != null && it.Parent == this)
                            {
                                CleanupContextlessFormattingGap(tr, it);
                            }

                            return true;
                        });
                    }
                });
            }

            CallTypeObservers(transaction, evt);
        }

        private ItemTextListPosition FindPosition(Transaction transaction, int index)
        {
            var currentAttributes = new Dictionary<string, object>();
            var marker = FindMarker(index);

            if (marker != null)
            {
                var pos = new ItemTextListPosition(marker.P.Left as Item, marker.P, marker.Index, currentAttributes);
                pos.FindNextPosition(transaction, index - marker.Index);
                return pos;
            }
            else
            {
                var pos = new ItemTextListPosition(null, _start, 0, currentAttributes);
                pos.FindNextPosition(transaction, index);
                return pos;
            }
        }

        private IDictionary<string, object> InsertAttributes(Transaction transaction, ItemTextListPosition currPos, IDictionary<string, object> attributes)
        {
            var doc = transaction.Doc;
            var ownClientId = doc.ClientId;
            var negatedAttributes = new Dictionary<string, object>();

            // Insert format-start items.
            foreach (var kvp in attributes)
            {
                var key = kvp.Key;
                var value = kvp.Value;

                if (!currPos.CurrentAttributes.TryGetValue(key, out var currentVal))
                {
                    currentVal = null;
                }

                if (!EqualAttrs(currentVal, value))
                {
                    // Save negated attribute (set null if currentVal is not set).
                    negatedAttributes[key] = currentVal;

                    currPos.Right = new Item(new ID(ownClientId, doc.Store.GetState(ownClientId)), currPos.Left, currPos.Left?.LastId, currPos.Right, currPos.Right?.Id, this, null, new ContentFormat(key, value));
                    currPos.Right.Integrate(transaction, 0);
                    currPos.Forward();
                }
            }

            return negatedAttributes;
        }

        private void InsertText(Transaction transaction, ItemTextListPosition currPos, object text, IDictionary<string, object> attributes)
        {
            attributes = attributes ?? new Dictionary<string, object>();

            foreach (var kvp in currPos.CurrentAttributes)
            {
                if (!attributes.ContainsKey(kvp.Key))
                {
                    attributes[kvp.Key] = null;
                }
            }

            var doc = transaction.Doc;
            var ownClientId = doc.ClientId;

            currPos.MinimizeAttributeChanges(attributes);
            var negatedAttributes = InsertAttributes(transaction, currPos, attributes);

            // Insert content.
            var content = text is string s ? (IContent)new ContentString(s) : new ContentEmbed(text);

            if (_searchMarkers.Count > 0)
            {
                _searchMarkers.UpdateMarkerChanges(currPos.Index, content.Length);
            }

            currPos.Right = new Item(new ID(ownClientId, doc.Store.GetState(ownClientId)), currPos.Left, currPos.Left?.LastId, currPos.Right, currPos.Right?.Id, this, null, content);
            currPos.Right.Integrate(transaction, 0);
            currPos.Forward();

            currPos.InsertNegatedAttributes(transaction, this, negatedAttributes);
        }

        private void FormatText(Transaction transaction, ItemTextListPosition curPos, int length, IDictionary<string, object> attributes)
        {
            var doc = transaction.Doc;
            var ownClientId = doc.ClientId;

            curPos.MinimizeAttributeChanges(attributes);
            var negatedAttributes = InsertAttributes(transaction, curPos, attributes);

            // Iterate until first non-format or null is found.
            // Delete all formats with attributes[format.Key] != null
            while (length > 0 && curPos.Right != null)
            {
                if (!curPos.Right.Deleted)
                {
                    switch (curPos.Right.Content)
                    {
                        case ContentFormat cf:
                            if (attributes.TryGetValue(cf.Key, out var attr))
                            {
                                if (EqualAttrs(attr, cf.Value))
                                {
                                    negatedAttributes.Remove(cf.Key);
                                }
                                else
                                {
                                    negatedAttributes[cf.Key] = cf.Value;
                                }

                                curPos.Right.Delete(transaction);
                            }
                            break;
                        case ContentEmbed _:
                        case ContentString _:
                            if (length < curPos.Right.Length)
                            {
                                transaction.Doc.Store.GetItemCleanStart(transaction, new ID(curPos.Right.Id.Client, curPos.Right.Id.Clock + length));
                            }
                            length -= curPos.Right.Length;
                            break;
                    }
                }

                curPos.Forward();
            }

            // Quill just assumes that the editor starts with a newline and that it always
            // ends with a newline. We only insert that newline when a new newline is
            // inserted - i.e. when length is bigger than type.length.
            if (length > 0)
            {
                var newLines = new string('\n', length - 1);
                curPos.Right = new Item(new ID(ownClientId, doc.Store.GetState(ownClientId)), curPos.Left, curPos.Left?.LastId, curPos.Right, curPos.Right?.Id, this, null, new ContentString(newLines));
                curPos.Right.Integrate(transaction, 0);
                curPos.Forward();
            }

            curPos.InsertNegatedAttributes(transaction, this, negatedAttributes);
        }

        /// <summary>
        /// Call this function after string content has been deleted in order to clean up formatting Items.
        /// </summary>
        private int CleanupFormattingGap(Transaction transaction, Item start, Item end, IDictionary<string, object> startAttributes, IDictionary<string, object> endAttributes)
        {
            while (end != null && !(end.Content is ContentString) && !(end.Content is ContentEmbed))
            {
                if (!end.Deleted && end.Content is ContentFormat cf)
                {
                    UpdateCurrentAttributes(endAttributes, cf);
                }

                end = end.Right as Item;
            }

            int cleanups = 0;
            while (start != end)
            {
                if (!start.Deleted)
                {
                    var content = start.Content;
                    switch (content)
                    {
                        case ContentFormat cf:
                            if (!endAttributes.TryGetValue(cf.Key, out var endVal))
                            {
                                endVal = null;
                            }

                            if (!startAttributes.TryGetValue(cf.Key, out var startVal))
                            {
                                startVal = null;
                            }

                            if ((endVal != cf.Value || !(endVal?.Equals(cf.Value) ?? false) ||
                                (startVal == cf.Value || (startVal?.Equals(cf.Value) ?? false))))
                            {
                                // Either this format is overwritten or it is not necessary because the attribute already existed.
                                start.Delete(transaction);
                                cleanups++;
                            }

                            break;
                    }
                }

                start = start.Right as Item;
            }

            return cleanups;
        }

        private void CleanupContextlessFormattingGap(Transaction transaction, Item item)
        {
            // Iterate until item.Right is null or content.
            while (item != null && item.Right != null && (item.Right.Deleted || (
                !((item.Right as Item).Content is ContentString) && !((item.Right as Item).Content is ContentEmbed)
                )))
            {
                item = item.Right as Item;
            }

            var attrs = new HashSet<object>();

            // Iterate back until a content item is found.
            while (item != null && (item.Deleted || (
                !(item.Content is ContentString) && !(item.Content is ContentEmbed)
                )))
            {
                if (!item.Deleted && item.Content is ContentFormat cf)
                {
                    var key = cf.Key;
                    if (attrs.Contains(key))
                    {
                        item.Delete(transaction);
                    }
                    else
                    {
                        attrs.Add(key);
                    }
                }

                item = item.Left as Item;
            }
        }

        /// <summary>
        /// This function is experimental and subject to change / be removed.
        /// <br/>
        /// Ideally, we don't need this function at all. Formatting attributes should be cleaned up
        /// automatically after each change. This function iterates twice over the complete YText type
        /// and removes unnecessary formatting attributes. This is also helpful for testing.
        /// <br/>
        /// This function won't be exported anymore as soon as there is confidence that the YText type works as intended.
        /// </summary>
        internal int CleanupFormatting()
        {
            int res = 0;

            Doc.Transact(transaction =>
            {
                var start = _start;
                var end = _start;
                var startAttributes = new Dictionary<string, object>();
                var currentAttributes = new Dictionary<string, object>();

                while (end != null)
                {
                    if (!end.Deleted)
                    {
                        switch (end.Content)
                        {
                            case ContentFormat cf:
                                UpdateCurrentAttributes(currentAttributes, cf);
                                break;
                            case ContentEmbed _:
                            case ContentString _:
                                res += CleanupFormattingGap(transaction, start, end, startAttributes, currentAttributes);
                                startAttributes = new Dictionary<string, object>(currentAttributes);
                                start = end;
                                break;
                        }
                    }

                    end = end.Right as Item;
                }
            });

            return res;
        }

        private ItemTextListPosition DeleteText(Transaction transaction, ItemTextListPosition curPos, int length)
        {
            var startLength = length;
            var startAttrs = new Dictionary<string, object>(curPos.CurrentAttributes);
            var start = curPos.Right;

            while (length > 0 && curPos.Right != null)
            {
                if (!curPos.Right.Deleted)
                {
                    switch (curPos.Right.Content)
                    {
                        case ContentEmbed _:
                        case ContentString _:
                            if (length < curPos.Right.Length)
                            {
                                transaction.Doc.Store.GetItemCleanStart(transaction, new ID(curPos.Right.Id.Client, curPos.Right.Id.Clock + length));
                            }
                            length -= curPos.Right.Length;
                            curPos.Right.Delete(transaction);
                            break;
                    }
                }

                curPos.Forward();
            }

            if (start != null)
            {
                CleanupFormattingGap(transaction, start, curPos.Right, startAttrs, new Dictionary<string, object>(curPos.CurrentAttributes));
            }

            var parent = (curPos.Left ?? curPos.Right).Parent as YText;
            if (parent?._searchMarkers?.Count > 0)
            {
                parent._searchMarkers.UpdateMarkerChanges(curPos.Index, -startLength + length);
            }

            return curPos;
        }

        internal override void Write(IUpdateEncoder encoder)
        {
            encoder.WriteTypeRef(YTextRefId);
        }

        internal static YText Read(IUpdateDecoder decoder)
        {
            return new YText();
        }

        internal static bool EqualAttrs(object attr1, object attr2)
        {
            return ReferenceEquals(attr1, attr2) || (attr1?.Equals(attr2) ?? false);
        }

        internal static void UpdateCurrentAttributes(IDictionary<string, object> attributes, ContentFormat format)
        {
            if (format.Value == null)
            {
                attributes.Remove(format.Key);
            }
            else
            {
                attributes[format.Key] = format.Value;
            }
        }
    }
}
