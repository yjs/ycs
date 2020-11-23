// ------------------------------------------------------------------------------
//  <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
//  </copyright>
// ------------------------------------------------------------------------------

using System;
using BenchmarkDotNet.Attributes;

namespace Ycs.Benchmarks
{
    /// <summary>
    /// Simulate two clients.
    /// One client modifies a text object and sends update messages to the other client.
    /// We measure the time to perform the task (time), the amount of data exchanged (avgUpdateSize),
    /// the size of the encoded document after the task is performed (docSize),
    /// the time to parse the encoded document (parseTime),
    /// and the memory used to hold the decoded document (memUsed).
    /// </summary>
    public class BenchmarkTests
    {
        private const int N = 6_000;

        private readonly Random _rand = new Random();

        [Benchmark]
        public void B1()
        {
            var doc1 = new YDoc();
            var doc2 = new YDoc();

            doc1.UpdateV2 += (s, e) =>
            {
                doc2.ApplyUpdateV2(e.data, doc1);
            };

            for (int i = 0; i < N; i++)
            {
                doc1.GetText("text").Insert(i, GetRandomChar(_rand).ToString());
            }

            doc1.Destroy();
            doc2.Destroy();
        }

        private static char GetRandomChar(Random rand) => (char)rand.Next('A', 'Z' + 1);
    }
}
