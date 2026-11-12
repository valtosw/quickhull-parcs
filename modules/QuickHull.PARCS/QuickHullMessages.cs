using QuickHull.Geometry;

namespace QuickHull.PARCS
{
    public enum QuickHullWorkCommand
    {
        Initialize,
        ComputeHull,
        Stop
    }

    public enum QuickHullWorkResultType
    {
        Ready,
        Hull
    }

    public sealed record QuickHullWorkRequest(
        QuickHullWorkCommand Command,
        int Round = 0,
        int Seed = 0,
        int StartIndex = 0,
        int PointCount = 0);

    public sealed record QuickHullWorkResult(
        QuickHullWorkResultType Type,
        int Round,
        string WorkerName,
        int PointCount,
        Point2D[] Hull,
        double ElapsedMilliseconds);

    public sealed record QuickHullRoundTiming(
        int Round,
        bool Warmup,
        double ElapsedMilliseconds,
        int HullVertexCount,
        IReadOnlyList<QuickHullWorkerTiming> Workers);

    public sealed record QuickHullWorkerTiming(
        string WorkerName,
        int PointCount,
        int HullVertexCount,
        double ComputeMilliseconds);

    public sealed record QuickHullRunReport(
        string Method,
        int PointCount,
        int Seed,
        int WorkerCount,
        int WarmupCount,
        int RepetitionCount,
        double SetupMilliseconds,
        double MedianMilliseconds,
        IReadOnlyList<QuickHullRoundTiming> Rounds);
}
