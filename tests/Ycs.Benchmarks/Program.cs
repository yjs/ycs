using System;
using BenchmarkDotNet.Running;

namespace Ycs.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkRunner.Run<BenchmarkTests>();
            Console.In.ReadLine();
        }
    }
}
