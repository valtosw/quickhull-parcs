# Build from the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/QuickHull.Geometry/QuickHull.Geometry.csproj src/QuickHull.Geometry/
COPY tools/QuickHull.Sequential/QuickHull.Sequential.csproj tools/QuickHull.Sequential/
RUN dotnet restore tools/QuickHull.Sequential/QuickHull.Sequential.csproj

COPY src/QuickHull.Geometry/ src/QuickHull.Geometry/
COPY tools/QuickHull.Sequential/ tools/QuickHull.Sequential/
RUN dotnet publish tools/QuickHull.Sequential/QuickHull.Sequential.csproj \
    -c Release \
    -o /publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish .
ENTRYPOINT ["dotnet", "QuickHull.Sequential.dll"]
