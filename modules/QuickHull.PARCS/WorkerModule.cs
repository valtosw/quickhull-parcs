using System.Diagnostics;
using Parcs.Net;
using QuickHull.Geometry;

namespace QuickHull.PARCS
{
    public sealed class WorkerModule : IModule
    {
        public async Task RunAsync(IModuleInfo moduleInfo, CancellationToken cancellationToken = default)
        {
            Point2D[]? points = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                var request = await moduleInfo.Parent.ReadObjectAsync<QuickHullWorkRequest>();


                if (request.Command == QuickHullWorkCommand.Stop)
                {
                    break;
                }


                if (request.Command == QuickHullWorkCommand.Initialize)
                {
                    points = PointSetGenerator.GenerateUniformRange(
                        request.StartIndex,
                        request.PointCount,
                        request.Seed);

                    await moduleInfo.Parent.WriteObjectAsync(new QuickHullWorkResult(
                        QuickHullWorkResultType.Ready,
                        -1,
                        Environment.MachineName,
                        points.Length,
                        [],
                        0));

                    continue;
                }


                if (request.Command != QuickHullWorkCommand.ComputeHull || points is null)
                {
                    throw new InvalidOperationException("The worker must be initialized before it can compute a hull.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                var stopwatch = Stopwatch.StartNew();
                var hull = QuickHullAlgorithm.ComputeHull(points, cancellationToken: cancellationToken).ToArray();
                stopwatch.Stop();

                await moduleInfo.Parent.WriteObjectAsync(new QuickHullWorkResult(
                    QuickHullWorkResultType.Hull,
                    request.Round,
                    Environment.MachineName,
                    points.Length,
                    hull,
                    stopwatch.Elapsed.TotalMilliseconds));
            }
        }
    }
}
