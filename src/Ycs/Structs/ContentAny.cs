// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Ycs
{
    public class ContentAny : IContent
    {
        internal const int _ref = 8;

        private List<object> _content;

        public ContentAny(IEnumerable content)
        {
            _content = new List<object>();
            foreach (var v in content)
            {
                _content.Add(v);
            }
        }

        public ContentAny(IEnumerable<object> content)
            : this(content.ToList())
        {
            // Do nothing.
        }

        private ContentAny(List<object> content)
        {
            _content = content;
        }

        public int Ref => _ref;
        public bool Countable => true;
        public int Length => _content.Count;

        public IReadOnlyList<object> GetContent() => _content.AsReadOnly();

        public IContent Copy() => new ContentAny(_content.ToList());

        public IContent Splice(int offset)
        {
            var right = new ContentAny(_content.GetRange(offset, _content.Count - offset));
            _content.RemoveRange(offset, _content.Count - offset);
            return right;
        }

        public bool MergeWith(IContent right)
        {
            Debug.Assert(right is ContentAny);
            _content.AddRange((right as ContentAny)._content);
            return true;
        }

        public void Integrate(Transaction transaction, Item item)
        {
            // Do nothing.
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
            int length = _content.Count;
            encoder.WriteLength(length - offset);

            for (int i = offset; i < length; i++)
            {
                var c = _content[i];
                encoder.WriteAny(c);
            }
        }

        public static ContentAny Read(IUpdateDecoder decoder)
        {
            var length = decoder.ReadLength();
            var cs = new List<object>(length);

            for (int i = 0; i < length; i++)
            {
                var c = decoder.ReadAny();
                cs.Add(c);
            }

            return new ContentAny(cs);
        }
    }
}
