using System.Diagnostics;
using System.Text.Json;
using Parcs.Net;
using QuickHull.Geometry;

namespace QuickHull.PARCS
{
    public sealed class MainModule : IModule
    {
        private const int MinimumWorkerCount = 1;
        private const int MinimumRepetitionCount = 1;

        public async Task RunAsync(IModuleInfo moduleInfo, CancellationToken cancellationToken = default)
        {
            var options = moduleInfo.BindModuleOptions<QuickHullOptions>();
            ValidateOptions(options);

            var setupStopwatch = Stopwatch.StartNew();
            var workerPoints = new List<IPoint>(options.PointsNumber);
            var channels = new List<IChannel>(options.PointsNumber);

            try
            {

                for (var workerIndex = 0; workerIndex < options.PointsNumber; workerIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var point = await moduleInfo.CreatePointAsync();
                    workerPoints.Add(point);
                    var channel = await point.CreateChannelAsync();
                    channels.Add(channel);
                    await point.ExecuteClassAsync<WorkerModule>();
                }

                var initializationRequests = CreateInitializationRequests(options);
                await Task.WhenAll(channels.Select((channel, workerIndex) =>
                    channel.WriteObjectAsync(initializationRequests[workerIndex]).AsTask()));

                var readyResponses = await Task.WhenAll(channels.Select(channel =>
                    channel.ReadObjectAsync<QuickHullWorkResult>()));
                ValidateReadyResponses(readyResponses, initializationRequests);
                setupStopwatch.Stop();

                var rounds = new List<QuickHullRoundTiming>(options.Warmup + options.Repetitions);
                var totalRoundCount = options.Warmup + options.Repetitions;
                IReadOnlyList<Point2D> finalHull = [];

                for (var round = 0; round < totalRoundCount; round++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var isWarmup = round < options.Warmup;
                    var result = await RunRoundAsync(channels, round, isWarmup, cancellationToken);
                    rounds.Add(result.Timing);


                    if (!isWarmup)
                    {
                        finalHull = result.Hull;
                    }
                }

                var measuredRounds = rounds.Where(round => !round.Warmup).ToArray();
                var points = PointSetGenerator.GenerateUniform(options.PointCount, options.Seed);
                ValidateHull(points, finalHull, cancellationToken);
                var report = new QuickHullRunReport(
                    "parcs",
                    options.PointCount,
                    options.Seed,
                    channels.Count,
                    options.Warmup,
                    options.Repetitions,
                    setupStopwatch.Elapsed.TotalMilliseconds,
                    CalculateMedian(measuredRounds.Select(round => round.ElapsedMilliseconds)),
                    rounds);

                await moduleInfo.OutputWriter.WriteToFileAsync(
                    JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions),
                    "timings.json");
                await moduleInfo.OutputWriter.WriteToFileAsync(
                    JsonSerializer.SerializeToUtf8Bytes(finalHull, JsonOptions),
                    "hull.json");
            }
            finally
            {
                await StopWorkersAsync(channels, workerPoints);
            }
        }

        private static JsonSerializerOptions JsonOptions => new() { WriteIndented = true };

        private static QuickHullWorkRequest[] CreateInitializationRequests(QuickHullOptions options)
        {
            var requests = new QuickHullWorkRequest[options.PointsNumber];
            var pointsPerWorker = options.PointCount / options.PointsNumber;
            var remainder = options.PointCount % options.PointsNumber;
            var startIndex = 0;

            for (var workerIndex = 0; workerIndex < options.PointsNumber; workerIndex++)
            {
                var pointCount = pointsPerWorker + (workerIndex < remainder ? 1 : 0);
                requests[workerIndex] = new QuickHullWorkRequest(
                    QuickHullWorkCommand.Initialize,
                    Seed: options.Seed,
                    StartIndex: startIndex,
                    PointCount: pointCount);
                startIndex += pointCount;
            }

            return requests;
        }

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

        private static void ValidateOptions(QuickHullOptions options)
        {

            if (options.PointsNumber < MinimumWorkerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(options.PointsNumber), "At least one worker point is required.");
            }

            ArgumentOutOfRangeException.ThrowIfNegative(options.PointCount);
            ArgumentOutOfRangeException.ThrowIfNegative(options.Warmup);
            ArgumentOutOfRangeException.ThrowIfLessThan(options.Repetitions, MinimumRepetitionCount);
        }

        private static void ValidateReadyResponses(
            IReadOnlyList<QuickHullWorkResult> responses,
            IReadOnlyList<QuickHullWorkRequest> requests)
        {

            for (var workerIndex = 0; workerIndex < responses.Count; workerIndex++)
            {

                if (responses[workerIndex].Type != QuickHullWorkResultType.Ready
                    || responses[workerIndex].PointCount != requests[workerIndex].PointCount)
                {
                    throw new InvalidOperationException($"Worker {workerIndex + 1} did not initialize its assigned point range.");
                }
            }
        }

        private static void ValidateHull(
            IReadOnlyList<Point2D> points,
            IReadOnlyList<Point2D> parallelHull,
            CancellationToken cancellationToken)
        {
            var sequentialHull = QuickHullAlgorithm.ComputeHull(points, cancellationToken: cancellationToken);


            if (!new HashSet<Point2D>(sequentialHull).SetEquals(parallelHull))
            {
                throw new InvalidOperationException("The parallel hull does not match the sequential QuickHull result.");
            }
        }

        private static async Task<RoundResult> RunRoundAsync(
            IReadOnlyList<IChannel> channels,
            int round,
            bool isWarmup,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var requests = channels.Select(_ => new QuickHullWorkRequest(
                QuickHullWorkCommand.ComputeHull,
                Round: round)).ToArray();

            await Task.WhenAll(channels.Select((channel, workerIndex) =>
                channel.WriteObjectAsync(requests[workerIndex]).AsTask()));

            cancellationToken.ThrowIfCancellationRequested();
            var results = await Task.WhenAll(channels.Select(channel =>
                channel.ReadObjectAsync<QuickHullWorkResult>()));

            for (var workerIndex = 0; workerIndex < results.Length; workerIndex++)
            {

                if (results[workerIndex].Type != QuickHullWorkResultType.Hull
                    || results[workerIndex].Round != round)
                {
                    throw new InvalidOperationException($"Worker {workerIndex + 1} returned an invalid hull response for round {round}.");
                }
            }

            var candidateHullPoints = results.SelectMany(result => result.Hull).ToArray();
            var hull = QuickHullAlgorithm.ComputeHull(candidateHullPoints, cancellationToken: cancellationToken);
            stopwatch.Stop();

            var workerTimings = results.Select(result => new QuickHullWorkerTiming(
                result.WorkerName,
                result.PointCount,
                result.Hull.Length,
                result.ElapsedMilliseconds)).ToArray();

            return new RoundResult(
                new QuickHullRoundTiming(round, isWarmup, stopwatch.Elapsed.TotalMilliseconds, hull.Count, workerTimings),
                hull);
        }

        private static async Task StopWorkersAsync(IReadOnlyList<IChannel> channels, IReadOnlyList<IPoint> points)
        {
            try
            {
                await Task.WhenAll(channels.Select(channel => channel.WriteObjectAsync(
                    new QuickHullWorkRequest(QuickHullWorkCommand.Stop)).AsTask()));
            }
            finally
            {
                foreach (var point in points)
                {
                    await point.DeleteAsync();
                }
            }
        }

        private sealed record RoundResult(QuickHullRoundTiming Timing, IReadOnlyList<Point2D> Hull);
    }
}
