using System.Diagnostics;
using System.Text.Json;
using QuickHull.Geometry;

namespace QuickHull.Sequential
{
    internal static class Program
    {
        private const int DefaultPointCount = 100_000;
        private const int DefaultSeed = 42;
        private const int DefaultWarmupCount = 1;
        private const int DefaultRepetitionCount = 5;

        private static int Main(string[] args)
        {
            try
            {
                var options = ParseOptions(args);
                var points = PointSetGenerator.GenerateUniform(options.PointCount, options.Seed);

                for (var warmup = 0; warmup < options.WarmupCount; warmup++)
                {
                    QuickHullAlgorithm.ComputeHull(points);
                }

                var timings = new List<double>(options.RepetitionCount);
                IReadOnlyList<Point2D> hull = [];

                for (var repetition = 0; repetition < options.RepetitionCount; repetition++)
                {
                    var stopwatch = Stopwatch.StartNew();
                    hull = QuickHullAlgorithm.ComputeHull(points);
                    stopwatch.Stop();
                    timings.Add(stopwatch.Elapsed.TotalMilliseconds);
                }

                var report = new SequentialRunReport(
                    "sequential",
                    options.PointCount,
                    options.Seed,
                    options.WarmupCount,
                    timings,
                    CalculateMedian(timings),
                    hull.Count,
                    hull);
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));

                return 0;
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);

                return 2;
            }
        }

        private static JsonSerializerOptions JsonOptions => new() { WriteIndented = true };

        private static double CalculateMedian(IEnumerable<double> values)
        {
            var orderedValues = values.Order().ToArray();
            var middleIndex = orderedValues.Length / 2;

            if (orderedValues.Length % 2 == 1)
            {
                return orderedValues[middleIndex];
            }

            return (orderedValues[middleIndex - 1] + orderedValues[middleIndex]) / 2;
        }

        private static SequentialOptions ParseOptions(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < args.Length; index += 2)
            {

                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Arguments must use --name value pairs.");
                }

                values[args[index][2..]] = args[index + 1];
            }

            var pointCount = ReadInteger(values, "points", DefaultPointCount, 0);
            var seed = ReadInteger(values, "seed", DefaultSeed, int.MinValue);
            var warmupCount = ReadInteger(values, "warmup", DefaultWarmupCount, 0);
            var repetitionCount = ReadInteger(values, "repetitions", DefaultRepetitionCount, 1);

            return new SequentialOptions(pointCount, seed, warmupCount, repetitionCount);
        }

        private static int ReadInteger(Dictionary<string, string> values, string name, int defaultValue, int minimum)
        {

            if (!values.TryGetValue(name, out var text))
            {
                return defaultValue;
            }

            if (!int.TryParse(text, out var value) || value < minimum)
            {
                throw new ArgumentException($"Option --{name} must be an integer greater than or equal to {minimum}.");
            }

            return value;
        }

        private sealed record SequentialOptions(int PointCount, int Seed, int WarmupCount, int RepetitionCount);

        private sealed record SequentialRunReport(
            string Method,
            int PointCount,
            int Seed,
            int WarmupCount,
            IReadOnlyList<double> ElapsedMilliseconds,
            double MedianMilliseconds,
            int HullVertexCount,
            IReadOnlyList<Point2D> Hull);
    }
}
