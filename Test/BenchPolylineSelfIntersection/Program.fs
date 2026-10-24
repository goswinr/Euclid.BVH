module BenchPolylineSelfIntersection

open Euclid
open Shared

#if !FABLE_COMPILER
open System.Threading
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Columns
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Running
open BenchmarkDotNet.Toolchains.InProcess.Emit
open Perfolizer.Horology

[<MemoryDiagnoser>]
type SelfIntersection2D() =

    let mutable polyline = Unchecked.defaultof<Polyline2D>

    // Keep in sync with Shared.pointCounts, attribute arguments must be literals.
    [<Params(5, 20, 50, 100, 200, 500, 1_000, 2_000, 10_000)>]
    member val PointCount = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        polyline <- ring this.PointCount

    [<Benchmark(Baseline = true)>]
    member _.TryFindSelfIntersection() =
        Polyline2D.tryFindSelfIntersection polyline

    [<Benchmark>]
    member _.TryFindSelfIntersectionWithBRect() =
        Polyline2D.tryFindSelfIntersectionWithBRect polyline

    [<Benchmark>]
    member _.TryFindSelfIntersectionWithBvh() =
        Polyline2D.tryFindSelfIntersectionWithBvh polyline

[<MemoryDiagnoser>]
type SelfIntersection3D() =

    let mutable polyline = Unchecked.defaultof<Polyline3D>

    // Keep in sync with Shared.pointCounts, attribute arguments must be literals.
    [<Params(5, 20, 50, 100, 200, 500, 1_000, 2_000, 10_000)>]
    member val PointCount = 0 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        polyline <- ring3D this.PointCount

    [<Benchmark(Baseline = true)>]
    member _.BruteForce() =
        tryFindSelfIntersection3D polyline

    [<Benchmark>]
    member _.TryFindSelfIntersectionWithBvh() =
        Polyline3D.tryFindSelfIntersectionWithBvh polyline

/// A call on 10_000 points takes over 100 ms, so the benchmark makes fewer than the 30 calls that
/// .NET needs to promote a method from its quick first JIT tier (or on-stack replacement code for
/// its loops) to fully optimized code, and that slower code would be measured.
/// Calling all methods often on a small ring first gets them to the optimized tier.
/// This works because the benchmarks run in this same process.
let warmUp () =
    let small = ring 50
    let small3D = ring3D 50
    for _ in 1 .. 5 do
        for _ in 1 .. 100 do
            Polyline2D.tryFindSelfIntersection small |> ignore
            Polyline2D.tryFindSelfIntersectionWithBRect small |> ignore
            Polyline2D.tryFindSelfIntersectionWithBvh small |> ignore
            tryFindSelfIntersection3D small3D |> ignore
            Polyline3D.tryFindSelfIntersectionWithBvh small3D |> ignore
        Thread.Sleep 200 // tier promotion compiles in the background

/// Runs in under two minutes instead of the half hour of the default job:
/// in process instead of a separately built process per benchmark, a fixed small count of
/// short iterations, and no overhead stage, which matters only for the nanosecond results.
/// Two warmup iterations are enough, because warmUp already got the JIT to its optimized tier.
/// With so few iterations the median is shown too, it is less affected by a changing CPU clock.
let config =
    DefaultConfig.Instance
        .AddJob(
            Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(2)
                .WithIterationCount(6)
                // just above the 100 ms below which BenchmarkDotNet warns about short iterations
                .WithIterationTime(TimeInterval.FromMilliseconds 120.0)
                .WithEvaluateOverhead(false))
        .AddColumn(StatisticColumn.Median)
#endif

/// On .NET this runs BenchmarkDotNet, or with --portable the same timing loop as on Fable.
/// On Fable JavaScript it always runs that portable timing loop. There --only is used
/// internally, to measure a single method in a fresh child process.
[<EntryPoint>]
let main args =
#if FABLE_COMPILER
    match Array.tryFindIndex ((=) "--only") args with
    | Some i -> Portable.printOne (int args.[i + 1]) (int args.[i + 2])
    | None ->
        verify ()
        Portable.run ()
#else
    verify ()
    warmUp ()
    if Array.contains "--portable" args then
        Portable.run ()
    else
        BenchmarkRunner.Run(typeof<SelfIntersection2D>.Assembly, config, args) |> ignore
#endif
    0
