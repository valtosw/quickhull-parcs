using Parcs.Net;

namespace QuickHull.PARCS
{
    public sealed class QuickHullOptions : IModuleOptions
    {
        public int PointsNumber { get; set; } = 1;

        public int PointCount { get; set; } = 100_000;

        public int Seed { get; set; } = 42;

        public int Warmup { get; set; } = 1;

        public int Repetitions { get; set; } = 5;
    }
}
