// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ycs
{
    [TestClass]
    public class YTestBase
    {
        // Set to '-1', so the first returned value is '0'.
        private static int _uniqueNumber = -1;

        protected List<TestYInstance> Users;
        protected TestConnector Connector;
        protected IDictionary<TestYInstance, YArray> Arrays;
        protected IDictionary<TestYInstance, YMap> Maps;
        protected IDictionary<TestYInstance, YText> Texts;

        [TestCleanup]
        protected void Cleanup()
        {
            Connector.DisconnectAll();

            foreach (var u in Users)
            {
                u.Destroy();
            }

            Users.Clear();
            Arrays.Clear();
            Maps.Clear();
            Texts.Clear();
        }

        protected void Init(int users = 5, YDocOptions options = null)
        {
            Users = new List<TestYInstance>();
            Arrays = new Dictionary<TestYInstance, YArray>();
            Maps = new Dictionary<TestYInstance, YMap>();
            Texts = new Dictionary<TestYInstance, YText>();
            Connector = new TestConnector();

            for (int i = 0; i < users; i++)
            {
                var y = Connector.CreateY(i, options);
                Users.Add(y);

                Arrays[y] = y.GetArray("array");
                Maps[y] = y.GetMap("map");
                Texts[y] = y.GetText("text");
            }

            Connector.SyncAll();
        }

        /// <summary>
        /// 1. Reconnect and flush all.
        /// 2. User 0 gc.
        /// 3. Get type content.
        /// 4. Disconnect & reconnect all (so gc is propagated).
        /// 5. Compare os, ds, ss.
        /// </summary>
        protected void CompareUsers()
        {
            foreach (var u in Users)
            {
                u.Connect();
            }

            while (Connector.FlushAllMessages())
            {
                // Flush all messages.
            }

            var userArrayValues = Users.Select(u => u.GetArray("array").ToArray()).ToList();
            var userMapValues = Users.Select(u => u.GetMap("map").ToList())
                .Select(v => new Dictionary<string, object>(v)).ToList();
            var userTextValues = Users.Select(u => u.GetText("text").ToDelta()).ToList();

            foreach (var u in Users)
            {
                u.Store.IntegrityCheck();
            }

            // Test array iterator.
            CollectionAssert.AreEqual(Users[0].GetArray("array").ToArray().ToList(), Users[0].GetArray("array").ToList());

            // Test map iterator.
            Assert.AreEqual(userMapValues[0].Count, Users[0].GetMap("map").Keys().Count());
            Assert.AreEqual(userMapValues[0].Count, Users[0].GetMap("map").Keys().Count());
            Assert.AreEqual(userMapValues[0].Count, Users[0].GetMap("map").Values().Count());

            foreach (var kvp in Users[0].GetMap("map"))
            {
                var otherKvp = userMapValues[0].FirstOrDefault(v => string.Equals(v.Key, kvp.Key));
                Assert.IsNotNull(otherKvp);
                Assert.AreEqual(otherKvp.Value, kvp.Value);
            }

            // Compare all users.
            for (int i = 0; i < Users.Count - 1; i++)
            {
                // Compare arrays.
                Assert.AreEqual(userArrayValues[i].Count, Users[i].GetArray("array").Length);
                CompareObjects(userArrayValues[i], userArrayValues[i + 1]);

                // Compare texts.
                CompareYText(Users[i].GetText("text"), Users[i + 1].GetText("text"));
                var deltaStr = string.Join(null, userTextValues[i].Select(a => a.Insert != null && a.Insert is string s ? s : string.Empty));
                Assert.AreEqual(Users[i].GetText("text").ToString(), deltaStr);

                // Compare maps.
                CompareObjects(userMapValues[i], userMapValues[i + 1]);

                // Compare internal structures.
                CompareObjects(Users[i].Store.GetStateVector(), Users[i + 1].Store.GetStateVector());
                CompareDS(new DeleteSet(Users[i].Store), new DeleteSet(Users[i + 1].Store));
                CompareStructStores(Users[i].Store, Users[i + 1].Store);
            }

            foreach (var u in Users)
            {
                u.Destroy();
            }
        }

        protected void CompareUsers(TestYInstance left, TestYInstance right)
        {

        }

        protected void CompareObjects(object o1, object o2)
        {
            if (ReferenceEquals(o1, o2))
            {
                return;
            }

            Assert.IsTrue((o1 == null) == (o2== null));

            switch (o1)
            {
                case YText text:
                    Assert.IsInstanceOfType(o1, o2.GetType());
                    CompareYText(text, o2 as YText);
                    break;
                case YArray arr:
                    Assert.IsInstanceOfType(o1, o2.GetType());
                    CompareYArray(arr, o2 as YArray);
                    break;
                case YMap map:
                    Assert.IsInstanceOfType(o1, o2.GetType());
                    CompareYMap(map, o2 as YMap);
                    break;
                case IDictionary d1:
                    var d2 = o2 as IDictionary;
                    Assert.AreEqual(d1.Count, d2.Count);
                    foreach (var key1 in d1.Keys)
                    {
                        Assert.IsTrue(d2.Contains(key1));
                        CompareObjects(d1[key1], d2[key1]);
                    }
                    break;
                case ICollection c1:
                    var c2 = o2 as ICollection;
                    Assert.IsNotNull(c2);
                    Assert.AreEqual(c1.Count, c2.Count);

                    int pos = 0;
                    var it2 = c2.GetEnumerator();
                    foreach (var v1 in c1)
                    {
                        it2.MoveNext();
                        CompareObjects(v1, it2.Current);
                        pos++;
                    }

                    break;
                default:
                    Assert.AreEqual(o1, o2);
                    break;
            }
        }

        protected void CompareYText(YText t1, YText t2)
        {
            if (ReferenceEquals(t1, t2))
            {
                return;
            }

            Assert.IsTrue((t1 == null) == (t2 == null));
            Assert.AreEqual(t1.Length, t2.Length);

            var str1 = t1.ToString();
            Assert.AreEqual(str1, t2.ToString());
        }

        protected void CompareYArray(YArray arr1, YArray arr2)
        {
            if (ReferenceEquals(arr1, arr2))
            {
                return;
            }

            Assert.IsTrue((arr1 == null) == (arr2 == null));
            Assert.AreEqual(arr1.Length, arr1.Length);

            for (int i = 0; i < arr1.Length; i++)
            {
                CompareObjects(arr1.Get(i), arr2.Get(i));
            }
        }

        protected void CompareYMap(YMap map1, YMap map2)
        {
            if (ReferenceEquals(map1, map2))
            {
                return;
            }

            Assert.IsTrue((map1 == null) == (map2 == null));
            Assert.AreEqual(map1.Count, map2.Count);

            foreach (var key1 in map1.Keys())
            {
                Assert.IsTrue(map2.ContainsKey(key1));
                CompareObjects(map1.Get(key1), map2.Get(key1));
            }
        }

        internal void CompareStructStores(StructStore ss1, StructStore ss2)
        {
            Assert.AreEqual(ss1.Clients.Count, ss2.Clients.Count);

            foreach (var kvp in ss1.Clients)
            {
                var client = kvp.Key;
                var structs1 = kvp.Value;

                Assert.IsTrue(ss2.Clients.TryGetValue(client, out var structs2));
                Assert.AreEqual(structs1.Count, structs2.Count);

                for (int i = 0; i < structs1.Count; i++)
                {
                    var s1 = structs1[i];
                    var s2 = structs2[i];

                    // Checks for abstract struct.
                    if (!s1.GetType().IsAssignableFrom(s2.GetType()) ||
                        !ID.Equals(s1.Id, s2.Id) ||
                        s1.Deleted != s2.Deleted ||
                        s1.Length != s2.Length)
                    {
                        Assert.Fail("Structs don't match");
                    }

                    if (s1 is Item s1Item)
                    {
                        if (!(s2 is Item s2Item) ||
                            !((s1Item.Left == null && s2Item.Left == null) || (s1Item.Left != null && s2Item.Left != null && ID.Equals((s1Item.Left as Item)?.LastId, (s2Item.Left as Item)?.LastId))) ||
                            !CompareItemIds(s1Item.Right as Item, s2Item.Right as Item) ||
                            !ID.Equals(s1Item.LeftOrigin, s2Item.LeftOrigin) ||
                            !ID.Equals(s1Item.RightOrigin, s2Item.RightOrigin) ||
                            !string.Equals(s1Item.ParentSub, s2Item.ParentSub))
                        {
                            Assert.Fail("Items don't match");
                        }

                        // Make sure that items are connected correctly.
                        Assert.IsTrue(s1Item.Left == null || (s1Item.Left as Item)?.Right == s1Item);
                        Assert.IsTrue(s1Item.Right == null || (s1Item.Right as Item)?.Left == s1Item);
                        Assert.IsTrue((s2 as Item).Left == null || ((s2 as Item).Left as Item).Right == s2);
                        Assert.IsTrue((s2 as Item).Right == null || ((s2 as Item).Right as Item).Left == s2);
                    }
                }
            }
        }

        internal void CompareDS(DeleteSet ds1, DeleteSet ds2)
        {
            Assert.AreEqual(ds1.Clients.Count, ds2.Clients.Count);

            foreach (var kvp in ds1.Clients)
            {
                var client = kvp.Key;
                var deleteItems1 = kvp.Value;
                Assert.IsTrue(ds2.Clients.TryGetValue(client, out var deleteItems2));
                Assert.AreEqual(deleteItems1.Count, deleteItems2.Count);

                for (int i = 0; i < deleteItems1.Count; i++)
                {
                    var di1 = deleteItems1[i];
                    var di2 = deleteItems2[i];

                    if (di1.Clock != di2.Clock || di1.Length != di2.Length)
                    {
                        Assert.Fail("DeleteSets don't match");
                    }
                }
            }
        }

        protected bool CompareItemIds(Item a, Item b)
        {
            var result = a == b || (a != null && b != null & ID.Equals(a.Id, b.Id));
            Assert.IsTrue(result);
            return result;
        }

        protected void RandomTests(IList<Action<TestYInstance, Random>> mods, int users, int iterations)
        {
            Init(users);

            var rand = new Random();

            for (int i = 0; i < iterations; i++)
            {
                if (rand.Next(0, 100) < 2)
                {
                    // 2% chance to disconnect/reconnect a random user.
                    if (rand.Next(0, 2) == 0)
                    {
                        Connector.DisconnectRandom();
                    }
                    else
                    {
                        Connector.ReconnectRandom();
                    }
                }
                else if (rand.Next(0, 100) == 0)
                {
                    // 1% chance to flush all.
                    Connector.FlushAllMessages();
                }
                else if (rand.Next(0, 100) < 50)
                {
                    // 50% chance to flush a random message.
                    Connector.FlushRandomMessage();
                }

                var userIndex = rand.Next(0, Users.Count - 1);
                var testIndex = rand.Next(0, mods.Count);

                mods[testIndex](Users[userIndex], rand);
            }

            CompareUsers();
        }

        protected int GetUniqueNumber() => Interlocked.Increment(ref _uniqueNumber);

        protected static char GetRandomChar(Random rand) => (char)rand.Next('A', 'Z' + 1);
    }
}
