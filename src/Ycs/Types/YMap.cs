// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

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

        public int Count => _prelimContent?.Count ?? TypeMapEnumerate().Count();

        public object Get(string key)
        {
            if (!TryTypeMapGet(key, out var value))
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

        public IEnumerable<string> Keys() => TypeMapEnumerate().Select(kvp => kvp.Key);

        public IEnumerable<object> Values() => TypeMapEnumerate().Select(kvp => kvp.Value.Content.GetContent()[kvp.Value.Length - 1]);

        public YMap Clone() => InternalClone() as YMap;

        internal override AbstractType InternalCopy()
        {
            return new YMap();
        }

        internal override AbstractType InternalClone()
        {
            var map = new YMap();

            foreach (var kvp in TypeMapEnumerate())
            {
                // TODO: [alekseyk] Yjs checks for the AbstractType here, but _map can only have 'Item' values. Might be an error?
                map.Set(kvp.Key, kvp.Value);
            }

            return map;
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

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => TypeMapEnumerateValues().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
