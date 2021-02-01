// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Ycs
{
    public abstract class YArrayBase : AbstractType, IEnumerable<object>
    {
        protected sealed class ArraySearchMarker
        {
            // Assigned to '-1', so the first timestamp is '0'.
            internal static long _globalSearchMarkerTimestamp = -1;

            public ArraySearchMarker(Item p, int index)
            {
                P = p;
                Index = index;
                P.Marker = true;

                RefreshTimestamp();
            }

            public Item P { get; internal set; }
            public int Index { get; internal set; }
            public long Timestamp { get; internal set; }

            public void RefreshTimestamp()
            {
                Timestamp = Interlocked.Increment(ref _globalSearchMarkerTimestamp);
            }

            public void Update(Item p, int index)
            {
                P.Marker = false;

                P = p;
                P.Marker = true;
                Index = index;

                RefreshTimestamp();
            }
        }

        protected sealed class ArraySearchMarkerCollection
        {
            internal readonly List<ArraySearchMarker> _searchMarkers = new List<ArraySearchMarker>();

            public int Count => _searchMarkers.Count;

            public void Clear()
            {
                _searchMarkers.Clear();
            }

            public ArraySearchMarker MarkPosition(Item p, int index)
            {
                if (_searchMarkers.Count >= MaxSearchMarkers)
                {
                    // Override oldest marker (we don't want to create more objects).
                    var marker = _searchMarkers.Aggregate((a, b) => a.Timestamp < b.Timestamp ? a : b);
                    marker.Update(p, index);
                    return marker;
                }
                else
                {
                    // Create a new marker.
                    var pm = new ArraySearchMarker(p, index);
                    _searchMarkers.Add(pm);
                    return pm;
                }
            }

            /// <summary>
            /// Update markers when a change happened.
            /// This should be called before doing a deletion!
            /// </summary>
            public void UpdateMarkerChanges(int index, int len)
            {
                for (int i = _searchMarkers.Count - 1; i >= 0; i--)
                {
                    var m = _searchMarkers[i];

                    if (len > 0)
                    {
                        var p = m.P;
                        p.Marker = false;

                        // Ideally we just want to do a simple position comparison, but this will only work if
                        // search markers don't point to deleted items for formats.
                        // Iterate marker to prev undeleted countable position so we know what to do when updating a position.
                        while (p != null && (p.Deleted || !p.Countable))
                        {
                            Debug.Assert(p.Left != p);
                            p = p.Left as Item;
                            if (p != null && !p.Deleted && p.Countable)
                            {
                                // Adjust position. The loop should break now.
                                m.Index -= p.Length;
                            }
                        }

                        if (p == null || p.Marker)
                        {
                            // Remove search marker if updated position is null or if position is already marked.
                            _searchMarkers.RemoveAt(i);
                            continue;
                        }

                        m.P = p;
                        p.Marker = true;
                    }

                    // A simple index <= m.Index check would actually suffice.
                    if (index < m.Index || (len > 0 && index == m.Index))
                    {
                        m.Index = Math.Max(index, m.Index + len);
                    }
                }
            }
        }

        private const int MaxSearchMarkers = 80;

        protected readonly ArraySearchMarkerCollection _searchMarkers;

        protected YArrayBase()
        {
            _searchMarkers = new ArraySearchMarkerCollection();
        }

        public IEnumerator<object> GetEnumerator()
        {
            return EnumerateContent().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return EnumerateContent().GetEnumerator();
        }

        internal void ClearSearchMarkers()
        {
            _searchMarkers.Clear();
        }

        /// <summary>
        /// Creates YArrayEvent and calls observers.
        /// </summary>
        internal override void CallObserver(Transaction transaction, ISet<string> parentSubs)
        {
            if (!transaction.Local)
            {
                _searchMarkers.Clear();
            }
        }

        protected void InsertGenerics(Transaction transaction, int index, ICollection<object> content)
        {
            if (index == 0)
            {
                if (_searchMarkers.Count > 0)
                {
                    _searchMarkers.UpdateMarkerChanges(index, content.Count);
                }

                InsertGenericsAfter(transaction, null, content);
                return;
            }

            int startIndex = index;
            var marker = FindMarker(index);
            var n = _start;

            if (marker != null)
            {
                n = marker.P;
                index -= marker.Index;

                // We need to iterate one to the left so that the algorithm works.
                if (index == 0)
                {
                    // @todo: refactor this as it actually doesn't consider formats.
                    n = n.Prev as Item;
                    index += n != null && n.Countable && !n.Deleted ? n.Length : 0;
                }
            }

            for (; n != null; n = n.Right as Item)
            {
                if (!n.Deleted && n.Countable)
                {
                    if (index <= n.Length)
                    {
                        if (index < n.Length)
                        {
                            // insert in-between
                            transaction.Doc.Store.GetItemCleanStart(transaction, new ID(n.Id.Client, n.Id.Clock + index));
                        }

                        break;
                    }

                    index -= n.Length;
                }
            }

            if (_searchMarkers.Count > 0)
            {
                _searchMarkers.UpdateMarkerChanges(startIndex, content.Count);
            }

            InsertGenericsAfter(transaction, n, content);
        }

        protected void InsertGenericsAfter(Transaction transaction, Item referenceItem, ICollection<object> content)
        {
            var left = referenceItem;
            var doc = transaction.Doc;
            var ownClientId = doc.ClientId;
            var store = doc.Store;
            var right = referenceItem == null ? _start : referenceItem.Right as Item;

            var jsonContent = new List<object>();

            void packJsonContent()
            {
                if (jsonContent.Count > 0)
                {
                    left = new Item(new ID(ownClientId, store.GetState(ownClientId)), left, left?.LastId, right, right?.Id, this, null, new ContentAny(jsonContent.ToList()));
                    left.Integrate(transaction, 0);
                    jsonContent.Clear();
                }
            }

            foreach (var c in content)
            {
                switch (c)
                {
                    case byte[] arr:
                        packJsonContent();
                        left = new Item(new ID(ownClientId, store.GetState(ownClientId)), left, left?.LastId, right, right?.Id, this, null, new ContentBinary(arr));
                        left.Integrate(transaction, 0);
                        break;
                    case YDoc d:
                        packJsonContent();
                        left = new Item(new ID(ownClientId, store.GetState(ownClientId)), left, left?.LastId, right, right?.Id, this, null, new ContentDoc(d));
                        left.Integrate(transaction, 0);
                        break;
                    case AbstractType at:
                        packJsonContent();
                        left = new Item(new ID(ownClientId, store.GetState(ownClientId)), left, left?.LastId, right, right?.Id, this, null, new ContentType(at));
                        left.Integrate(transaction, 0);
                        break;
                    default:
                        jsonContent.Add(c);
                        break;
                }
            }

            packJsonContent();
        }

        protected void Delete(Transaction transaction, int index, int length)
        {
            if (length == 0)
            {
                return;
            }

            int startIndex = index;
            int startLength = length;
            var marker = FindMarker(index);
            var n = _start;

            if (marker != null)
            {
                n = marker.P;
                index -= marker.Index;
            }

            // Compute the first item to be deleted.
            for (; n != null && index > 0; n = n.Right as Item)
            {
                if (!n.Deleted && n.Countable)
                {
                    if (index < n.Length)
                    {
                        transaction.Doc.Store.GetItemCleanStart(transaction, new ID(n.Id.Client, n.Id.Clock + index));
                    }

                    index -= n.Length;
                }
            }

            // Delete all items until done.
            while (length > 0 && n != null)
            {
                if (!n.Deleted)
                {
                    if (length < n.Length)
                    {
                        transaction.Doc.Store.GetItemCleanStart(transaction, new ID(n.Id.Client, n.Id.Clock + length));
                    }

                    n.Delete(transaction);
                    length -= n.Length;
                }

                n = n.Right as Item;
            }

            if (length > 0)
            {
                throw new Exception("Array length exceeded");
            }

            if (_searchMarkers.Count > 0)
            {
                _searchMarkers.UpdateMarkerChanges(startIndex, -startLength + length /* in case we remove the above exception */);
            }
        }

        protected IReadOnlyList<object> InternalSlice(int start, int end)
        {
            if (start < 0)
            {
                start += Length;
            }

            if (end < 0)
            {
                end += Length;
            }

            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            if (end < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(end));
            }

            if (start > end)
            {
                throw new ArgumentOutOfRangeException(nameof(end));
            }

            int length = end - start;
            Debug.Assert(length >= 0);

            var cs = new List<object>();
            var n = _start;

            while (n != null && length > 0)
            {
                if (n.Countable && !n.Deleted)
                {
                    var c = n.Content.GetContent();
                    if (c.Count <= start)
                    {
                        start -= c.Count;
                    }
                    else
                    {
                        for (int i = start; i < c.Count && length > 0; i++)
                        {
                            cs.Add(c[i]);
                            length--;
                        }

                        start = 0;
                    }
                }

                n = n.Right as Item;
            }

            return cs.AsReadOnly();
        }

        protected void ForEach(Action<object, int, YArrayBase> fun)
        {
            int index = 0;
            var n = _start;

            while (n != null)
            {
                if (n.Countable && !n.Deleted)
                {
                    var c = n.Content.GetContent();
                    foreach (var cItem in c)
                    {
                        fun(cItem, index++, this);
                    }
                }

                n = n.Right as Item;
            }
        }

        protected void ForEachSnapshot(Action<object, int, YArrayBase> fun, Snapshot snapshot)
        {
            int index = 0;
            var n = _start;

            while (n != null)
            {
                if (n.Countable && n.IsVisible(snapshot))
                {
                    var c = n.Content.GetContent();
                    foreach (var value in c)
                    {
                        fun(value, index++, this);
                    }
                }

                n = n.Right as Item;
            }
        }

        protected IEnumerable<object> EnumerateContent()
        {
            var n = _start;
            while (n != null)
            {
                while (n != null && n.Deleted)
                {
                    n = n.Right as Item;
                }

                // Check if we reached the end, no need to check currentContent, because it does not exist.
                if (n == null)
                {
                    yield break;
                }

                var currentContent = n.Content.GetContent();
                foreach (var c in currentContent)
                {
                    yield return c;
                }

                // We used content of n, now iterate to next.
                n = n.Right as Item;
            }
        }

        /// <summary>
        /// Search markers help us to find positions in the associative array faster.
        /// <br/>
        /// They speed up the process of finding a position without much bookkeeping.
        /// <br/>
        /// A maximum of 'MaxSearchMarker' objects are created.
        /// <br/>
        /// This function always returns a refreshed marker (updated timestamp).
        /// </summary>
        protected ArraySearchMarker FindMarker(int index)
        {
            if (_start == null || index == 0 || _searchMarkers == null || _searchMarkers.Count == 0)
            {
                return null;
            }

            var marker = _searchMarkers.Count == 0 ? null : _searchMarkers._searchMarkers.Aggregate((a, b) => Math.Abs(index - a.Index) < Math.Abs(index - b.Index) ? a : b);
            var p = _start;
            int pIndex = 0;

            if (marker != null)
            {
                p = marker.P;
                pIndex = marker.Index;

                // We used it, we might need to use it again.
                marker.RefreshTimestamp();
            }

            // Iterate to right if possible.
            while (p.Right != null && pIndex < index)
            {
                if (!p.Deleted && p.Countable)
                {
                    if (index < pIndex + p.Length)
                    {
                        break;
                    }

                    pIndex += p.Length;
                }

                p = p.Right as Item;
            }

            // Iterate to left if necessary (might be that pIndex > index).
            while (p.Left != null && pIndex > index)
            {
                p = p.Left as Item;
                if (p == null)
                {
                    break;
                }
                else if (!p.Deleted && p.Countable)
                {
                    pIndex -= p.Length;
                }
            }

            // We want to make sure that p can't be merged with left, because that would screw up everything.
            // In that case just return what we have (it is most likely the best marker anyway).
            // Iterate to left until p can't be merged with left.
            while (p.Left != null && p.Left.Id.Client == p.Id.Client && p.Left.Id.Clock + p.Left.Length == p.Id.Clock)
            {
                p = p.Left as Item;
                if (p == null)
                {
                    break;
                }
                else if (!p.Deleted && p.Countable)
                {
                    pIndex -= p.Length;
                }
            }

            if (marker != null && Math.Abs(marker.Index - pIndex) < ((p.Parent as AbstractType).Length / MaxSearchMarkers))
            {
                // Adjust existing marker.
                marker.Update(p, pIndex);
                return marker;
            }
            else
            {
                // Create a new marker.
                return _searchMarkers.MarkPosition(p, pIndex);
            }
        }
    }
}
