// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Diagnostics;

namespace Ycs
{
    public abstract class AbstractStruct
    {
        protected AbstractStruct(ID id, int length)
        {
            Debug.Assert(length >= 0);

            Id = id;
            Length = length;
        }

        public ID Id { get; protected set; }
        public int Length { get; protected set; }

        public abstract bool Deleted { get; }

        public abstract bool MergeWith(AbstractStruct right);
        public abstract void Delete(Transaction transaction);
        public abstract void Integrate(Transaction transaction, int offset);
        public abstract int? GetMissing(Transaction transaction, StructStore store);
        public abstract void Write(IUpdateEncoder encoder, int offset);
    }
}
