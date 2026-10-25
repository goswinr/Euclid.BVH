module BenchQueries

open System
open Euclid

// The same workload and timing loop run under .NET and Fable/Node.
#if FABLE_COMPILER
let now () : float = Fable.Core.JsInterop.emitJsExpr () "performance.now()"
let allocated () = -1.0
let collect () : unit = Fable.Core.JsInterop.emitJsExpr () "globalThis.gc?.()"
#else
let now () = float (Diagnostics.Stopwatch.GetTimestamp()) * 1000.0 / float Diagnostics.Stopwatch.Frequency
let allocated () = float (GC.GetAllocatedBytesForCurrentThread())
let collect () =
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
#endif

// Integer arithmetic stays below 2^53 so the generator is identical in JS and .NET.
let generator seed =
    let mutable state = float seed
    fun () ->
        state <- (state * 16807.0) % 2147483647.0
        state / 2147483647.0

// Checksums stay below 2^31 on both runtimes. They are built from distances, pair counts and
// pair indices, never from the index of a closest item, because ties may resolve differently.
let modulus = 1_000_000_007
let addTo (sum: int) (v: int) = (sum + v % modulus) % modulus
let addDist (sum: int) (d: float) = addTo sum (int (floor (d * 1000.0)))

/// Takes up to k entries of a by-distance sequence and returns a checksum of their distances.
let takeSum (k: int) (s: seq<int * float>) =
    let mutable sum = 0
    let mutable taken = 0
    use e = s.GetEnumerator()
    while taken < k && e.MoveNext() do
        let _, d = e.Current
        sum <- addDist sum d
        taken <- taken + 1
    addTo sum taken

let pairSum (pairs: ResizeArray<BVHPair>) =
    let mutable sum = pairs.Count
    for p in pairs do
        sum <- addTo sum (p.IdxA * 7 + p.IdxB)
    sum

/// Prints one CSV row with the median time per unit in nanoseconds.
/// Warms up for at least 350 ms, calibrates a batch to at least 40 ms, then takes 7 samples,
/// or 3 if a batch takes over 300 ms. GC is forced outside the timer before each sample.
let measure (dimensions: int) (distribution: string) (count: int) (operation: string) (units: int) (action: unit -> int) =
    let mutable sink = 0
    let warmupStart = now ()
    while now () - warmupStart < 350.0 do
        sink <- sink ^^^ action ()

    // Always calibrate from a single call up, so that one slow warmup call cannot leave the batch too small.
    let mutable batch = 1
    let mutable elapsed = 0.0
    while elapsed < 40.0 do
        let start = now ()
        for _ = 1 to batch do
            sink <- sink ^^^ action ()
        elapsed <- now () - start
        if elapsed < 40.0 then batch <- batch * 2

    let samples = if elapsed > 300.0 then 3 else 7
    let times = Array.zeroCreate<float> samples
    let allocations = Array.zeroCreate<float> samples
    for sample = 0 to samples - 1 do
        collect ()
        let bytesBefore = allocated ()
        let start = now ()
        for _ = 1 to batch do
            sink <- sink ^^^ action ()
        times.[sample] <- (now () - start) * 1_000_000.0 / float (batch * units)
        allocations.[sample] <- if bytesBefore < 0.0 then -1.0 else (allocated () - bytesBefore) / float (batch * units)
    Array.sortInPlace times
    Array.sortInPlace allocations
    if sink = Int32.MinValue then failwith "Unexpected benchmark checksum"
    printfn "%d,%s,%d,%s,%d,%d,%.1f,%.1f,%.1f,%.1f,%d" dimensions distribution count operation units batch times.[samples / 2] times.[0] times.[samples - 1] allocations.[samples / 2] (action ())

let run (only: string[]) dimensions distribution count queryCount allCount =
    let random = generator (count + dimensions * 1009 + (if distribution = "clustered" then 7 else 0))
    let side = if dimensions = 2 then sqrt (float count) * 8.0 else (float count ** (1.0 / 3.0)) * 8.0
    let position () =
        if distribution = "clustered" then
            let center = if random () < 0.5 then 0.25 else 0.75
            (center + (random () - 0.5) * 0.12) * side
        else random () * side
    // A segment of length 1 to 5 in a random direction, flat in 2D.
    let segment (x: float, y: float, z: float) =
        let dx = random () - 0.5
        let dy = random () - 0.5
        let dz = if dimensions = 2 then 0.0 else random () - 0.5
        let len = 1.0 + random () * 4.0
        let l = max 1e-9 (sqrt (dx * dx + dy * dy + dz * dz))
        x, y, z, x + dx / l * len, y + dy / l * len, z + dz / l * len
    let segments = Array.init count (fun _ -> segment (position (), position (), position ()))
    // Half of the query points are on an input segment, the other half span the full domain.
    let points =
        Array.init queryCount (fun i ->
            if i % 2 = 0 then
                let x, y, z, xx, yy, zz = segments.[int (random () * float count)]
                let t = random ()
                x + (xx - x) * t, y + (yy - y) * t, z + (zz - z) * t
            else random () * side, random () * side, random () * side)
    let querySegments = points |> Array.map segment
    let bench operation units action =
        if only.Length = 0 || Array.contains operation only then
            measure dimensions distribution count operation units action

    if dimensions = 2 then
        let lines = segments |> Array.map (fun (x, y, _, xx, yy, _) -> Line2D(x, y, xx, yy))
        let rects = lines |> Array.map BRect.createFromLine
        let pts = points |> Array.map (fun (x, y, _) -> Pt(x, y))
        let queryLines = querySegments |> Array.map (fun (x, y, _, xx, yy, _) -> Line2D(x, y, xx, yy))
        let rectTree = BVH2D.createFromRects rects
        let lineTree = BVHLine2D.create lines
        bench "build" 1 (fun () -> (BVH2D.createFromRects rects).Count)
        bench "closest-box-pt" queryCount (fun () ->
            let mutable sum = 0
            for p in pts do sum <- addDist sum (snd (rectTree.ClosestRect p))
            sum)
        bench "closest-line-pt" queryCount (fun () ->
            let mutable sum = 0
            for p in pts do sum <- addDist sum (snd (lineTree.ClosestLine p))
            sum)
        bench "closest-line-line" queryCount (fun () ->
            let mutable sum = 0
            for q in queryLines do sum <- addDist sum (snd (lineTree.ClosestLine q))
            sum)
        bench "nearest-neighbors-box" 1 (fun () ->
            let mutable sum = 0
            for p in rectTree.NearestNeighbors () do sum <- addDist sum p.Distance
            sum)
        bench "nearest-neighbors-line" 1 (fun () ->
            let mutable sum = 0
            for p in lineTree.NearestNeighbors () do sum <- addDist sum p.Distance
            sum)
        bench "closest-pair-box" 1 (fun () -> addDist 0 (rectTree.ClosestPair ()).Distance)
        bench "closest-pair-line" 1 (fun () -> addDist 0 (lineTree.ClosestPair ()).Distance)
        bench "close-pairs-box-0" 1 (fun () -> pairSum (rectTree.ClosePairs 0.0))
        bench "close-pairs-box-0.5" 1 (fun () -> pairSum (rectTree.ClosePairs 0.5))
        bench "close-pairs-line-0" 1 (fun () -> pairSum (lineTree.ClosePairs 0.0))
        bench "close-pairs-line-0.5" 1 (fun () -> pairSum (lineTree.ClosePairs 0.5))
        bench "boxes-by-distance-10" queryCount (fun () ->
            let mutable sum = 0
            for p in pts do sum <- addTo sum (takeSum 10 (rectTree.RectsByDistance p))
            sum)
        bench "lines-by-distance-10" queryCount (fun () ->
            let mutable sum = 0
            for p in pts do sum <- addTo sum (takeSum 10 (lineTree.LinesByDistance p))
            sum)
        bench "lines-by-line-distance-10" queryCount (fun () ->
            let mutable sum = 0
            for q in queryLines do sum <- addTo sum (takeSum 10 (lineTree.LinesByDistance q))
            sum)
        bench "boxes-by-distance-all" allCount (fun () ->
            let mutable sum = 0
            for i = 0 to allCount - 1 do sum <- addTo sum (takeSum Int32.MaxValue (rectTree.RectsByDistance pts.[i]))
            sum)
        bench "lines-by-distance-all" allCount (fun () ->
            let mutable sum = 0
            for i = 0 to allCount - 1 do sum <- addTo sum (takeSum Int32.MaxValue (lineTree.LinesByDistance pts.[i]))
            sum)
        bench "lines-by-line-distance-all" allCount (fun () ->
            let mutable sum = 0
            for i = 0 to allCount - 1 do sum <- addTo sum (takeSum Int32.MaxValue (lineTree.LinesByDistance queryLines.[i]))
            sum)
    else
        let lines = segments |> Array.map (fun (x, y, z, xx, yy, zz) -> Line3D(x, y, z, xx, yy, zz))
        let boxes = lines |> Array.map BBox.createFromLine
        let pts = points |> Array.map (fun (x, y, z) -> Pnt(x, y, z))
        let queryLines = querySegments |> Array.map (fun (x, y, z, xx, yy, zz) -> Line3D(x, y, z, xx, yy, zz))
        let boxTree = BVH.createFromBoxes boxes
        let lineTree = BVHLine3D.create lines
        bench "build" 1 (fun () -> (BVH.createFromBoxes boxes).Count)
        bench "closest-box-pt" queryCount (fun () ->
            let mutable sum = 0
            for p in pts do sum <- addDist sum (snd (boxTree.ClosestBox p))
            sum)
        bench "closest-line-pt" queryCount (fun () ->
            let mutable sum = 0
            for p in pts do sum <- addDist sum (snd (lineTree.ClosestLine p))
            sum)
        bench "closest-line-line" queryCount (fun () ->
            let mutable sum = 0
            for q in queryLines do sum <- addDist sum (snd (lineTree.ClosestLine q))
            sum)
        bench "nearest-neighbors-box" 1 (fun () ->
            let mutable sum = 0
            for p in boxTree.NearestNeighbors () do sum <- addDist sum p.Distance
            sum)
        bench "nearest-neighbors-line" 1 (fun () ->
            let mutable sum = 0
            for p in lineTree.NearestNeighbors () do sum <- addDist sum p.Distance
            sum)
        bench "closest-pair-box" 1 (fun () -> addDist 0 (boxTree.ClosestPair ()).Distance)
        bench "closest-pair-line" 1 (fun () -> addDist 0 (lineTree.ClosestPair ()).Distance)
        bench "close-pairs-box-0" 1 (fun () -> pairSum (boxTree.ClosePairs 0.0))
        bench "close-pairs-box-0.5" 1 (fun () -> pairSum (boxTree.ClosePairs 0.5))
        bench "close-pairs-line-0" 1 (fun () -> pairSum (lineTree.ClosePairs 0.0))
        bench "close-pairs-line-0.5" 1 (fun () -> pairSum (lineTree.ClosePairs 0.5))
        bench "boxes-by-distance-10" queryCount (fun () ->
            let mutable sum = 0
            for p in pts do sum <- addTo sum (takeSum 10 (boxTree.BoxesByDistance p))
            sum)
        bench "lines-by-distance-10" queryCount (fun () ->
            let mutable sum = 0
            for p in pts do sum <- addTo sum (takeSum 10 (lineTree.LinesByDistance p))
            sum)
        bench "lines-by-line-distance-10" queryCount (fun () ->
            let mutable sum = 0
            for q in queryLines do sum <- addTo sum (takeSum 10 (lineTree.LinesByDistance q))
            sum)
        bench "boxes-by-distance-all" allCount (fun () ->
            let mutable sum = 0
            for i = 0 to allCount - 1 do sum <- addTo sum (takeSum Int32.MaxValue (boxTree.BoxesByDistance pts.[i]))
            sum)
        bench "lines-by-distance-all" allCount (fun () ->
            let mutable sum = 0
            for i = 0 to allCount - 1 do sum <- addTo sum (takeSum Int32.MaxValue (lineTree.LinesByDistance pts.[i]))
            sum)
        bench "lines-by-line-distance-all" allCount (fun () ->
            let mutable sum = 0
            for i = 0 to allCount - 1 do sum <- addTo sum (takeSum Int32.MaxValue (lineTree.LinesByDistance queryLines.[i]))
            sum)

[<EntryPoint>]
let main args =
    let smoke = Array.contains "--smoke" args
    let counts = if smoke then [| 1_000 |] else [| 10_000; 100_000 |]
    let distributions = if smoke then [| "uniform" |] else [| "uniform"; "clustered" |]
    let queries = if smoke then 256 else 4096
    let allCount = 4
    // --only=build,closest-pair-box runs only the named operations.
    let only =
        args
        |> Array.tryFind (fun a -> a.StartsWith "--only=")
        |> Option.map (fun a -> a.Substring(7).Split(','))
        |> Option.defaultValue [||]
    printfn "dimensions,distribution,count,operation,units,batch,median_ns,min_ns,max_ns,allocated_bytes,checksum"
    for dimensions in [| 2; 3 |] do
        for distribution in distributions do
            for count in counts do
                run only dimensions distribution count queries allCount
    0
