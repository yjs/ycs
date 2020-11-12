// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ycs
{
    [TestClass]
    public class YDocTests : YTestBase
    {
        [TestMethod]
        public void TestClientIdDuplicateChange()
        {
            var doc1 = new YDoc();
            doc1.ClientId = 0;
            var doc2 = new YDoc();
            doc2.ClientId = 0;
            Assert.AreEqual(doc1.ClientId, doc2.ClientId);

            doc1.GetArray("a").Insert(0, new object[] { 1, 2 });
            doc2.ApplyUpdateV2(doc1.EncodeStateAsUpdateV2());
            Assert.AreNotEqual(doc1.ClientId, doc2.ClientId);
        }

        [TestMethod]
        public void TestGetTypeEmptyId()
        {
            var doc1 = new YDoc();
            doc1.GetText(string.Empty).Insert(0, "h");
            doc1.GetText().Insert(1, "i");

            var doc2 = new YDoc();
            doc2.ApplyUpdateV2(doc1.EncodeStateAsUpdateV2());

            Assert.AreEqual("hi", doc2.GetText().ToString());
            Assert.AreEqual("hi", doc2.GetText(string.Empty).ToString());
        }

        [TestMethod]
        public void TestSubdoc()
        {
            var doc = new YDoc();
            doc.Load();

            {
                List<List<string>> events = null;
                doc.SubdocsChanged += (s, e) =>
                {
                    events = new List<List<string>>();
                    events.Add(new List<string>(e.Added.Select(d => d.Guid)));
                    events.Add(new List<string>(e.Removed.Select(d => d.Guid)));
                    events.Add(new List<string>(e.Loaded.Select(d => d.Guid)));
                };

                var subdocs = doc.GetMap("mysubdocs");
                var docA = new YDoc(new YDocOptions { Guid = "a" });
                docA.Load();
                subdocs.Set("a", docA);
                CollectionAssert.AreEqual(new[] { "a" }, events[0]);
                CollectionAssert.AreEqual(new object[] { }, events[1]);
                CollectionAssert.AreEqual(new[] { "a" }, events[2]);
                events = null;

                (subdocs.Get("a") as YDoc).Load();
                Assert.IsNull(events);
                events = null;

                (subdocs.Get("a") as YDoc).Destroy();
                CollectionAssert.AreEqual(new[] { "a" }, events[0]);
                CollectionAssert.AreEqual(new[] { "a" }, events[1]);
                CollectionAssert.AreEqual(new object[] { }, events[2]);
                events = null;

                (subdocs.Get("a") as YDoc).Load();
                CollectionAssert.AreEqual(new object[] { }, events[0]);
                CollectionAssert.AreEqual(new object[] { }, events[1]);
                CollectionAssert.AreEqual(new[] { "a" }, events[2]);
                events = null;

                subdocs.Set("b", new YDoc(new YDocOptions { Guid = "a" }));
                CollectionAssert.AreEqual(new[] { "a" }, events[0]);
                CollectionAssert.AreEqual(new object[] { }, events[1]);
                CollectionAssert.AreEqual(new object[] { }, events[2]);
                events = null;

                (subdocs.Get("b") as YDoc).Load();
                CollectionAssert.AreEqual(new object[] { }, events[0]);
                CollectionAssert.AreEqual(new object[] { }, events[1]);
                CollectionAssert.AreEqual(new[] { "a" }, events[2]);
                events = null;

                var docC = new YDoc(new YDocOptions { Guid = "c" });
                docC.Load();
                subdocs.Set("c", docC);
                CollectionAssert.AreEqual(new[] { "c" }, events[0]);
                CollectionAssert.AreEqual(new object[] { }, events[1]);
                CollectionAssert.AreEqual(new[] { "c" }, events[2]);
                events = null;

                var guids = doc.GetSubdocGuids().ToList();
                guids.Sort();
                CollectionAssert.AreEqual(new[] { "a", "c" }, guids);
            }

            var doc2 = new YDoc();

            {
                Assert.AreEqual(0, doc2.GetSubdocGuids().Count());

                List<List<string>> events = null;
                doc2.SubdocsChanged += (s, e) =>
                {
                    events = new List<List<string>>();
                    events.Add(new List<string>(e.Added.Select(d => d.Guid)));
                    events.Add(new List<string>(e.Removed.Select(d => d.Guid)));
                    events.Add(new List<string>(e.Loaded.Select(d => d.Guid)));
                };

                doc2.ApplyUpdateV2(doc.EncodeStateAsUpdateV2());
                CollectionAssert.AreEqual(new[] { "a", "a", "c" }, events[0]);
                CollectionAssert.AreEqual(new object[] { }, events[1]);
                CollectionAssert.AreEqual(new object[] { }, events[2]);
                events = null;

                (doc2.GetMap("mysubdocs").Get("a") as YDoc).Load();
                CollectionAssert.AreEqual(new object[] { }, events[0]);
                CollectionAssert.AreEqual(new object[] { }, events[1]);
                CollectionAssert.AreEqual(new[] { "a" }, events[2]);
                events = null;

                var guids = doc2.GetSubdocGuids().ToList();
                guids.Sort();
                CollectionAssert.AreEqual(new[] { "a", "c" }, guids);

                doc2.GetMap("mysubdocs").Delete("a");
                CollectionAssert.AreEqual(new object[] { }, events[0]);
                CollectionAssert.AreEqual(new[] { "a" }, events[1]);
                CollectionAssert.AreEqual(new object[] { }, events[2]);
                events = null;

                guids = doc2.GetSubdocGuids().ToList();
                guids.Sort();
                CollectionAssert.AreEqual(new[] { "a", "c" }, guids);
            }
        }
    }
}
