// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Ycs
{
    public class ContentString : IContentEx
    {
        internal const int _ref = 4;

        private readonly List<object> _content;

        internal ContentString(string value)
            : this(value.Cast<object>().ToList())
        {
            // Do nothing.
        }

        private ContentString(List<object> content)
        {
            _content = content;
        }

        int IContentEx.Ref => _ref;

        public bool Countable => true;
        public int Length => _content.Count;

        internal void AppendToBuilder(StringBuilder sb)
        {
            foreach (var c in _content)
            {
                sb.Append((char)c);
            }
        }

        public string GetString()
        {
            var sb = new StringBuilder();

            foreach (var c in _content)
            {
                sb.Append((char)c);
            }

            return sb.ToString();
        }

        public IReadOnlyList<object> GetContent() => _content.AsReadOnly();

        public IContent Copy() => new ContentString(_content.ToList());

        public IContent Splice(int offset)
        {
            var right = new ContentString(_content.GetRange(offset, _content.Count - offset));
            _content.RemoveRange(offset, _content.Count - offset);

            // Prevent encoding invalid documents because of splitting of surrogate pairs.
            var firstCharCode = (char)_content[offset - 1];
            if (firstCharCode >= 0xD800 && firstCharCode <= 0xDBFF)
            {
                // Last character of the left split is the start of a surrogate utf16/ucs2 pair.
                // We don't support splitting of surrogate pairs because this may lead to invalid documents.
                // Replace the invalid character with a unicode replacement character U+FFFD.
                _content[offset - 1] = '\uFFFD';

                // Replace right as well.
                right._content[0] = '\uFFFD';
            }

            return right;
        }

        public bool MergeWith(IContent right)
        {
            Debug.Assert(right is ContentString);
            _content.AddRange((right as ContentString)._content);
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
            var sb = new StringBuilder(_content.Count - offset);
            for (int i = offset; i < _content.Count; i++)
            {
                sb.Append((char)_content[i]);
            }

            var str = sb.ToString();
            encoder.WriteString(str);
        }

        internal static ContentString Read(IUpdateDecoder decoder)
        {
            return new ContentString(decoder.ReadString());
        }
    }
}
