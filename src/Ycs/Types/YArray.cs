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
        public YArrayEvent(YArray arr, Transaction transaction)
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

        internal override void Integrate(YDoc doc, Item item)
        {
            base.Integrate(doc, item);
            Insert(0, _prelimContent);
            _prelimContent = null;
        }

        internal override AbstractType Copy()
        {
            return new YArray();
        }

        internal override void Write(IUpdateEncoder encoder)
        {
            encoder.WriteTypeRef(YArrayRefId);
        }

        public static YArray Read(IUpdateDecoder decoder)
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
            var n = _start;

            while (n != null)
            {
                if (n.Countable && !n.Deleted)
                {
                    var c = n.Content.GetContent();
                    cs.AddRange(c);
                }

                n = n.Right as Item;
            }

            return cs;
        }
    }
}
