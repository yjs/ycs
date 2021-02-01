// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Ycs
{
    public class YArrayEvent : YEvent
    {
        internal YArrayEvent(YArray arr, Transaction transaction)
            : base(arr, transaction)
        {
            // Do nothing.
        }
    }

    public class YArray : YArrayBase
    {
        public const byte YArrayRefId = 0;

        private List<object> _prelimContent;

        public YArray()
            : this(null)
        {
            // Do nothing.
        }

        public YArray(IEnumerable<object> prelimContent = null)
        {
            _prelimContent = prelimContent != null ? new List<object>(prelimContent) : new List<object>();
        }

        public override int Length => _prelimContent?.Count ?? base.Length;

        public YArray Clone() => InternalClone() as YArray;

        internal override void Integrate(YDoc doc, Item item)
        {
            base.Integrate(doc, item);
            Insert(0, _prelimContent);
            _prelimContent = null;
        }

        internal override AbstractType InternalCopy()
        {
            return new YArray();
        }

        internal override AbstractType InternalClone()
        {
            var arr = new YArray();

            foreach (var item in EnumerateList())
            {
                if (item is AbstractType at)
                {
                    arr.Add(new[] { at.InternalClone() });
                }
                else
                {
                    arr.Add(new[] { item });
                }
            }

            return arr;
        }

        internal override void Write(IUpdateEncoder encoder)
        {
            encoder.WriteTypeRef(YArrayRefId);
        }

        internal static YArray Read(IUpdateDecoder decoder)
        {
            return new YArray();
        }

        /// <summary>
        /// Creates YArrayEvent and calls observers.
        /// </summary>
        internal override void CallObserver(Transaction transaction, ISet<string> parentSubs)
        {
            base.CallObserver(transaction, parentSubs);
            CallTypeObservers(transaction, new YArrayEvent(this, transaction));
        }

        /// <summary>
        /// Inserts new content at an index.
        /// </summary>
        public void Insert(int index, ICollection<object> content)
        {
            if (Doc != null)
            {
                Doc.Transact((tr) =>
                {
                    InsertGenerics(tr, index, content);
                });
            }
            else
            {
                _prelimContent.InsertRange(index, content);
            }
        }

        public void Add(ICollection<object> content)
        {
            Insert(Length, content);
        }

        public void Unshift(ICollection<object> content)
        {
            Insert(0, content);
        }

        public void Delete(int index, int length = 1)
        {
            if (Doc != null)
            {
                Doc.Transact((tr) =>
                {
                    Delete(tr, index, length);
                });
            }
            else
            {
                _prelimContent.RemoveRange(index, length);
            }
        }

        public IReadOnlyList<object> Slice(int start = 0) => InternalSlice(start, Length);

        public IReadOnlyList<object> Slice(int start, int end) => InternalSlice(start, end);

        public object Get(int index)
        {
            var marker = FindMarker(index);
            var n = _start;

            if (marker != null)
            {
                n = marker.P;
                index -= marker.Index;
            }

            for (; n != null; n = n.Right as Item)
            {
                if (!n.Deleted && n.Countable)
                {
                    if (index < n.Length)
                    {
                        return n.Content.GetContent()[index];
                    }

                    index -= n.Length;
                }
            }

            return default;
        }

        public IList<object> ToArray()
        {
            var cs = new List<object>();
            cs.AddRange(EnumerateList());
            return cs;
        }

        private IEnumerable<object> EnumerateList()
        {
            var n = _start;

            while (n != null)
            {
                if (n.Countable && !n.Deleted)
                {
                    var c = n.Content.GetContent();
                    foreach (var item in c)
                    {
                        yield return item;
                    }
                }

                n = n.Right as Item;
            }
        }
    }
}
