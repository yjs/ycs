// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Diagnostics;

namespace Ycs
{
    public class GC : AbstractStruct
    {
        internal const byte StructGCRefNumber = 0;

        internal GC(ID id, int length)
            : base(id, length)
        {
            // Do nothing.
        }

        public override bool Deleted => true;

        internal override bool MergeWith(AbstractStruct right)
        {
            Debug.Assert(right is GC);
            Length += right.Length;
            return true;
        }

        internal override void Delete(Transaction transaction)
        {
            // Do nothing.
        }

        internal override void Integrate(Transaction transaction, int offset)
        {
            if (offset > 0)
            {
                Id = new ID(Id.Client, Id.Clock + offset);
                Length -= offset;
            }

            transaction.Doc.Store.AddStruct(this);
        }

        internal override long? GetMissing(Transaction transaction, StructStore store)
        {
            return null;
        }

        internal override void Write(IUpdateEncoder encoder, int offset)
        {
            encoder.WriteInfo(StructGCRefNumber);
            encoder.WriteLength(Length - offset);
        }
    }
}
