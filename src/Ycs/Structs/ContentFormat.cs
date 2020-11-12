// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Ycs
{
    public class ContentFormat : IContent
    {
        internal const int _ref = 6;

        public readonly string Key;
        public readonly object Value;

        public ContentFormat(string key, object value)
        {
            Key = key;
            Value = value;
        }

        public int Ref => _ref;
        public bool Countable => false;
        public int Length => 1;

        public IContent Copy() => new ContentFormat(Key, Value);

        public IReadOnlyList<object> GetContent() => throw new NotImplementedException();

        public IContent Splice(int offset) => throw new NotImplementedException();

        public bool MergeWith(IContent right)
        {
            return false;
        }

        public void Integrate(Transaction transaction, Item item)
        {
            // Search markers are currently unsupported for rich text documents.
            (item.Parent as YArrayBase)?.ClearSearchMarkers();
        }

        public void Delete(Transaction transaction)
        {
            // Do nothing.
        }

        public void Gc(StructStore store)
        {
            // Do nothing.
        }

        public void Write(IUpdateEncoder encoder, int offset)
        {
            encoder.WriteKey(Key);
            encoder.WriteJson(Value);
        }

        public static ContentFormat Read(IUpdateDecoder decoder)
        {
            var key = decoder.ReadKey();
            var value = decoder.ReadJson();
            return new ContentFormat(key, value);
        }
    }
}
