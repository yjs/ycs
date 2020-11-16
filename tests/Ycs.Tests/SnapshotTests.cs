// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ycs
{
    [TestClass]
    public class SnapshotTests : YTestBase
    {
        [TestMethod]
        public void TestBasicRestoreSnapshot()
        {
            var doc = new YDoc(new YDocOptions { Gc = false });
            doc.GetArray("array").Insert(0, new[] { "hello" });

            var snap = doc.CreateSnapshot();
            doc.GetArray("array").Insert(1, new[] { "world" });

            var docRestored = snap.RestoreDocument(doc);

            CollectionAssert.AreEqual(new[] { "hello" }, (ICollection)docRestored.GetArray("array").ToArray());
            CollectionAssert.AreEqual(new[] { "hello", "world" }, (ICollection)doc.GetArray("array").ToArray());
        }

        [TestMethod]
        public void TestEmptyRestoreSnapshot()
        {
            var doc = new YDoc(new YDocOptions { Gc = false });
            var snap = doc.CreateSnapshot();

            snap.StateVector[9999] = 0;
            doc.GetArray().Insert(0, new[] { "world" });

            var docRestored = snap.RestoreDocument(doc);
            Assert.AreEqual(0, docRestored.GetArray().ToArray().Count);
            CollectionAssert.AreEqual(new[] { "world" }, (ICollection)doc.GetArray().ToArray());

            // Now this snapshot reflects the latest state. It should still work.
            var snap2 = doc.CreateSnapshot();
            var docRestored2 = snap2.RestoreDocument(doc);
            CollectionAssert.AreEqual(new[] { "world" }, (ICollection)docRestored2.GetArray().ToArray());
        }

        [TestMethod]
        public void TestRestoreSnapshotWithSubType()
        {
            var doc = new YDoc(new YDocOptions { Gc = false });
            doc.GetArray("array").Insert(0, new[] { new YMap() });
            var subMap = doc.GetArray("array").Get(0) as YMap;
            subMap.Set("key1", "value1");

            var snap = doc.CreateSnapshot();
            subMap.Set("key2", "value2");
            var docRestored = snap.RestoreDocument(doc);

            var restoredSubMap = docRestored.GetArray("array").Get(0) as YMap;
            subMap = doc.GetArray("array").Get(0) as YMap;

            Assert.AreEqual(1, restoredSubMap.Count);
            Assert.AreEqual("value1", restoredSubMap.Get("key1"));

            Assert.AreEqual(2, subMap.Count);
            Assert.AreEqual("value1", subMap.Get("key1"));
            Assert.AreEqual("value2", subMap.Get("key2"));
        }

        [TestMethod]
        public void TestRestoreDeletedItem1()
        {
            var doc = new YDoc(new YDocOptions { Gc = false });
            doc.GetArray("array").Insert(0, new[] { "item1", "item2" });

            var snap = doc.CreateSnapshot();
            doc.GetArray("array").Delete(0);
            var docRestored = snap.RestoreDocument(doc);

            CollectionAssert.AreEqual(new[] { "item1", "item2" }, (ICollection)docRestored.GetArray("array").ToArray());
            CollectionAssert.AreEqual(new[] { "item2" }, (ICollection)doc.GetArray("array").ToArray());
        }

        [TestMethod]
        public void TestRestoreLeftItem()
        {
            var doc = new YDoc(new YDocOptions { Gc = false });
            doc.GetArray("array").Insert(0, new[] { "item1" });
            doc.GetMap("map").Set("test", 1);
            doc.GetArray("array").Insert(0, new[] { "item0" });

            var snap = doc.CreateSnapshot();
            doc.GetArray("array").Delete(1);
            var docRestored = snap.RestoreDocument(doc);

            CollectionAssert.AreEqual(new[] { "item0", "item1" }, (ICollection)docRestored.GetArray("array").ToArray());
            CollectionAssert.AreEqual(new[] { "item0" }, (ICollection)doc.GetArray("array").ToArray());
        }

        [TestMethod]
        public void TestDeletedItemsBase()
        {
            var doc = new YDoc(new YDocOptions { Gc = false });
            doc.GetArray("array").Insert(0, new[] { "item1" });
            doc.GetArray("array").Delete(0);

            var snap = doc.CreateSnapshot();
            doc.GetArray("array").Insert(0, new[] { "item0" });
            var docRestored = snap.RestoreDocument(doc);

            Assert.AreEqual(0, docRestored.GetArray("array").ToArray().Count);
            CollectionAssert.AreEqual(new[] { "item0" }, (ICollection)doc.GetArray("array").ToArray());
        }

        [TestMethod]
        public void TestDeletedItems2()
        {
            var doc = new YDoc(new YDocOptions { Gc = false });
            doc.GetArray("array").Insert(0, new[] { "item1", "item2", "item3" });
            doc.GetArray("array").Delete(1);

            var snap = doc.CreateSnapshot();
            doc.GetArray("array").Insert(0, new[] { "item0" });
            var docRestored = snap.RestoreDocument(doc);

            CollectionAssert.AreEqual(new[] { "item1", "item3" }, (ICollection)docRestored.GetArray("array").ToArray());
            CollectionAssert.AreEqual(new[] { "item0", "item1", "item3" }, (ICollection)doc.GetArray("array").ToArray());
        }

        [TestMethod]
        public void TestDependentChanges()
        {
            Init(users: 2, new YDocOptions { Gc = false });
            var array0 = Arrays[Users[0]];
            var array1 = Arrays[Users[1]];

            array0.Insert(0, new[] { "user1item1" });
            Connector.SyncAll();
            array1.Insert(1, new[] { "user2item1" });
            Connector.SyncAll();

            var snap = array0.Doc.CreateSnapshot();

            array0.Insert(2, new[] { "user1item2" });
            Connector.SyncAll();
            array1.Insert(3, new[] { "user2item2" });
            Connector.SyncAll();

            var docRestored0 = snap.RestoreDocument(array0.Doc);
            CollectionAssert.AreEqual(new[] { "user1item1", "user2item1" }, (ICollection)docRestored0.GetArray("array").ToArray());

            var docRestored1 = snap.RestoreDocument(array1.Doc);
            CollectionAssert.AreEqual(new[] { "user1item1", "user2item1" }, (ICollection)docRestored1.GetArray("array").ToArray());
        }
    }
}
