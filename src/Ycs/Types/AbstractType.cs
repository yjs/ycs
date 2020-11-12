// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Ycs
{
    public class YEventArgs
    {
        public YEventArgs(YEvent evt, Transaction transaction)
        {
            Event = evt;
            Transaction = transaction;
        }

        public YEvent Event { get; }
        public Transaction Transaction { get; }
    }

    public class YDeepEventArgs
    {
        public YDeepEventArgs(IList<YEvent> events, Transaction transaction)
        {
            Events = events;
            Transaction = transaction;
        }

        public IList<YEvent> Events { get; }
        public Transaction Transaction { get; }
    }

    public class AbstractType
    {
        private int _length;
        public virtual int Length
        {
            get
            {
                return _length;
            }

            internal set
            {
                _length = value;
            }
        }

        public Item _item = null;
        public Item _start = null;
        public IDictionary<string, Item> _map = new Dictionary<string, Item>();

        public event EventHandler<YEventArgs> EventHandler;
        public event EventHandler<YDeepEventArgs> DeepEventHandler;

        public YDoc Doc { get; protected set; }

        internal virtual void Integrate(YDoc doc, Item item)
        {
            Doc = doc;
            _item = item;
        }

        internal virtual AbstractType Copy() { throw new NotImplementedException(); }
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

        internal string FindRootTypeKey()
        {
            foreach (var kvp in Doc.Share)
            {
                if (Equals(kvp.Value))
                {
                    return kvp.Key;
                }
            }

            throw new Exception();
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
    }
}
