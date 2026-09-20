module PortableBenchmark

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

let measure dimensions distribution count queries operation units (action: unit -> int) =
    let mutable sink = 0
    let warmupStart = now ()
    while now () - warmupStart < 350.0 do
        sink <- sink ^^^ action ()

    let mutable batch = 1
    let mutable elapsed = 0.0
    while elapsed < 40.0 do
        let start = now ()
        for _ = 1 to batch do
            sink <- sink ^^^ action ()
        elapsed <- now () - start
        if elapsed < 40.0 then batch <- batch * 2

    let times = Array.zeroCreate<float> 7
    let allocations = Array.zeroCreate<float> 7
    for sample = 0 to 6 do
        collect ()
        let bytesBefore = allocated ()
        let start = now ()
        for _ = 1 to batch do
            sink <- sink ^^^ action ()
        times.[sample] <- (now () - start) * 1_000_000.0 / float (batch * units)
        allocations.[sample] <- if bytesBefore < 0.0 then -1.0 else (allocated () - bytesBefore) / float (batch * units)
    Array.sortInPlace times
    Array.sortInPlace allocations
    // Keep consumption observable, and print a batch-independent correctness checksum.
    if sink = Int32.MinValue then failwith "Unexpected benchmark checksum"
    printfn "%d,%s,%d,%d,%s,%d,%.3f,%.3f,%.3f,%.3f,%d" dimensions distribution count queries operation batch times.[3] times.[0] times.[6] allocations.[3] (action ())

let run dimensions distribution count queryCount buildOnly =
    let random = generator (count + dimensions * 1009)
    let side = if dimensions = 2 then sqrt (float count) * 8.0 else (float count ** (1.0 / 3.0)) * 8.0
    let position () =
        if distribution = "clustered" then
            let center = if random () < 0.5 then 0.25 else 0.75
            (center + (random () - 0.5) * 0.12) * side
        else random () * side
    let bounds =
        Array.init count (fun _ ->
            let x, y, z = position (), position (), position ()
            let sx, sy, sz = 1.0 + random () * 4.0, 1.0 + random () * 4.0, 1.0 + random () * 4.0
            x, y, z, x + sx, y + sy, z + sz)
    let points =
        Array.init queryCount (fun i ->
            if i % 2 = 0 then
                let x, y, z, xx, yy, zz = bounds.[int (random () * float count)]
                x + (xx - x) * random (), y + (yy - y) * random (), z + (zz - z) * random ()
            else random () * side, random () * side, random () * side)
    let bench operation units action = measure dimensions distribution count queryCount operation units action
    if dimensions = 2 then
        let rects = bounds |> Array.map (fun (x, y, _, xx, yy, _) -> BRect.createXY(x, y, xx, yy))
        let pts = points |> Array.map (fun (x, y, _) -> Pt(x, y))
        let ranges = pts |> Array.map (fun p -> BRect.createXY(p.X - 1.0, p.Y - 1.0, p.X + 1.0, p.Y + 1.0))
        let tree = BVH2D.createFromRects rects
        bench "build" 1 (fun () -> (BVH2D.createFromRects rects).Count)
        for tolerance in (if buildOnly then [||] else [| 0.0; 0.5 |]) do
            // Verify a deterministic sample against an independent linear scan, outside timing.
            for i = 0 to min 31 (queryCount - 1) do
                let p = pts.[i]
                let q = ranges.[i]
                let pointExpected = ResizeArray<int>()
                let rangeExpected = ResizeArray<int>()
                for j = 0 to count - 1 do
                    let r = rects.[j]
                    let dx = max 0.0 (max (r.MinX - p.X) (p.X - r.MaxX))
                    let dy = max 0.0 (max (r.MinY - p.Y) (p.Y - r.MaxY))
                    if dx * dx + dy * dy <= tolerance * tolerance then pointExpected.Add j
                    let rx = max 0.0 (max (r.MinX - q.MaxX) (q.MinX - r.MaxX))
                    let ry = max 0.0 (max (r.MinY - q.MaxY) (q.MinY - r.MaxY))
                    if rx * rx + ry * ry <= tolerance * tolerance then rangeExpected.Add j
                if (tree.ItemsNearPoint(p, tolerance).ToArray() |> Array.sort) <> pointExpected.ToArray() then failwith "2D point mismatch"
                if (tree.ItemsInRect(q, tolerance).ToArray() |> Array.sort) <> rangeExpected.ToArray() then failwith "2D range mismatch"
            let suffix = if tolerance = 0.0 then "zero" else "positive"
            bench ("point-" + suffix) queryCount (fun () ->
                let mutable hits = 0
                for p in pts do hits <- hits + tree.ItemsNearPoint(p, tolerance).Count
                hits)
            bench ("range-" + suffix) queryCount (fun () ->
                let mutable hits = 0
                for q in ranges do hits <- hits + tree.ItemsInRect(q, tolerance).Count
                hits)
    else
        let boxes = bounds |> Array.map (fun (x, y, z, xx, yy, zz) -> BBox.createUnchecked(x, y, z, xx, yy, zz))
        let pts = points |> Array.map (fun (x, y, z) -> Pnt(x, y, z))
        let ranges = pts |> Array.map (fun p -> BBox.createUnchecked(p.X - 1.0, p.Y - 1.0, p.Z - 1.0, p.X + 1.0, p.Y + 1.0, p.Z + 1.0))
        let tree = BVH.createFromBoxes boxes
        bench "build" 1 (fun () -> (BVH.createFromBoxes boxes).Count)
        for tolerance in (if buildOnly then [||] else [| 0.0; 0.5 |]) do
            for i = 0 to min 31 (queryCount - 1) do
                let p = pts.[i]
                let q = ranges.[i]
                let pointExpected = ResizeArray<int>()
                let rangeExpected = ResizeArray<int>()
                for j = 0 to count - 1 do
                    let b = boxes.[j]
                    let dx = max 0.0 (max (b.MinX - p.X) (p.X - b.MaxX))
                    let dy = max 0.0 (max (b.MinY - p.Y) (p.Y - b.MaxY))
                    let dz = max 0.0 (max (b.MinZ - p.Z) (p.Z - b.MaxZ))
                    if dx * dx + dy * dy + dz * dz <= tolerance * tolerance then pointExpected.Add j
                    let rx = max 0.0 (max (b.MinX - q.MaxX) (q.MinX - b.MaxX))
                    let ry = max 0.0 (max (b.MinY - q.MaxY) (q.MinY - b.MaxY))
                    let rz = max 0.0 (max (b.MinZ - q.MaxZ) (q.MinZ - b.MaxZ))
                    if rx * rx + ry * ry + rz * rz <= tolerance * tolerance then rangeExpected.Add j
                if (tree.ItemsNearPoint(p, tolerance).ToArray() |> Array.sort) <> pointExpected.ToArray() then failwith "3D point mismatch"
                if (tree.ItemsInBox(q, tolerance).ToArray() |> Array.sort) <> rangeExpected.ToArray() then failwith "3D range mismatch"
            let suffix = if tolerance = 0.0 then "zero" else "positive"
            bench ("point-" + suffix) queryCount (fun () ->
                let mutable hits = 0
                for p in pts do hits <- hits + tree.ItemsNearPoint(p, tolerance).Count
                hits)
            bench ("range-" + suffix) queryCount (fun () ->
                let mutable hits = 0
                for q in ranges do hits <- hits + tree.ItemsInBox(q, tolerance).Count
                hits)

[<EntryPoint>]
let main args =
    let smoke = Array.contains "--smoke" args
    let buildOnly = Array.contains "--build-only" args
    let counts = if smoke then [| 1_000 |] else [| 10_000; 100_000 |]
    let distributions = if smoke then [| "uniform" |] else [| "uniform"; "clustered" |]
    let queries = if smoke then 256 else 4096
    printfn "dimensions,distribution,count,queries,operation,batch,median_ns,min_ns,max_ns,allocated_bytes,checksum"
    for dimensions in [| 2; 3 |] do
        for distribution in distributions do
            for count in counts do
                run dimensions distribution count queries buildOnly
    0
