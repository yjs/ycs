// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;

namespace Ycs
{
    public struct ID : IEquatable<ID>
    {
        /// <summary>
        /// Client id.
        /// </summary>
        public int Client;

        /// <summary>
        /// Unique per client id, continuous number.
        /// </summary>
        public int Clock;

        public ID(int client, int clock)
        {
            Debug.Assert(client >= 0);
            Debug.Assert(clock >= 0);

            Client = client;
            Clock = clock;
        }

        public bool Equals(ID other)
        {
            return Client == other.Client && Clock == other.Clock;
        }

        public static bool Equals(ID? a, ID? b)
        {
            return (a == null && b == null) || (a != null && b != null && a.Value.Equals(b.Value));
        }

        public void Write(BinaryWriter writer)
        {
            writer.WriteVarUint((uint)Client);
            writer.WriteVarUint((uint)Clock);
        }

        public static ID Read(BinaryReader reader)
        {
            var client = (int)reader.ReadVarUint();
            var clock = (int)reader.ReadVarUint();
            Debug.Assert(client >= 0 && clock >= 0);

            return new ID(client, clock);
        }
    }
}
