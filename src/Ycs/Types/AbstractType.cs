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
    public class YEventArgs
    {
        internal YEventArgs(YEvent evt, Transaction transaction)
        {
            Event = evt;
            Transaction = transaction;
        }

        public YEvent Event { get; }
        public Transaction Transaction { get; }
    }

    public class YDeepEventArgs
    {
        internal YDeepEventArgs(IList<YEvent> events, Transaction transaction)
        {
            Events = events;
            Transaction = transaction;
        }

        public IList<YEvent> Events { get; }
        public Transaction Transaction { get; }
    }

    public class AbstractType
    {
        internal Item _item = null;
        internal Item _start = null;
        internal IDictionary<string, Item> _map = new Dictionary<string, Item>();

        public event EventHandler<YEventArgs> EventHandler;
        public event EventHandler<YDeepEventArgs> DeepEventHandler;

        public YDoc Doc { get; protected set; }
        public AbstractType Parent => _item != null ? _item.Parent as AbstractType : null;

        public virtual int Length { get; internal set; }

        internal virtual void Integrate(YDoc doc, Item item)
        {
            Doc = doc;
            _item = item;
        }

        internal virtual AbstractType InternalCopy() { throw new NotImplementedException(); }
        internal virtual AbstractType InternalClone() { throw new NotImplementedException(); }

        internal virtual void Write(IUpdateEncoder encoder) { throw new NotImplementedException(); }

        /// <summary>
        /// Call event listeners with an event. This will also add an event to all parents
        /// for observeDeep handlers.
        /// </summary>
        internal virtual void CallTypeObservers(Transaction transaction, YEvent evt)
        {
            var type = this;

            while (true)
            {
                if (!transaction.ChangedParentTypes.TryGetValue(type, out var values))
                {
                    values = new List<YEvent>();
                    transaction.ChangedParentTypes[type] = values;
                }

                values.Add(evt);

                if (type._item == null)
                {
                    break;
                }

                type = type._item.Parent as AbstractType;
            }

            InvokeEventHandlers(evt, transaction);
        }

        /// <summary>
        /// Creates YEvent and calls all type observers.
        /// Must be implemented by each type.
        /// </summary>
        internal virtual void CallObserver(Transaction transaction, ISet<string> parentSubs)
        {
            // Do nothing.
        }

        internal Item _First()
        {
            var n = _start;
            while (n != null && n.Deleted)
            {
                n = n.Right as Item;
            }
            return n;
        }

        internal void InvokeEventHandlers(YEvent evt, Transaction transaction)
        {
            EventHandler?.Invoke(this, new YEventArgs(evt, transaction));
        }

        internal void CallDeepEventHandlerListeners(IList<YEvent> events, Transaction transaction)
        {
            DeepEventHandler?.Invoke(this, new YDeepEventArgs(events, transaction));
        }

        internal string FindRootTypeKey()
        {
            return Doc.FindRootTypeKey(this);
        }

        protected void TypeMapDelete(Transaction transaction, string key)
        {
            if (_map.TryGetValue(key, out var c))
            {
                c.Delete(transaction);
            }
        }

        protected void TypeMapSet(Transaction transaction, string key, object value)
        {
            if (!_map.TryGetValue(key, out var left))
            {
                left = null;
            }

            var doc = transaction.Doc;
            var ownClientId = doc.ClientId;
            IContent content;

            if (value == null)
            {
                content = new ContentAny(new object[] { value });
            }
            else
            {
                switch (value)
                {
                    case YDoc d:
                        content = new ContentDoc(d);
                        break;
                    case AbstractType at:
                        content = new ContentType(at);
                        break;
                    case byte[] ba:
                        content = new ContentBinary(ba);
                        break;
                    default:
                        content = new ContentAny(new[] { value });
                        break;
                }
            }

            var newItem = new Item(new ID(ownClientId, doc.Store.GetState(ownClientId)), left, left?.LastId, null, null, this, key, content);
            newItem.Integrate(transaction, 0);
        }

        protected bool TryTypeMapGet(string key, out object value)
        {
            if (_map.TryGetValue(key, out var val) && !val.Deleted)
            {
                value = val.Content.GetContent()[val.Length - 1];
                return true;
            }

            value = default;
            return false;
        }

        protected object TypeMapGetSnapshot(string key, Snapshot snapshot)
        {
            if (!_map.TryGetValue(key, out var v))
            {
                v = null;
            }

            while (v != null && (!snapshot.StateVector.ContainsKey(v.Id.Client) || v.Id.Clock >= snapshot.StateVector[v.Id.Client]))
            {
                v = v.Left as Item;
            }

            return v != null && v.IsVisible(snapshot) ? v.Content.GetContent()[v.Length - 1] : null;
        }

        protected IEnumerable<KeyValuePair<string, Item>> TypeMapEnumerate() => _map.Where(kvp => !kvp.Value.Deleted);
        
        protected IEnumerable<KeyValuePair<string, object>> TypeMapEnumerateValues()
        {
            foreach (var kvp in TypeMapEnumerate())
            {
                var key = kvp.Key;
                var value = kvp.Value.Content.GetContent()[kvp.Value.Length - 1];
                yield return new KeyValuePair<string, object>(key, value);
            }
        }
    }
}
