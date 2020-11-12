// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Ycs
{
    public class ContentDoc : IContent
    {
        internal const int _ref = 9;

        public ContentDoc(YDoc doc)
        {
            if (doc._item != null)
            {
                throw new Exception("This document was already integrated as a sub-document. You should create a second instance instead with the same guid.");
            }

            Doc = doc;
            Opts = new YDocOptions();

            if (!doc.Gc)
            {
                Opts.Gc = false;
            }

            if (doc.AutoLoad)
            {
                Opts.AutoLoad = true;
            }

            if (doc.Meta != null)
            {
                Opts.Meta = doc.Meta;
            }
        }

        public int Ref => _ref;
        public bool Countable => true;
        public int Length => 1;

        public YDoc Doc { get; internal set; }
        public YDocOptions Opts { get; internal set; } = new YDocOptions();

        public IReadOnlyList<object> GetContent() => new[] { Doc };

        public IContent Copy() => new ContentDoc(Doc);

        public IContent Splice(int offset)
        {
            throw new NotImplementedException();
        }

        public bool MergeWith(IContent right)
        {
            return false;
        }

        public void Integrate(Transaction transaction, Item item)
        {
            // This needs to be reflected in doc.destroy as well.
            Doc._item = item;
            transaction.SubdocsAdded.Add(Doc);

            if (Doc.ShouldLoad)
            {
                transaction.SubdocsLoaded.Add(Doc);
            }
        }

        public void Delete(Transaction transaction)
        {
            if (transaction.SubdocsAdded.Contains(Doc))
            {
                transaction.SubdocsAdded.Remove(Doc);
            }
            else
            {
                transaction.SubdocsRemoved.Add(Doc);
            }
        }

        public void Gc(StructStore store)
        {
            // Do nothing.
        }

        public void Write(IUpdateEncoder encoder, int offset)
        {
            // 32 digits separated by hyphens, no braces.
            encoder.WriteString(Doc.Guid);
            Opts.Write(encoder, offset);
        }

        public static ContentDoc Read(IUpdateDecoder decoder)
        {
            var guidStr = decoder.ReadString();

            var opts = YDocOptions.Read(decoder);
            opts.Guid = guidStr;

            return new ContentDoc(new YDoc(opts));
        }
    }
}
