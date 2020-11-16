// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;

namespace Ycs
{
    public class ContentJson : IContentEx
    {
        internal const int _ref = 2;

        private readonly List<object> _content;

        internal ContentJson(IEnumerable<object> data)
        {
            _content = new List<object>(data);
        }

        private ContentJson(List<object> other)
        {
            _content = other;
        }

        int IContentEx.Ref => _ref;

        public bool Countable => true;
        public int Length => _content?.Count ?? 0;

        public IReadOnlyList<object> GetContent() => _content.AsReadOnly();

        public IContent Copy() => new ContentJson(_content);

        public IContent Splice(int offset)
        {
            var right = new ContentJson(_content.GetRange(offset, _content.Count - offset));
            _content.RemoveRange(offset, _content.Count - offset);
            return right;
        }

        public bool MergeWith(IContent right)
        {
            Debug.Assert(right is ContentJson);
            _content.AddRange((right as ContentJson)._content);
            return true;
        }

        void IContentEx.Integrate(Transaction transaction, Item item)
        {
            // Do nothing.
        }

        void IContentEx.Delete(Transaction transaction)
        {
            // Do nothing.
        }

        void IContentEx.Gc(StructStore store)
        {
            // Do nothing.
        }

        void IContentEx.Write(IUpdateEncoder encoder, int offset)
        {
            var len = _content.Count;
            encoder.WriteLength(len);
            for (int i = offset; i < len; i++)
            {
                var jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(_content[i]);
                encoder.WriteString(jsonStr);
            }
        }

        internal static ContentJson Read(IUpdateDecoder decoder)
        {
            var len = decoder.ReadLength();
            var content = new List<object>(len);

            for (int i = 0; i < len; i++)
            {
                var jsonStr = decoder.ReadString();
                object jsonObj = string.Equals(jsonStr, "undefined")
                    ? null
                    : Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);
                content.Add(jsonObj);
            }

            return new ContentJson(content);
        }
    }
}
