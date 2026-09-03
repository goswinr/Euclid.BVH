module TestBvh2d

open Euclid
open System

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
open Fable.Mocha
#else
open Expecto
#endif

/// A deterministic pseudo random generator so tests are repeatable.
let private rand = Random 4242

/// The squared distance between two axis aligned bounding rectangles, 0.0 if they overlap.
let private sqRectDist (a: BRect) (b: BRect) =
    let axis aMin aMax bMin bMax =
        if bMin > aMax then bMin - aMax
        elif aMin > bMax then aMin - bMax
        else 0.0
    let dx = axis a.MinX a.MaxX b.MinX b.MaxX
    let dy = axis a.MinY a.MaxY b.MinY b.MaxY
    dx * dx + dy * dy

let private rectDistance (a: BRect) (b: BRect) = sqrt (sqRectDist a b)

/// The distance between a point and an axis aligned bounding rectangle, 0.0 if the point is inside.
let private ptRectDistance (p: Pt) (r: BRect) =
    let axis v rMin rMax =
        if v < rMin then rMin - v
        elif v > rMax then v - rMax
        else 0.0
    let dx = axis p.X r.MinX r.MaxX
    let dy = axis p.Y r.MinY r.MaxY
    sqrt (dx * dx + dy * dy)

/// Creates random small rectangles, clustered unevenly in the plane to mimic real world input.
let private randomRects (count: int) : BRect[] =
    Array.init count (fun _ ->
        let cx = rand.NextDouble() * 100.0
        let cy = rand.NextDouble() * 100.0
        // cluster by rounding centers to a coarse grid on some rectangles:
        let cx = if rand.NextDouble() < 0.5 then Math.Round(cx / 25.0) * 25.0 + rand.NextDouble() * 3.0 else cx
        let cy = if rand.NextDouble() < 0.5 then Math.Round(cy / 25.0) * 25.0 + rand.NextDouble() * 3.0 else cy
        let sx = rand.NextDouble() * 2.0
        let sy = rand.NextDouble() * 2.0
        BRect.createXY (cx, cy, cx + sx, cy + sy))

/// Brute force nearest neighbor rectangle of rectangle i.
let private bruteNearest (rects: BRect[]) (i: int) : int * float =
    let mutable bestJ = -1
    let mutable bestD = Double.MaxValue
    for j = 0 to rects.Length - 1 do
        if j <> i then
            let d = rectDistance rects.[i] rects.[j]
            if d < bestD then
                bestD <- d
                bestJ <- j
    bestJ, bestD

/// Brute force all pairs of rectangles closer than maxDist.
let private brutePairs (rects: BRect[]) (maxDist: float) : Set<int * int> =
    let mutable result = Set.empty
    for i = 0 to rects.Length - 1 do
        for j = i + 1 to rects.Length - 1 do
            if rectDistance rects.[i] rects.[j] <= maxDist then
                result <- result.Add (i, j)
    result

/// A custom item type to test the generic build with a rectangle function.
type private Disk = { Center: Pt; Radius: float }

let private diskRect (d: Disk) : BRect =
    BRect.createFromCenter (d.Center, 2.0 * d.Radius, 2.0 * d.Radius)

/// The exact squared distance between the outlines of two disks (0.0 if they intersect).
let private diskSqDist (a: Disk) (b: Disk) : float =
    let d = max 0.0 (a.Center.DistanceTo b.Center - a.Radius - b.Radius)
    d * d

let private randomDisks (count: int) : Disk[] =
    Array.init count (fun _ ->
        { Center = Pt (rand.NextDouble() * 100.0, rand.NextDouble() * 100.0)
          Radius = rand.NextDouble() * 1.5 })

let tests =
    testList "Bvh2d" [
        test "build evaluates every bounding rectangle once" {
            let mutable calls = 0
            let rects = [| BRect.createXY (0., 0., 1., 1.); BRect.createXY (2., 0., 3., 1.) |]
            Bvh2d.create (rects, fun rect -> calls <- calls + 1; rect) |> ignore
            Expect.equal calls rects.Length "bounding rectangle function is called once per item"
        }

        test "closest rectangle matches planar distance" {
            let rects =
                [| BRect.createXY (0., 0., 1., 1.)
                   BRect.createXY (10., 0., 12., 1.)
                   BRect.createXY (4., 5., 6., 7.) |]
            let bvh = Bvh2d.createFromRects rects
            let query = BRect.createXY (2., 0., 3., 1.)
            let struct (idx, distance) = bvh.ClosestRect query
            Expect.equal idx 0 "first rectangle is closest"
            Expect.floatClose Accuracy.high distance (rectDistance query rects.[0]) "distance is planar"
            Expect.isTrue (bvh.Rectangle.Contains rects.[0]) "tree rectangle contains items"
        }

        test "rectangle pairs and point queries are planar" {
            let rects =
                [| BRect.createXY (0., 0., 1., 1.)
                   BRect.createXY (1.5, 0., 2.5, 1.)
                   BRect.createXY (10., 0., 11., 1.) |]
            let bvh = Bvh2d.createFromRects rects
            let pairs = bvh.ClosePairs 0.5 |> Seq.map (fun p -> p.IdxA, p.IdxB) |> Set.ofSeq
            Expect.equal pairs (Set.singleton (0, 1)) "only the nearby pair is returned"
            let found = bvh.ItemsNearPoint (Pt (2., 0.5), 0.0) |> Set.ofSeq
            Expect.equal found (Set.singleton 1) "point query finds containing rectangle"
        }

        test "the tree rectangle is the union of all item rectangles" {
            let rects = randomRects 200
            let bvh = Bvh2d.createFromRects rects
            let all = rects |> Array.reduce (fun a b -> a.Union b)
            Expect.floatClose Accuracy.high bvh.Rectangle.MinX all.MinX "MinX of the tree rectangle"
            Expect.floatClose Accuracy.high bvh.Rectangle.MinY all.MinY "MinY of the tree rectangle"
            Expect.floatClose Accuracy.high bvh.Rectangle.MaxX all.MaxX "MaxX of the tree rectangle"
            Expect.floatClose Accuracy.high bvh.Rectangle.MaxY all.MaxY "MaxY of the tree rectangle"
            Expect.equal bvh.Count rects.Length "the count of items"
            Expect.equal bvh.Rects.Length rects.Length "one rectangle per item"
        }

        test "closest rectangle to a query rectangle matches brute force" {
            let rects = randomRects 300
            let bvh = Bvh2d.createFromRects rects
            for _ = 1 to 50 do
                let x = rand.NextDouble() * 120.0 - 10.0
                let y = rand.NextDouble() * 120.0 - 10.0
                let query = BRect.createXY (x, y, x + 1.0, y + 1.0)
                let struct (idx, d) = bvh.ClosestRect query
                let mutable bestD = Double.MaxValue
                for i = 0 to rects.Length - 1 do
                    bestD <- min bestD (rectDistance query rects.[i])
                Expect.floatClose Accuracy.high d bestD "the closest rectangle distance"
                Expect.floatClose Accuracy.high (rectDistance query rects.[idx]) bestD "the reported index is at that distance"
        }

        test "closest rectangle to a query point matches brute force" {
            let rects = randomRects 300
            let bvh = Bvh2d.createFromRects rects
            for _ = 1 to 50 do
                let pt = Pt (rand.NextDouble() * 120.0 - 10.0, rand.NextDouble() * 120.0 - 10.0)
                let struct (idx, d) = bvh.ClosestRect pt
                let mutable bestD = Double.MaxValue
                for i = 0 to rects.Length - 1 do
                    bestD <- min bestD (ptRectDistance pt rects.[i])
                Expect.floatClose Accuracy.high d bestD "the closest rectangle distance to the point"
                Expect.floatClose Accuracy.high (ptRectDistance pt rects.[idx]) bestD "the reported index is at that distance"
        }

        test "leaf size does not change the query results" {
            let rects = randomRects 250
            let small = Bvh2d.createFromRects (rects, 1)
            let big = Bvh2d.createFromRects (rects, 16)
            for _ = 1 to 30 do
                let pt = Pt (rand.NextDouble() * 120.0 - 10.0, rand.NextDouble() * 120.0 - 10.0)
                let struct (_, dSmall) = small.ClosestRect pt
                let struct (_, dBig) = big.ClosestRect pt
                Expect.floatClose Accuracy.high dSmall dBig "the same distance for any leaf size"
            let inRect = BRect.createXY (10., 10., 40., 40.)
            let a = small.ItemsInRect inRect |> Set.ofSeq
            let b = big.ItemsInRect inRect |> Set.ofSeq
            Expect.equal a b "the same items in a rectangle for any leaf size"
        }

        test "nearest neighbors and closest pair match brute force" {
            let rects = randomRects 200
            let bvh = Bvh2d.createFromRects rects
            let nn = bvh.NearestNeighbors ()
            Expect.equal nn.Length rects.Length "one neighbor per item"
            for i = 0 to rects.Length - 1 do
                let _, bestD = bruteNearest rects i
                Expect.equal nn.[i].IdxA i "IdxA is the item itself"
                Expect.floatClose Accuracy.high nn.[i].Distance bestD "the nearest neighbor distance"
                Expect.floatClose Accuracy.high (rectDistance rects.[i] rects.[nn.[i].IdxB]) bestD "the reported neighbor is at that distance"
            let pair = bvh.ClosestPair ()
            let mutable bestD = Double.MaxValue
            for i = 0 to rects.Length - 1 do
                for j = i + 1 to rects.Length - 1 do
                    bestD <- min bestD (rectDistance rects.[i] rects.[j])
            Expect.floatClose Accuracy.high pair.Distance bestD "the closest pair distance"
            Expect.isTrue (pair.IdxA < pair.IdxB) "the pair indices are ordered"
        }

        test "close pairs match brute force" {
            let rects = randomRects 200
            let bvh = Bvh2d.createFromRects rects
            for maxDist in [ 0.0; 1.0; 5.0 ] do
                let found = bvh.ClosePairs maxDist |> Seq.map (fun p -> p.IdxA, p.IdxB) |> Set.ofSeq
                Expect.equal found (brutePairs rects maxDist) $"all pairs closer than {maxDist}"
        }

        test "items in a rectangle and near a point match brute force" {
            let rects = randomRects 250
            let bvh = Bvh2d.createFromRects rects
            let query = BRect.createXY (20., 20., 45., 60.)
            for tol in [ 0.0; 2.5 ] do
                let found = bvh.ItemsInRect (query, tol) |> Set.ofSeq
                let expected =
                    seq { for i = 0 to rects.Length - 1 do
                            if rectDistance query rects.[i] <= tol then i }
                    |> Set.ofSeq
                Expect.equal found expected $"items within {tol} of the query rectangle"
            let pt = Pt (33., 47.)
            for tol in [ 0.0; 4.0 ] do
                let found = bvh.ItemsNearPoint (pt, tol) |> Set.ofSeq
                let expected =
                    seq { for i = 0 to rects.Length - 1 do
                            if ptRectDistance pt rects.[i] <= tol then i }
                    |> Set.ofSeq
                Expect.equal found expected $"items within {tol} of the query point"
        }

        test "exact distance queries on a custom item type" {
            let disks = randomDisks 150
            let bvh = Bvh2d.create (disks, diskRect)
            // the closest disk to a point, measured to the disk outline:
            for _ = 1 to 20 do
                let pt = Pt (rand.NextDouble() * 100.0, rand.NextDouble() * 100.0)
                let sqDistTo (d: Disk) = let v = max 0.0 (d.Center.DistanceTo pt - d.Radius) in v * v
                let struct (idx, d) = bvh.ClosestItem (pt, sqDistTo)
                let mutable bestD = Double.MaxValue
                for i = 0 to disks.Length - 1 do
                    bestD <- min bestD (sqrt (sqDistTo disks.[i]))
                Expect.floatClose Accuracy.high d bestD "the closest disk outline distance"
                Expect.floatClose Accuracy.high (sqrt (sqDistTo disks.[idx])) bestD "the reported index is at that distance"
            // the closest pair of disks, measured between the outlines:
            let pair = bvh.ClosestPair diskSqDist
            let mutable bestD = Double.MaxValue
            for i = 0 to disks.Length - 1 do
                for j = i + 1 to disks.Length - 1 do
                    bestD <- min bestD (sqrt (diskSqDist disks.[i] disks.[j]))
            Expect.floatClose Accuracy.high pair.Distance bestD "the closest pair of disks"
            // all pairs of disks closer than 1.0:
            let found = bvh.ClosePairs (1.0, diskSqDist) |> Seq.map (fun p -> p.IdxA, p.IdxB) |> Set.ofSeq
            let expected =
                seq { for i = 0 to disks.Length - 1 do
                        for j = i + 1 to disks.Length - 1 do
                            if sqrt (diskSqDist disks.[i] disks.[j]) <= 1.0 then i, j }
                |> Set.ofSeq
            Expect.equal found expected "all pairs of disks closer than 1.0"
        }

        test "a tree of a single item answers all queries" {
            let rects = [| BRect.createXY (0., 0., 1., 1.) |]
            let bvh = Bvh2d.createFromRects rects
            let struct (idx, d) = bvh.ClosestRect (Pt (3., 1.))
            Expect.equal idx 0 "the only item is the closest"
            Expect.floatClose Accuracy.high d 2.0 "the distance to the only item"
            Expect.equal (bvh.ItemsInRect (BRect.createXY (0.2, 0.2, 0.8, 0.8))).Count 1 "the only item is found"
            Expect.equal (bvh.ClosePairs 100.0).Count 0 "a single item has no pairs"
        }

        test "2D line wrapper uses rectangle bounds" {
            let lines = [| Line2D (0., 0., 1., 0.); Line2D (5., 0., 6., 0.) |]
            let bvh = LineBvh2d.create lines
            let struct (idx, distance) = bvh.ClosestLine (Pt (0.5, 2.))
            Expect.equal idx 0 "first line is closest"
            Expect.floatClose Accuracy.high distance 2.0 "exact planar line distance"
        }

        test "2D line queries match brute force" {
            let lines =
                Array.init 200 (fun _ ->
                    let x = rand.NextDouble() * 100.0
                    let y = rand.NextDouble() * 100.0
                    Line2D (x, y, x + rand.NextDouble() * 10.0 - 5.0, y + rand.NextDouble() * 10.0 - 5.0))
            let bvh = LineBvh2d.create lines
            for _ = 1 to 25 do
                let pt = Pt (rand.NextDouble() * 100.0, rand.NextDouble() * 100.0)
                let struct (idx, d) = bvh.ClosestLine pt
                let mutable bestD = Double.MaxValue
                for i = 0 to lines.Length - 1 do
                    bestD <- min bestD (sqrt (lines.[i].SqDistanceToPt pt))
                Expect.floatClose Accuracy.high d bestD "the closest line distance to a point"
                Expect.floatClose Accuracy.high (sqrt (lines.[idx].SqDistanceToPt pt)) bestD "the reported line is at that distance"
                Expect.floatClose Accuracy.high (bvh.ClosestPoint(pt).DistanceTo pt) bestD "the closest point is at that distance"
            let pair = bvh.ClosestPair ()
            let mutable bestD = Double.MaxValue
            for i = 0 to lines.Length - 1 do
                for j = i + 1 to lines.Length - 1 do
                    bestD <- min bestD (sqrt (XLine2D.getSqDistance (lines.[i], lines.[j])))
            Expect.floatClose Accuracy.high pair.Distance bestD "the closest pair of lines"
        }
    ]
