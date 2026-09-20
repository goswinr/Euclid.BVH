module BenchCDev3

open System
open System.Diagnostics
open System.Numerics
open System.Runtime.InteropServices
open Euclid

[<Struct>]
type private CDevRect =
    val Bounds: global.BVH2D.AABB

    new (minX: float32, minY: float32, maxX: float32, maxY: float32) =
        { Bounds = global.BVH2D.AABB(Vector2(minX, minY), Vector2(maxX, maxY)) }

    interface global.BVH2D.IBounded with
        member rect.GetAABB() = rect.Bounds

type private Dataset = {
    EuclidRects: BRect[]
    CDevRects: CDevRect[]
    EuclidPoints: Pt[]
    CDevPoints: Vector2[]
    }

type private Measurement = {
    MedianMilliseconds: float
    MedianAllocatedBytes: float
    Checksum: int
    }

let private forceFullGc () =
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()

let private median (values: float[]) =
    Array.sortInPlace values
    let middle = values.Length / 2
    if values.Length % 2 = 0 then
        (values.[middle - 1] + values.[middle]) * 0.5
    else
        values.[middle]

let private measure samples (action: unit -> int) =
    let milliseconds = Array.zeroCreate<float> samples
    let allocations = Array.zeroCreate<float> samples
    let mutable checksum = 0

    for sample = 0 to samples - 1 do
        forceFullGc ()
        let allocatedBefore = GC.GetAllocatedBytesForCurrentThread()
        let started = Stopwatch.GetTimestamp()
        let sampleChecksum = action ()
        let elapsed = Stopwatch.GetTimestamp() - started
        let allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore
        checksum <- checksum ^^^ sampleChecksum
        milliseconds.[sample] <- float elapsed * 1000.0 / float Stopwatch.Frequency
        allocations.[sample] <- float allocated

    {
        MedianMilliseconds = median milliseconds
        MedianAllocatedBytes = median allocations
        Checksum = checksum
    }

let private makeDataset count queryCount =
    let random = Random(0x5EED + count)
    let side = float32 (sqrt (float count) * 10.0)
    let euclidRects = Array.zeroCreate<BRect> count
    let cDevRects = Array.zeroCreate<CDevRect> count

    for i = 0 to count - 1 do
        let minX = float32 (random.NextDouble()) * side
        let minY = float32 (random.NextDouble()) * side
        let sizeX = 1.0f + float32 (random.NextDouble()) * 8.0f
        let sizeY = 1.0f + float32 (random.NextDouble()) * 8.0f
        let maxX = minX + sizeX
        let maxY = minY + sizeY
        euclidRects.[i] <- BRect.createXY (float minX, float minY, float maxX, float maxY)
        cDevRects.[i] <- CDevRect(minX, minY, maxX, maxY)

    let euclidPoints = Array.zeroCreate<Pt> queryCount
    let cDevPoints = Array.zeroCreate<Vector2> queryCount

    for i = 0 to queryCount - 1 do
        let x, y =
            if i % 2 = 0 then
                // Half the probes are guaranteed to hit at least one rectangle.
                let rect = cDevRects.[random.Next(count)].Bounds
                let x = rect.Min.X + float32 (random.NextDouble()) * (rect.Max.X - rect.Min.X)
                let y = rect.Min.Y + float32 (random.NextDouble()) * (rect.Max.Y - rect.Min.Y)
                x, y
            else
                float32 (random.NextDouble()) * side, float32 (random.NextDouble()) * side

        euclidPoints.[i] <- Pt(float x, float y)
        cDevPoints.[i] <- Vector2(x, y)

    {
        EuclidRects = euclidRects
        CDevRects = cDevRects
        EuclidPoints = euclidPoints
        CDevPoints = cDevPoints
    }

let private buildEuclid (dataset: Dataset) =
    Euclid.BVH2D.createFromRects dataset.EuclidRects

let private buildCDev (dataset: Dataset) =
    global.BVH2D.BVH2d.Build<CDevRect>(dataset.CDevRects)

let private euclidQueryChecksum (tree: Euclid.BVH2D<BRect>) (points: Pt[]) =
    let mutable checksum = 0
    for point in points do
        checksum <- checksum + tree.ItemsNearPoint(point, 0.0).Count
    checksum

let private cDevQueryChecksum (tree: global.BVH2D.BVH2d) (points: Vector2[]) =
    let mutable checksum = 0
    for point in points do
        checksum <- checksum + tree.QueryPoint(point).Count
    checksum

let private verifyEquivalentResults count (dataset: Dataset) (euclidTree: Euclid.BVH2D<BRect>) (cDevTree: global.BVH2D.BVH2d) =
    for i = 0 to dataset.EuclidPoints.Length - 1 do
        let euclidResult = euclidTree.ItemsNearPoint(dataset.EuclidPoints.[i], 0.0).ToArray()
        let cDevResult = cDevTree.QueryPoint(dataset.CDevPoints.[i]).ToArray()
        Array.sortInPlace euclidResult
        Array.sortInPlace cDevResult
        if euclidResult <> cDevResult then
            failwith $"Point-query result mismatch for rectangle count {count}, query {i}."

let private mib bytes = bytes / (1024.0 * 1024.0)

let private printBuildRow count name (measurement: Measurement) =
    printfn "| %s | %d | %.3f | %.3f |" name count measurement.MedianMilliseconds (mib measurement.MedianAllocatedBytes)

let private printQueryRow count queryCount name (measurement: Measurement) =
    let nanosecondsPerQuery = measurement.MedianMilliseconds * 1_000_000.0 / float queryCount
    let bytesPerQuery = measurement.MedianAllocatedBytes / float queryCount
    printfn "| %s | %d | %.1f | %.1f | %d |" name count nanosecondsPerQuery bytesPerQuery measurement.Checksum

[<EntryPoint>]
let main args =
    let quick = Array.contains "--quick" args
    let counts = if quick then [| 1_000; 10_000 |] else [| 1_000; 10_000; 100_000 |]
    let queryCount = if quick then 5_000 else 20_000
    let querySamples = if quick then 3 else 7

    printfn "# Euclid.BVH vs C-dev3/BVH2D"
    printfn ""
    printfn "- Runtime: %s" RuntimeInformation.FrameworkDescription
    printfn "- OS: %s" RuntimeInformation.OSDescription
    printfn "- Logical processors visible: %d" Environment.ProcessorCount
    printfn "- Configuration: Release, in-process, median of warmed samples"
    printfn "- Dataset: deterministic uniform rectangles; half of query points are guaranteed hits"
    printfn "- Euclid.BVH: current project, doubles, default leaf size 4"
    printfn "- C-dev3/BVH2D: NuGet 1.0.0, floats, fixed one-item leaves"
    printfn ""

    let results = ResizeArray<int * int * Measurement * Measurement * Measurement * Measurement>()

    for count in counts do
        let dataset = makeDataset count queryCount
        let buildSamples = if quick then 3 elif count >= 50_000 then 5 else 7
        let warmupCount = if quick then 12 else 40

        // Repeated calls are intentional: they give tiered JIT/PGO enough work to optimize
        // every hot path and also warm C-dev3's shared ArrayPool state.
        let mutable euclidTree = buildEuclid dataset
        let mutable cDevTree = buildCDev dataset
        for _ = 1 to warmupCount do
            euclidTree <- buildEuclid dataset
            cDevTree <- buildCDev dataset
        for _ = 1 to warmupCount do
            euclidQueryChecksum euclidTree dataset.EuclidPoints |> ignore
            cDevQueryChecksum cDevTree dataset.CDevPoints |> ignore

        let euclidBuild =
            measure buildSamples (fun () ->
                euclidTree <- buildEuclid dataset
                euclidTree.Count)

        let cDevBuild =
            measure buildSamples (fun () ->
                cDevTree <- buildCDev dataset
                cDevTree.GetHashCode())

        // Query the final measured trees and validate every result outside timed regions.
        verifyEquivalentResults count dataset euclidTree cDevTree

        let euclidQuery = measure querySamples (fun () -> euclidQueryChecksum euclidTree dataset.EuclidPoints)
        let cDevQuery = measure querySamples (fun () -> cDevQueryChecksum cDevTree dataset.CDevPoints)
        if euclidQuery.Checksum <> cDevQuery.Checksum then
            failwith $"Point-query checksum mismatch for rectangle count {count}."

        results.Add(count, queryCount, euclidBuild, cDevBuild, euclidQuery, cDevQuery)

    printfn "## Build"
    printfn ""
    printfn "| Implementation | Rectangles | Median ms | Allocated MiB/build |"
    printfn "| --- | ---: | ---: | ---: |"
    for count, _, euclidBuild, cDevBuild, _, _ in results do
        printBuildRow count "Euclid.BVH2D" euclidBuild
        printBuildRow count "C-dev3/BVH2D" cDevBuild

    printfn ""
    printfn "## Point containment queries"
    printfn ""
    printfn "Both rows materialize a fresh result list for every query. The checksum is the total hit count combined across samples."
    printfn ""
    printfn "| Implementation | Rectangles | Median ns/query | Allocated B/query | Checksum |"
    printfn "| --- | ---: | ---: | ---: | ---: |"
    for count, queries, _, _, euclidQuery, cDevQuery in results do
        printQueryRow count queries "Euclid.BVH2D" euclidQuery
        printQueryRow count queries "C-dev3/BVH2D" cDevQuery

    0
