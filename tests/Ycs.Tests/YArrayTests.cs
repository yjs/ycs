// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ycs
{
    [TestClass]
    public class YArrayTests : YTestBase
    {
        [TestMethod]
        public void TestBasicUpdate()
        {
            var doc1 = new YDoc();
            var doc2 = new YDoc();
            var content = new List<object> { "hi" };
            doc1.GetArray("array").Insert(0, content);
            var update = doc1.EncodeStateAsUpdateV2();
            doc2.ApplyUpdateV2(update);

            CollectionAssert.AreEqual(content, (ICollection)doc2.GetArray("array").ToArray());
        }

        [TestMethod]
        public void TestDeleteInsert()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            array0.Delete(0, 0);

            Assert.ThrowsException<Exception>(() => array0.Delete(1, 1));

            array0.Insert(0, new List<object> { "A" });
            array0.Delete(1, 0);

            CompareUsers();
        }

        [TestMethod]
        public void TestInsertThreeElementsTryRegetProperty()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            array0.Insert(0, new List<object> { 1, true, false });
            CollectionAssert.AreEqual(new object[] { 1, true, false }, (ICollection)array0.ToArray());

            Connector.FlushAllMessages();

            var array1 = Arrays[Users[1]];
            CollectionAssert.AreEqual(new object[] { 1, true, false }, (ICollection)array1.ToArray());

            CompareUsers();
        }

        [TestMethod]
        public void TestConcurrentInsertWithThreeConflicts()
        {
            Init(users: 3);

            Arrays[Users[0]].Insert(0, new object[] { 0 });
            Arrays[Users[1]].Insert(0, new object[] { 0 });
            Arrays[Users[2]].Insert(0, new object[] { 0 });

            CompareUsers();
        }

        [TestMethod]
        public void TestConcurrentInsertDeleteWithThreeConflicts()
        {
            Init(users: 3);

            var array0 = Arrays[Users[0]];
            var array1 = Arrays[Users[1]];
            var array2 = Arrays[Users[2]];

            array0.Insert(0, new List<object> { "x", "y", "z" });

            Connector.FlushAllMessages();

            array0.Insert(1, new object[] { 0 });
            array1.Delete(0);
            array1.Delete(1, 1);
            array2.Insert(1, new object[] { 2 });

            CompareUsers();
        }

        [TestMethod]
        public void TestInsertionsInLateSync()
        {
            Init(users: 3);

            var array0 = Arrays[Users[0]];
            var array1 = Arrays[Users[1]];
            var array2 = Arrays[Users[2]];

            array0.Insert(0, new[] { "x", "y" });
            Connector.FlushAllMessages();

            Users[1].Disconnect();
            Users[2].Disconnect();

            array0.Insert(1, new[] { "user0" });
            array1.Insert(1, new[] { "user1" });
            array2.Insert(1, new[] { "user2" });

            Users[1].Connect();
            Users[2].Connect();
            Connector.FlushAllMessages();

            CompareUsers();
        }

        [TestMethod]
        public void TestDisconnectReallyPreventsSendingMessages()
        {
            Init(users: 3);

            var array0 = Arrays[Users[0]];
            var array1 = Arrays[Users[1]];

            array0.Insert(0, new[] { "x", "y" });
            Connector.FlushAllMessages();

            Users[1].Disconnect();
            Users[2].Disconnect();

            array0.Insert(1, new[] { "user0" });
            array1.Insert(1, new[] { "user1" });

            CollectionAssert.AreEqual(new[] { "x", "user0", "y" }, (ICollection)array0.ToArray());
            CollectionAssert.AreEqual(new[] { "x", "user1", "y" }, (ICollection)array1.ToArray());

            Users[1].Connect();
            Users[2].Connect();

            CompareUsers();
        }

        [TestMethod]
        public void TestDeletionsInLateSync()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            var array1 = Arrays[Users[1]];

            array0.Insert(0, new[] { "x", "y" });
            Connector.FlushAllMessages();
            Users[1].Disconnect();

            array0.Delete(0, 2);
            array1.Delete(1, 1);

            Users[1].Connect();

            CompareUsers();
        }

        [TestMethod]
        public void TestInsertThenMergeDeleteOnSync()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            var array1 = Arrays[Users[1]];

            array0.Insert(0, new[] { "x", "y", "z" });
            Connector.FlushAllMessages();
            Users[0].Disconnect();
            array1.Delete(0, 3);
            Users[0].Connect();

            CompareUsers();
        }

        [TestMethod]
        public void TestInsertAndDeleteEvents()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            YEventArgs evt = null;

            array0.EventHandler += (s, e) =>
            {
                evt = e;
            };

            array0.Insert(0, new object[] { 0, 1, 2 });
            Assert.IsNotNull(evt);
            evt = null;

            array0.Delete(0);
            Assert.IsNotNull(evt);
            evt = null;

            array0.Delete(0, 2);
            Assert.IsNotNull(evt);
            evt = null;

            CompareUsers();
        }

        [TestMethod]
        public void TestNestedObserverEvents()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            var values = new List<object>();

            array0.EventHandler += (s, e) =>
            {
                if (array0.Length == 1)
                {
                    // Inserting, will call this observer again.
                    // We expect that this observer is called after this handler finishes.
                    array0.Insert(1, new object[] { 1 });
                    values.Add(0);
                }
                else
                {
                    // This should be called the second time an element is inserted (above case).
                    values.Add(1);
                }
            };

            array0.Insert(0, new object[] { 0 });

            CollectionAssert.AreEqual(new object[] { 0, 1 }, values);
            CollectionAssert.AreEqual(new object[] { 0, 1 }, (ICollection)array0.ToArray());

            CompareUsers();
        }

        [TestMethod]
        public void TestInsertAndDeleteEventsForTypes()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            YEventArgs evt = null;

            array0.EventHandler += (s, e) =>
            {
                evt = e;
            };

            array0.Insert(0, new object[] { new YArray() });
            Assert.IsNotNull(evt);
            evt = null;

            array0.Delete(0);
            Assert.IsNotNull(evt);
            evt = null;

            CompareUsers();
        }

        [TestMethod]
        public void TestObserveDeepEventOrder()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            IList<YEvent> events = null;

            array0.DeepEventHandler += (s, e) =>
            {
                events = e.Events;
            };

            array0.Insert(0, new[] { new YMap() });

            Users[0].Transact(tr =>
            {
                ((YMap)array0.Get(0)).Set("a", "a");
                array0.Insert(0, new object[] { 0 });
            });

            for (int i = 1; i < events.Count; i++)
            {
                Assert.IsTrue(events[i - 1].Path.Count <= events[i].Path.Count, "Path size increases, fire top-level events first.");
            }
        }

        [TestMethod]
        public void TestChangeEvent()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            ChangesCollection changes = null;

            array0.EventHandler += (s, e) =>
            {
                changes = e.Event.Changes;
            };

            var newArr = new YArray();

            array0.Insert(0, new object[] { newArr, 4, "dtrn" });
            Assert.IsNotNull(changes);
            Assert.AreEqual(2, changes.Added.Count);
            Assert.AreEqual(0, changes.Deleted.Count);
            Assert.AreEqual(1, changes.Delta.Count);
            CollectionAssert.AreEqual(new object[] { newArr, 4, "dtrn" }, (ICollection)changes.Delta[0].Insert);
            changes = null;

            array0.Delete(0, 2);
            Assert.IsNotNull(changes);
            Assert.AreEqual(0, changes.Added.Count);
            Assert.AreEqual(2, changes.Deleted.Count);
            Assert.AreEqual(2, changes.Delta[0].Delete);
            changes = null;

            array0.Insert(1, new object[] { 0.1 });
            Assert.IsNotNull(changes);
            Assert.AreEqual(1, changes.Added.Count);
            Assert.AreEqual(0, changes.Deleted.Count);
            Assert.AreEqual(2, changes.Delta.Count);
            Assert.AreEqual(1, changes.Delta[0].Retain);
            CollectionAssert.AreEqual(new object[] { 0.1 }, (ICollection)changes.Delta[1].Insert);

            CompareUsers();
        }

        [TestMethod]
        public void TestInsertAndDeleteEventsForTypes2()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            var events = new List<YEventArgs>();

            array0.EventHandler += (s, e) =>
            {
                events.Add(e);
            };

            array0.Insert(0, new object[] { "hi", new YMap() });
            Assert.AreEqual(1, events.Count, "Event is triggered exactly once for insertion of two elements");
            array0.Delete(1);
            Assert.AreEqual(2, events.Count, "Event is triggered exactly once for deletion");

            CompareUsers();
        }

        [TestMethod]
        public void TestNewChildDoesNotEmitEventInTransaction()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            var fired = false;

            Users[0].Transact(tr =>
            {
                var newMap = new YMap();
                newMap.EventHandler += (s, e) =>
                {
                    fired = true;
                };

                array0.Insert(0, new object[] { newMap });
                newMap.Set("tst", 42);
            });

            Assert.IsFalse(fired, "Event does not trigger");
        }

        [TestMethod]
        public void TestGarbageCollector()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            array0.Insert(0, new object[] { "x", "y", "z" });
            Connector.FlushAllMessages();

            Users[0].Disconnect();
            array0.Delete(0, 3);
            Users[0].Connect();
            Connector.FlushAllMessages();

            CompareUsers();
        }

        [TestMethod]
        public void TestEventTargetIsSetCorrectlyOnLocal()
        {
            Init(users: 2);

            var array0 = Arrays[Users[0]];
            YEvent evt = null;

            array0.EventHandler += (s, e) =>
            {
                evt = e.Event;
            };

            array0.Insert(0, new object[] { "stuff" });
            Assert.AreEqual(array0, evt.Target, "Target property is set correctly");
        }

        [TestMethod]
        public void TestEventTargetIsSetCorrectlyOnRemote()
        {
            Init(users: 3);

            var array0 = Arrays[Users[0]];
            var array1 = Arrays[Users[1]];
            YEvent evt = null;

            array0.EventHandler += (s, e) =>
            {
                evt = e.Event;
            };

            array1.Insert(0, new object[] { "stuff" });
            Connector.FlushAllMessages();

            Assert.AreEqual(array0, evt.Target, "Target property is set correctly");

            CompareUsers();
        }

        [TestMethod]
        public void TestIteratingArrayContainingTypes()
        {
            Init(users: 1);
            var arr = Arrays[Users[0]];
            const int numItems = 10;

            for (int i = 0; i < numItems; i++)
            {
                var map = new YMap();
                map.Set("value", i);
                arr.Add(new[] { map });
            }

            int cnt = 0;
            foreach (var item in arr)
            {
                Assert.AreEqual(cnt++, (item as YMap).Get("value"));
            }
        }

        [TestMethod]
        public void TestSlice()
        {
            Init(users: 1);
            var arr = Arrays[Users[0]];

            arr.Insert(0, new object[] { 1, 2, 3 });
            CollectionAssert.AreEqual(new object[] { 1, 2, 3 }, arr.Slice(0).ToArray());
            CollectionAssert.AreEqual(new object[] { 2, 3 }, arr.Slice(1).ToArray());
            CollectionAssert.AreEqual(new object[] { 1, 2 }, arr.Slice(0, -1).ToArray());

            arr.Insert(0, new object[] { 0 });
            CollectionAssert.AreEqual(new object[] { 0, 1, 2, 3 }, arr.Slice(0).ToArray());
            CollectionAssert.AreEqual(new object[] { 0, 1 }, arr.Slice(0, 2).ToArray());
        }

        [DataTestMethod]
        [DataRow(5, 6)]
        [DataRow(5, 40)]
        [DataRow(5, 42)]
        [DataRow(5, 43)]
        [DataRow(5, 44)]
        [DataRow(5, 45)]
        [DataRow(5, 46)]
        [DataRow(5, 300)]
        [DataRow(5, 400)]
        [DataRow(5, 500)]
        [DataRow(5, 600)]
        [DataRow(5, 1_000)]
        [DataRow(5, 1_800)]
        [DataRow(5, 3_000)]
        /*
        [DataRow(5, 5_000)]
        [DataRow(5, 10_000)]
        [DataRow(5, 15_000)]
        */
        public void TestRepeatGeneratingYArrayTests(int users, int iterations)
        {
            RandomTests(new List<Action<TestYInstance, Random>>
            {
                // Insert
                (user, rand) =>
                {
                    var yarray = Arrays[user];
                    var uniqueNumber = GetUniqueNumber();
                    var content = new List<object>();
                    int len = rand.Next(1, 4);

                    for (int i = 0; i < len; i++)
                    {
                        content.Add(uniqueNumber);
                    }

                    int pos = rand.Next(0, yarray.Length + 1);
                    var oldContent = yarray.ToArray().ToList();
                    // Debug.WriteLine($"INSERT by {user.ClientId} at pos {pos}, oldContent: {string.Join(",", oldContent)}, adds: {string.Join(",", content)}");

                    yarray.Insert(pos, content);
                    oldContent.InsertRange(pos, content);

                    var newContent = yarray.ToArray();
                    // Debug.WriteLine($"New content: {string.Join(",", newContent)}");
                    CompareObjects(newContent, oldContent);
                },

                // Insert array
                (user, rand) =>
                {
                    var yarray = Arrays[user];
                    var pos = rand.Next(0, yarray.Length + 1);
                    // Debug.WriteLine($"ARRAY by {user.ClientId} at pos {pos}");

                    yarray.Insert(pos, new[] { new YArray() });

                    var arr = (YArray)yarray.Get(pos);
                    arr.Insert(0, new object[] { 1, 2, 3 });
                },

                // Insert map
                (user, rand) =>
                {
                    var yarray = Arrays[user];
                    var pos = rand.Next(0, yarray.Length + 1);
                    // Debug.WriteLine($"MAP by {user.ClientId} at pos {pos}");

                    yarray.Insert(pos, new[] { new YMap() });

                    var map = (YMap)yarray.Get(pos);
                    map.Set("someprop", 42);
                    map.Set("someprop", 43);
                    map.Set("someprop", 44);
                },

                // Delete
                (user, rand) =>
                {
                    var yarray = Arrays[user];
                    int length = yarray.Length;

                    if (length > 0)
                    {
                        int somePos = rand.Next(0, length - 1);
                        int delLength = rand.Next(1, length - somePos);

                        var oldContent = yarray.ToArray().ToList();
                        // Debug.WriteLine($"DELETE by {user.ClientId} at pos {somePos}, len: {delLength}, oldContent: {string.Join(",", oldContent)}");

                        yarray.Delete(somePos, delLength);
                        oldContent.RemoveRange(somePos, delLength);

                        var newContent = yarray.ToArray();
                        // Debug.WriteLine($"New content: {string.Join(",", newContent)}");
                        CompareObjects(newContent, oldContent);
                    }
                }
            }, users, iterations);
        }
    }
}
