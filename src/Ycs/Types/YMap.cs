// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Ycs
{
    /// <summary>
    /// Event that describes changes on a YMap.
    /// </summary>
    public class YMapEvent : YEvent
    {
        public readonly ISet<string> KeysChanged;

        internal YMapEvent(YMap map, Transaction transaction, ISet<string> subs)
            : base(map, transaction)
        {
            KeysChanged = subs;
        }
    }

    /// <summary>
    /// A shared Map implementation.
    /// </summary>
    public class YMap : AbstractType, IEnumerable<KeyValuePair<string, object>>
    {
        internal const int YMapRefId = 1;

        private Dictionary<string, object> _prelimContent;

        public YMap()
            : this(null)
        {
            // Do nothing.
        }

        public YMap(IDictionary<string, object> entries)
        {
            _prelimContent = entries != null ? new Dictionary<string, object>(entries) : new Dictionary<string, object>();
        }

        public int Count => _prelimContent?.Count ?? EnumerateMap().Count();

        public object Get(string key)
        {
            if (!TryMapGet(key, out var value))
            {
                throw new KeyNotFoundException();
            }

            return value;
        }

        public void Set(string key, object value)
        {
            if (Doc != null)
            {
                Doc.Transact(tr =>
                {
                    TypeMapSet(tr, key, value);
                });
            }
            else
            {
                _prelimContent[key] = value;
            }
        }

        public void Delete(string key)
        {
            if (Doc != null)
            {
                Doc.Transact(tr =>
                {
                    TypeMapDelete(tr, key);
                });
            }
            else
            {
                _prelimContent.Remove(key);
            }
        }

        public bool ContainsKey(string key)
        {
            return _map.TryGetValue(key, out var val) && !val.Deleted;
        }

        public IEnumerable<string> Keys() => EnumerateMap().Select(kvp => kvp.Key);

        public IEnumerable<object> Values() => EnumerateMap().Select(kvp => kvp.Value.Content.GetContent()[kvp.Value.Length - 1]);

        internal override AbstractType Copy()
        {
            return new YMap();
        }

        internal override void Integrate(YDoc doc, Item item)
        {
            base.Integrate(doc, item);

            foreach (var kvp in _prelimContent)
            {
                Set(kvp.Key, kvp.Value);
            }

            _prelimContent = null;
        }

        internal override void CallObserver(Transaction transaction, ISet<string> parentSubs)
        {
            CallTypeObservers(transaction, new YMapEvent(this, transaction, parentSubs));
        }

        internal override void Write(IUpdateEncoder encoder)
        {
            encoder.WriteTypeRef(YMapRefId);
        }

        internal static YMap Read(IUpdateDecoder decoder)
        {
            return new YMap();
        }

        private void TypeMapDelete(Transaction transaction, string key)
        {
            if (_map.TryGetValue(key, out var c))
            {
                c.Delete(transaction);
            }
        }

        private void TypeMapSet(Transaction transaction, string key, object value)
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

        private bool TryMapGet(string key, out object value)
        {
            if (_map.TryGetValue(key, out var val) && !val.Deleted)
            {
                value = val.Content.GetContent()[val.Length - 1];
                return true;
            }

            value = default;
            return false;
        }

        // TODO: [alekseyk] Needed for xml?
        private object TypeMapGetSnapshot(string key, Snapshot snapshot)
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

        private IEnumerable<KeyValuePair<string, Item>> EnumerateMap() => _map.Where(kvp => !kvp.Value.Deleted);

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return EnumerateMap().Select(kvp =>
            {
                var key = kvp.Key;
                var value = kvp.Value.Content.GetContent()[kvp.Value.Length - 1];
                return new KeyValuePair<string, object>(key, value);
            }).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
