# QuickHull with PARCS

This project implements two-dimensional QuickHull in C# in sequential and PARCS versions. The PARCS coordinator assigns disjoint deterministic point ranges to workers. Workers compute local hulls and return only their hull vertices; the coordinator computes the hull of those vertices. The result is checked against the sequential implementation.

## Prerequisites

- .NET 10 SDK for local builds and runs.
- Git to get the pinned PARCS8 source when building the PARCS module or platform images.
- For the cloud setup only: Google Cloud CLI (`gcloud`), `kubectl`, Helm, and Docker Desktop using Linux containers. See [the deployment guide](deploy/README.md).

Run the following commands from the repository root in PowerShell.

## Run the sequential version

```powershell
dotnet run --project tools/QuickHull.Sequential -- --points 100000 --seed 42 --warmup 1 --repetitions 5
```

The program prints JSON with the measured durations, median, hull size, and vertices. Point generation and the warmup are outside the measured QuickHull rounds. Change `--points`, `--seed`, `--warmup`, or `--repetitions` to select another input or measurement setup.

## Build the PARCS module

The project pins PARCS8 commit `d35d29f948cb1849a070967e681bc0df7f25d779`. That source provides the `Parcs.Net` 10.1.0 package used by the module; build it into a local NuGet feed before publishing:

```powershell
git clone https://github.com/alexeybogusevich/parcs8 third_party/parcs8
git -C third_party/parcs8 checkout d35d29f948cb1849a070967e681bc0df7f25d779
dotnet pack third_party/parcs8/src/Parcs.Net/Parcs.Net.csproj -c Release -o artifacts/nuget -p:GeneratePackageOnBuild=false

$sources = dotnet nuget list source
if ($sources -notmatch 'parcs-local') {
    dotnet nuget add source "$PWD/artifacts/nuget" --name parcs-local
}

dotnet publish modules/QuickHull.PARCS/QuickHull.PARCS.csproj -c Release
```

The module files are in `modules/QuickHull.PARCS/bin/Release/net10.0/publish/`. Upload `QuickHull.PARCS.dll` and its `.deps.json` file to the PARCS Portal. Create a job with assembly `QuickHull.PARCS`, class `QuickHull.PARCS.MainModule`, and JSON options such as:

```json
{"PointsNumber":4,"PointCount":100000,"Seed":42,"Warmup":1,"Repetitions":5}
```

`PointsNumber` is the number of workers and must not exceed the number of ready PARCS daemons. The job writes `timings.json` and `hull.json` to its PARCS output. Repeat with `PointsNumber` set to 1, 2, 3, and 4 to compare worker counts.

## Cloud deployment and experiments

Follow [deploy/README.md](deploy/README.md) to create the GKE cluster, build and publish the PARCS and sequential images, deploy PARCS, upload the module, and run the sequential comparison. It includes the commands to retrieve logs and clean up cloud resources.
