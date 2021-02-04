// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.IO;

namespace Ycs
{
    /// <summary>
    /// A relative position is based on the YUjs model and is not affected by document changes.
    /// E.g. if you place a relative position before a certain character, it will always point to this character.
    /// If you place a relative position at the end of a type, it will always point to the end of the type.
    /// <br/>
    /// A numberic position is often unsuited for user selections, because it does not change when content is inserted
    /// before or after.
    /// <br/>
    /// <c>Insert(0, 'x')('a|bc') = 'xa|bc'</c> Where | is tehre relative position.
    /// <br/>
    /// Only one property must be defined.
    /// </summary>
    internal class RelativePosition : IEquatable<RelativePosition>
    {
        public readonly ID? Item;
        public readonly ID? TypeId;
        public readonly string TName;

        /// <summary>
        /// A relative position is associated to a specific character.
        /// By default, the value is <c>&gt;&eq; 0</c>, the relative position is associated to the character
        /// after the meant position.
        /// I.e. position <c>1</c> in <c>'ab'</c> is associated with the character <c>'b'</c>.
        /// <br/>
        /// If the value is <c>&lt; 0</c>, then the relative position is associated with the caharacter
        /// before the meant position.
        /// </summary>
        public int Assoc;

        public RelativePosition(AbstractType type, ID? item, int assoc = 0)
        {
            Item = item;
            Assoc = assoc;

            if (type._item == null)
            {
                TName = type.FindRootTypeKey();
            }
            else
            {
                TypeId = new ID(type._item.Id.Client, type._item.Id.Clock);
            }
        }

        /*
        public RelativePosition(dynamic json)
        {
            TypeId = json.type == null ? (ID?)null : new ID((long)json.type.client, (long)json.type.clock);
            TName = json.tname ?? null;
            Item = json.item == null ? (ID?)null : new ID((long)json.item.client, (long)json.item.clock);
        }
        */

        private RelativePosition(ID? typeId, string tname, ID? item, int assoc)
        {
            TypeId = typeId;
            TName = tname;
            Item = item;
            Assoc = assoc;
        }

        public bool Equals(RelativePosition other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other != null
                && string.Equals(TName, other.TName)
                && ID.Equals(Item, other.Item)
                && ID.Equals(TypeId, other.TypeId)
                && Assoc == other.Assoc;
        }

        /// <summary>
        /// Create a relative position based on an absolute position.
        /// </summary>
        public static RelativePosition FromTypeIndex(AbstractType type, int index, int assoc = 0)
        {
            if (assoc < 0)
            {
                // Associated with the left character or the beginning of a type, decrement index if possible.
                if (index == 0)
                {
                    return new RelativePosition(type, type._item?.Id, assoc);
                }

                index--;
            }

            var t = type._start;
            while (t != null)
            {
                if (!t.Deleted && t.Countable)
                {
                    if (t.Length > index)
                    {
                        // Case 1: found position somewhere in the linked list.
                        return new RelativePosition(type, new ID(t.Id.Client, t.Id.Clock + index), assoc);
                    }

                    index -= t.Length;
                }

                if (t.Right == null && assoc < 0)
                {
                    // Left-associated position, return last available id.
                    return new RelativePosition(type, t.LastId, assoc);
                }

                t = t.Right as Item;
            }

            return new RelativePosition(type, type._item?.Id, assoc);
        }

        public void Write(Stream writer)
        {
            if (Item != null)
            {
                // Case 1: Found position somewhere in the linked list.
                writer.WriteVarUint(0);
                Item.Value.Write(writer);
            }
            else if (TName != null)
            {
                // Case 2: Found position at the end of the list and type is stored in y.share.
                writer.WriteVarUint(1);
                writer.WriteVarString(TName);
            }
            else if (TypeId != null)
            {
                // Case 3: Found position at the end of the list and type is attached to an item.
                writer.WriteVarUint(2);
                TypeId.Value.Write(writer);
            }
            else
            {
                throw new Exception();
            }

            writer.WriteVarInt(Assoc, treatZeroAsNegative: false);
        }

        public static RelativePosition Read(byte[] encodedPosition)
        {
            using (var stream = new MemoryStream(encodedPosition))
            {
                return Read(stream);
            }
        }

        public static RelativePosition Read(Stream reader)
        {
            ID? itemId = null;
            ID? typeId = null;
            string tName = null;

            switch (reader.ReadVarUint())
            {
                case 0:
                    // Case 1: Found position somewhere in the linked list.
                    itemId = ID.Read(reader);
                    break;
                case 1:
                    // Case 2: Found position at the end of the list and type is stored in y.share.
                    tName = reader.ReadVarString();
                    break;
                case 2:
                    // Case 3: Found position at the end of the list and type is attached to an item.
                    typeId = ID.Read(reader);
                    break;
                default:
                    throw new Exception();
            }

            var assoc = reader.Position < reader.Length ? (int)reader.ReadVarInt().Value : 0;
            return new RelativePosition(typeId, tName, itemId, assoc);
        }

        public byte[] ToArray()
        {
            using (var stream = new MemoryStream())
            {
                Write(stream);
                return stream.ToArray();
            }
        }
    }
}
