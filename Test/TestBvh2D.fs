module TestBvh2D

open Euclid
open System
open Scriptorium.Nib.Assertion
open Asserts
open type Scriptorium.Quill.Test

// Every test below creates its own seeded generator instead of sharing one. That keeps
// each test repeatable on its own, whatever order the tests run in and whichever of them
// run at all, and the seeds differ per test so they do not all see the same input.

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
let private randomRects (rand: Random) (count: int) : BRect[] =
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

let private randomDisks (rand: Random) (count: int) : Disk[] =
    Array.init count (fun _ ->
        { Center = Pt (rand.NextDouble() * 100.0, rand.NextDouble() * 100.0)
          Radius = rand.NextDouble() * 1.5 })

let tests =
    testList ("BVH2D", [
        test ("zero tolerance includes touching and degenerate bounds at every leaf size", fun _ ->
            let rects = [|
                BRect.createXY(0., 0., 0., 0.)
                BRect.createXY(-1., -1., 1., 1.)
                BRect.createXY(2., -1., 2., 1.)
                BRect.createXY(-2., 2., 2., 2.)
                BRect.createXY(5., 5., 6., 6.)
            |]
            let points = [| Pt(0., 0.); Pt(-1., -1.); Pt(1., 1.); Pt(2., 0.); Pt(0., 2.); Pt(3., 3.); Pt(6., 6.) |]
            for leafSize in [| 1; 2; 4; 16 |] do
                let tree = BVH2D.createFromRects(rects, leafSize)
                for pt in points do
                    let expected = rects |> Array.indexed |> Array.choose (fun (i, r) -> if r.ContainsPt pt then Some i else None)
                    for tolerance in [| 0.0; -0.0 |] do
                        assertThat (tree.ItemsNearPoint(pt, tolerance).ToArray() |> Array.sort) (tag "point boundaries" >> isEqualTo expected)
                    let query = BRect.createXY(pt.X, pt.Y, pt.X + 1., pt.Y + 1.)
                    let expected = rects |> Array.indexed |> Array.choose (fun (i, r) -> if r.IsOverlapping query then Some i else None)
                    assertThat (tree.ItemsInRect(query).ToArray() |> Array.sort) (tag "touching rectangles" >> isEqualTo expected)
            let single = BVH2D.createFromRects [| rects.[0] |]
            assertThat (single.ItemsNearPoint(Pt(0., 0.)).Count) (tag "single point hit" >> isEqualTo 1)
            assertThat (single.ItemsNearPoint(Pt(1., 0.)).Count) (tag "single point miss" >> isEqualTo 0)
        )

        test ("positive tolerance uses Euclidean distance at rectangle corners", fun _ ->
            let tree = BVH2D.createFromRects [| BRect.createXY(0., 0., 1., 1.) |]
            assertThat (tree.ItemsNearPoint(Pt(-0.75, -0.75), 1.).Count) (tag "diagonal point outside radius" >> isEqualTo 0)
            assertThat (tree.ItemsNearPoint(Pt(-0.5, -0.5), 1.).Count) (tag "diagonal point inside radius" >> isEqualTo 1)
            assertThat (tree.ItemsInRect(BRect.createXY(-1., -1., -0.75, -0.75), 1.).Count) (tag "diagonal rectangle outside radius" >> isEqualTo 0)
            assertThat (tree.ItemsInRect(BRect.createXY(-1., -1., -0.5, -0.5), 1.).Count) (tag "diagonal rectangle inside radius" >> isEqualTo 1)
        )

        test ("build evaluates every bounding rectangle once", fun _ ->
            let mutable calls = 0
            let rects = [| BRect.createXY (0., 0., 1., 1.); BRect.createXY (2., 0., 3., 1.) |]
            BVH2D.create (rects, fun rect -> calls <- calls + 1; rect) |> ignore
            assertThat calls (tag "bounding rectangle function is called once per item" >> isEqualTo rects.Length)
        )

        test ("closest rectangle matches planar distance", fun _ ->
            let rects =
                [| BRect.createXY (0., 0., 1., 1.)
                   BRect.createXY (10., 0., 12., 1.)
                   BRect.createXY (4., 5., 6., 7.) |]
            let bvh = BVH2D.createFromRects rects
            let query = BRect.createXY (2., 0., 3., 1.)
            let (idx, distance) = bvh.ClosestRect query
            assertThat idx (tag "first rectangle is closest" >> isEqualTo 0)
            assertThat distance (tag "distance is planar" >> isCloseTo (rectDistance query rects.[0]))
            assertThat (bvh.Rectangle.Contains rects.[0]) (tag "tree rectangle contains items" >> isTrue)
        )

        test ("rectangle pairs and point queries are planar", fun _ ->
            let rects =
                [| BRect.createXY (0., 0., 1., 1.)
                   BRect.createXY (1.5, 0., 2.5, 1.)
                   BRect.createXY (10., 0., 11., 1.) |]
            let bvh = BVH2D.createFromRects rects
            let pairs = bvh.ClosePairs 0.5 |> Seq.map (fun p -> p.IdxA, p.IdxB) |> Set.ofSeq
            assertThat pairs (tag "only the nearby pair is returned" >> isEqualTo (Set.singleton (0, 1)))
            let found = bvh.ItemsNearPoint (Pt (2., 0.5), 0.0) |> Set.ofSeq
            assertThat found (tag "point query finds containing rectangle" >> isEqualTo (Set.singleton 1))
        )

        test ("the tree rectangle is the union of all item rectangles", fun _ ->
            let rand = Random 2001
            let rects = randomRects rand 200
            let bvh = BVH2D.createFromRects rects
            let all = rects |> Array.reduce (fun a b -> a.Union b)
            assertThat bvh.Rectangle.MinX (tag "MinX of the tree rectangle" >> isCloseTo all.MinX)
            assertThat bvh.Rectangle.MinY (tag "MinY of the tree rectangle" >> isCloseTo all.MinY)
            assertThat bvh.Rectangle.MaxX (tag "MaxX of the tree rectangle" >> isCloseTo all.MaxX)
            assertThat bvh.Rectangle.MaxY (tag "MaxY of the tree rectangle" >> isCloseTo all.MaxY)
            assertThat bvh.Count (tag "the count of items" >> isEqualTo rects.Length)
            assertThat bvh.Rects.Length (tag "one rectangle per item" >> isEqualTo rects.Length)
        )

        test ("closest rectangle to a query rectangle matches brute force", fun _ ->
            let rand = Random 2002
            let rects = randomRects rand 300
            let bvh = BVH2D.createFromRects rects
            for _ = 1 to 50 do
                let x = rand.NextDouble() * 120.0 - 10.0
                let y = rand.NextDouble() * 120.0 - 10.0
                let query = BRect.createXY (x, y, x + 1.0, y + 1.0)
                let (idx, d) = bvh.ClosestRect query
                let mutable bestD = Double.MaxValue
                for i = 0 to rects.Length - 1 do
                    bestD <- min bestD (rectDistance query rects.[i])
                assertThat d (tag "the closest rectangle distance" >> isCloseTo bestD)
                assertThat (rectDistance query rects.[idx]) (tag "the reported index is at that distance" >> isCloseTo bestD)
        )

        test ("closest rectangle to a query point matches brute force", fun _ ->
            let rand = Random 2003
            let rects = randomRects rand 300
            let bvh = BVH2D.createFromRects rects
            for _ = 1 to 50 do
                let pt = Pt (rand.NextDouble() * 120.0 - 10.0, rand.NextDouble() * 120.0 - 10.0)
                let (idx, d) = bvh.ClosestRect pt
                let mutable bestD = Double.MaxValue
                for i = 0 to rects.Length - 1 do
                    bestD <- min bestD (ptRectDistance pt rects.[i])
                assertThat d (tag "the closest rectangle distance to the point" >> isCloseTo bestD)
                assertThat (ptRectDistance pt rects.[idx]) (tag "the reported index is at that distance" >> isCloseTo bestD)
        )

        test ("leaf size does not change the query results", fun _ ->
            let rand = Random 2004
            let rects = randomRects rand 250
            let small = BVH2D.createFromRects (rects, 1)
            let big = BVH2D.createFromRects (rects, 16)
            for _ = 1 to 30 do
                let pt = Pt (rand.NextDouble() * 120.0 - 10.0, rand.NextDouble() * 120.0 - 10.0)
                let (_, dSmall) = small.ClosestRect pt
                let (_, dBig) = big.ClosestRect pt
                assertThat dSmall (tag "the same distance for any leaf size" >> isCloseTo dBig)
            let inRect = BRect.createXY (10., 10., 40., 40.)
            let a = small.ItemsInRect inRect |> Set.ofSeq
            let b = big.ItemsInRect inRect |> Set.ofSeq
            assertThat a (tag "the same items in a rectangle for any leaf size" >> isEqualTo b)
        )

        test ("nearest neighbors and closest pair match brute force", fun _ ->
            let rand = Random 2005
            let rects = randomRects rand 200
            let bvh = BVH2D.createFromRects rects
            let nn = bvh.NearestNeighbors ()
            assertThat nn.Length (tag "one neighbor per item" >> isEqualTo rects.Length)
            for i = 0 to rects.Length - 1 do
                let _, bestD = bruteNearest rects i
                assertThat nn.[i].IdxA (tag "IdxA is the item itself" >> isEqualTo i)
                assertThat nn.[i].Distance (tag "the nearest neighbor distance" >> isCloseTo bestD)
                assertThat (rectDistance rects.[i] rects.[nn.[i].IdxB]) (tag "the reported neighbor is at that distance" >> isCloseTo bestD)
            let pair = bvh.ClosestPair ()
            let mutable bestD = Double.MaxValue
            for i = 0 to rects.Length - 1 do
                for j = i + 1 to rects.Length - 1 do
                    bestD <- min bestD (rectDistance rects.[i] rects.[j])
            assertThat pair.Distance (tag "the closest pair distance" >> isCloseTo bestD)
            assertThat (pair.IdxA < pair.IdxB) (tag "the pair indices are ordered" >> isTrue)
        )

        test ("close pairs match brute force", fun _ ->
            let rand = Random 2006
            let rects = randomRects rand 200
            let bvh = BVH2D.createFromRects rects
            for maxDist in [ 0.0; 1.0; 5.0 ] do
                let found = bvh.ClosePairs maxDist |> Seq.map (fun p -> p.IdxA, p.IdxB) |> Set.ofSeq
                assertThat found (tag $"all pairs closer than {maxDist}" >> isEqualTo (brutePairs rects maxDist))
        )

        test ("rectangle and point queries reject negative tolerances", fun _ ->
            let rect = BRect.createXY (-1., -1., 1., 1.)
            let bvh = BVH2D.createFromRects [| rect |]
            for tolerance in [ -1.0; -1e-200; nan ] do
                assertThat (fun () -> bvh.ItemsInRect (rect, tolerance) |> ignore) (tag $"negative rectangle tolerance {tolerance}" >> throws)
                assertThat (fun () -> bvh.ItemsNearPoint (Pt (0., 0.), tolerance) |> ignore) (tag $"negative point tolerance {tolerance}" >> throws)
        )

        test ("items in a rectangle and near a point match brute force", fun _ ->
            let rand = Random 2007
            let rects = randomRects rand 250
            let bvh = BVH2D.createFromRects rects
            let query = BRect.createXY (20., 20., 45., 60.)
            for tol in [ 0.0; 2.5 ] do
                let found = bvh.ItemsInRect (query, tol) |> Set.ofSeq
                let expected =
                    seq { for i = 0 to rects.Length - 1 do
                            if rectDistance query rects.[i] <= tol then i }
                    |> Set.ofSeq
                assertThat found (tag $"items within {tol} of the query rectangle" >> isEqualTo expected)
            let pt = Pt (33., 47.)
            for tol in [ 0.0; 4.0 ] do
                let found = bvh.ItemsNearPoint (pt, tol) |> Set.ofSeq
                let expected =
                    seq { for i = 0 to rects.Length - 1 do
                            if ptRectDistance pt rects.[i] <= tol then i }
                    |> Set.ofSeq
                assertThat found (tag $"items within {tol} of the query point" >> isEqualTo expected)
        )

        test ("exact distance queries on a custom item type", fun _ ->
            let rand = Random 2008
            let disks = randomDisks rand 150
            let bvh = BVH2D.create (disks, diskRect)
            // the closest disk to a point, measured to the disk outline:
            for _ = 1 to 20 do
                let pt = Pt (rand.NextDouble() * 100.0, rand.NextDouble() * 100.0)
                let sqDistTo (d: Disk) = let v = max 0.0 (d.Center.DistanceTo pt - d.Radius) in v * v
                let (idx, d) = bvh.ClosestItem (pt, sqDistTo)
                let mutable bestD = Double.MaxValue
                for i = 0 to disks.Length - 1 do
                    bestD <- min bestD (sqrt (sqDistTo disks.[i]))
                assertThat d (tag "the closest disk outline distance" >> isCloseTo bestD)
                assertThat (sqrt (sqDistTo disks.[idx])) (tag "the reported index is at that distance" >> isCloseTo bestD)
            // the closest pair of disks, measured between the outlines:
            let pair = bvh.ClosestPair diskSqDist
            let mutable bestD = Double.MaxValue
            for i = 0 to disks.Length - 1 do
                for j = i + 1 to disks.Length - 1 do
                    bestD <- min bestD (sqrt (diskSqDist disks.[i] disks.[j]))
            assertThat pair.Distance (tag "the closest pair of disks" >> isCloseTo bestD)
            // all pairs of disks closer than 1.0:
            let found = bvh.ClosePairs (1.0, diskSqDist) |> Seq.map (fun p -> p.IdxA, p.IdxB) |> Set.ofSeq
            let expected =
                seq { for i = 0 to disks.Length - 1 do
                        for j = i + 1 to disks.Length - 1 do
                            if sqrt (diskSqDist disks.[i] disks.[j]) <= 1.0 then i, j }
                |> Set.ofSeq
            assertThat found (tag "all pairs of disks closer than 1.0" >> isEqualTo expected)
        )

        test ("a tree of a single item answers all queries", fun _ ->
            let rects = [| BRect.createXY (0., 0., 1., 1.) |]
            let bvh = BVH2D.createFromRects rects
            let (idx, d) = bvh.ClosestRect (Pt (3., 1.))
            assertThat idx (tag "the only item is the closest" >> isEqualTo 0)
            assertThat d (tag "the distance to the only item" >> isCloseTo 2.0)
            assertThat (bvh.ItemsInRect (BRect.createXY (0.2, 0.2, 0.8, 0.8))).Count (tag "the only item is found" >> isEqualTo 1)
            assertThat (bvh.ClosePairs 100.0).Count (tag "a single item has no pairs" >> isEqualTo 0)
        )

        test ("2D line wrapper uses rectangle bounds", fun _ ->
            let lines = [| Line2D (0., 0., 1., 0.); Line2D (5., 0., 6., 0.) |]
            let bvh = BVHLine2D.create lines
            let (idx, distance) = bvh.ClosestLine (Pt (0.5, 2.))
            assertThat idx (tag "first line is closest" >> isEqualTo 0)
            assertThat distance (tag "exact planar line distance" >> isCloseTo 2.0)
        )

        test ("2D line range queries reject negative tolerances", fun _ ->
            let line = Line2D (0., 0., 1., 0.)
            let bvh = BVHLine2D.create [| line |]
            for tolerance in [ -1.0; -1e-200; nan ] do
                assertThat (fun () -> bvh.LinesInRect (BRect.createFromLine line, tolerance) |> ignore) (tag $"negative line rectangle tolerance {tolerance}" >> throws)
                assertThat (fun () -> bvh.LinesNearPoint (Pt (0.5, 0.), tolerance) |> ignore) (tag $"negative line point tolerance {tolerance}" >> throws)
        )

        test ("2D line queries match brute force", fun _ ->
            let rand = Random 2009
            let lines =
                Array.init 200 (fun _ ->
                    let x = rand.NextDouble() * 100.0
                    let y = rand.NextDouble() * 100.0
                    Line2D (x, y, x + rand.NextDouble() * 10.0 - 5.0, y + rand.NextDouble() * 10.0 - 5.0))
            let bvh = BVHLine2D.create lines
            for _ = 1 to 25 do
                let pt = Pt (rand.NextDouble() * 100.0, rand.NextDouble() * 100.0)
                let (idx, d) = bvh.ClosestLine pt
                let mutable bestD = Double.MaxValue
                for i = 0 to lines.Length - 1 do
                    bestD <- min bestD (sqrt (lines.[i].SqDistanceToPt pt))
                assertThat d (tag "the closest line distance to a point" >> isCloseTo bestD)
                assertThat (sqrt (lines.[idx].SqDistanceToPt pt)) (tag "the reported line is at that distance" >> isCloseTo bestD)
                assertThat (bvh.ClosestPoint(pt).DistanceTo pt) (tag "the closest point is at that distance" >> isCloseTo bestD)
            let pair = bvh.ClosestPair ()
            let mutable bestD = Double.MaxValue
            for i = 0 to lines.Length - 1 do
                for j = i + 1 to lines.Length - 1 do
                    bestD <- min bestD (sqrt (XLine2D.getSqDistance (lines.[i], lines.[j])))
            assertThat pair.Distance (tag "the closest pair of lines" >> isCloseTo bestD)
        )

        test ("rectangles by distance enumerate every rectangle in increasing distance order", fun _ ->
            let rand = Random 2010
            let rects = randomRects rand 300
            let bvh = BVH2D.createFromRects rects
            let queryRect = BRect.createXY (30., 70., 33., 72.)
            let found = bvh.RectsByDistance queryRect |> Seq.toArray
            assertThat found.Length (tag "every rectangle should be enumerated" >> isEqualTo rects.Length)
            assertThat (found |> Array.map fst |> Set.ofArray |> Set.count) (tag "every index only once" >> isEqualTo rects.Length)
            let brute = rects |> Array.map (rectDistance queryRect) |> Array.sort
            for i = 0 to found.Length - 1 do
                assertThat (snd found.[i]) (tag $"distance at position {i}" >> isCloseTo brute.[i])
                assertThat (snd found.[i]) (tag $"reported distance of rectangle {fst found.[i]}" >> isCloseTo (rectDistance queryRect rects.[fst found.[i]]))
        )

        test ("rectangles by distance start at the closest rectangle and skip the given index", fun _ ->
            let rand = Random 2011
            let rects = randomRects rand 200
            let bvh = BVH2D.createFromRects rects
            let queryRect = BRect.createXY (10., 10., 15., 12.)
            let (_, closestD) = bvh.ClosestRect queryRect
            let firstFive = bvh.RectsByDistance queryRect |> Seq.truncate 5 |> Seq.toArray
            assertThat firstFive.Length (tag "five entries taken" >> isEqualTo 5)
            assertThat (snd firstFive.[0]) (tag "first entry should be the closest rectangle" >> isCloseTo closestD)
            let skip = 17
            let skipped = bvh.RectsByDistance (rects.[skip], skip) |> Seq.toArray
            assertThat skipped.Length (tag "all but the skipped rectangle" >> isEqualTo (rects.Length - 1))
            assertThat (skipped |> Array.exists (fun (i, _) -> i = skip)) (tag "the skipped rectangle should not appear" >> isFalse)
            let _, nearestD = bruteNearest rects skip
            assertThat (snd skipped.[0]) (tag "first entry should be the nearest neighbor" >> isCloseTo nearestD)
        )

        test ("rectangles by distance to a point match brute force order", fun _ ->
            let rand = Random 2012
            let rects = randomRects rand 300
            let bvh = BVH2D.createFromRects rects
            let pt = Pt (42., 61.)
            let found = bvh.RectsByDistance pt |> Seq.toArray
            assertThat found.Length (tag "every rectangle should be enumerated" >> isEqualTo rects.Length)
            let brute = rects |> Array.map (ptRectDistance pt) |> Array.sort
            for i = 0 to found.Length - 1 do
                assertThat (snd found.[i]) (tag $"distance to point at position {i}" >> isCloseTo brute.[i])
        )

        test ("items by distance with exact distance match brute force order", fun _ ->
            let rand = Random 2013
            let disks = randomDisks rand 300
            let bvh = BVH2D.create (disks, diskRect)
            let query = { Center = Pt (50., 50.); Radius = 1.0 }
            let found = bvh.ItemsByDistance (diskRect query, diskSqDist query) |> Seq.toArray
            assertThat found.Length (tag "every disk should be enumerated" >> isEqualTo disks.Length)
            let brute = disks |> Array.map (fun d -> sqrt (diskSqDist query d)) |> Array.sort
            for i = 0 to found.Length - 1 do
                assertThat (snd found.[i]) (tag $"exact distance at position {i}" >> isCloseTo brute.[i])
                assertThat (snd found.[i]) (tag $"reported distance of disk {fst found.[i]}" >> isCloseTo (sqrt (diskSqDist query disks.[fst found.[i]])))
        )

        test ("items by distance to a point with exact distance match brute force order", fun _ ->
            let rand = Random 2014
            let disks = randomDisks rand 300
            let bvh = BVH2D.create (disks, diskRect)
            let pt = Pt (50., 50.)
            let sqDistTo (d: Disk) =
                let g = max 0.0 (d.Center.DistanceTo pt - d.Radius)
                g * g
            let found = bvh.ItemsByDistance (pt, sqDistTo) |> Seq.toArray
            assertThat found.Length (tag "every disk should be enumerated" >> isEqualTo disks.Length)
            let brute = disks |> Array.map (fun d -> sqrt (sqDistTo d)) |> Array.sort
            for i = 0 to found.Length - 1 do
                assertThat (snd found.[i]) (tag $"exact distance to point at position {i}" >> isCloseTo brute.[i])
        )

        test ("close pairs reject a NaN maximum distance", fun _ ->
            let bvh = BVH2D.createFromRects (randomRects (Random 2015) 20)
            assertThat (fun () -> bvh.ClosePairs nan |> ignore) (tag "rectangle pairs" >> throws)
            assertThat (fun () -> bvh.ClosePairs (nan, sqRectDist) |> ignore) (tag "exact pairs" >> throws)
        )

        test ("closest pair matches brute force at every leaf size", fun _ ->
            let rand = Random 2016
            let disks = randomDisks rand 250
            let mutable bestExact = Double.MaxValue
            let mutable bestRect = Double.MaxValue
            for i = 0 to disks.Length - 1 do
                for j = i + 1 to disks.Length - 1 do
                    bestExact <- min bestExact (sqrt (diskSqDist disks.[i] disks.[j]))
                    bestRect <- min bestRect (rectDistance (diskRect disks.[i]) (diskRect disks.[j]))
            for leafSize in [ 1; 2; 4; 16 ] do
                let bvh = BVH2D.create (disks, diskRect, leafSize)
                let exact = bvh.ClosestPair diskSqDist
                assertThat exact.Distance (tag $"exact distance with leaf size {leafSize}" >> isCloseTo bestExact)
                assertThat (sqrt (diskSqDist disks.[exact.IdxA] disks.[exact.IdxB])) (tag $"the exact pair is at that distance with leaf size {leafSize}" >> isCloseTo bestExact)
                assertThat (exact.IdxA < exact.IdxB) (tag "the exact pair is ordered" >> isTrue)
                let byRect = bvh.ClosestPair ()
                assertThat byRect.Distance (tag $"rectangle distance with leaf size {leafSize}" >> isCloseTo bestRect)
                assertThat (rectDistance (diskRect disks.[byRect.IdxA]) (diskRect disks.[byRect.IdxB])) (tag $"the rectangle pair is at that distance with leaf size {leafSize}" >> isCloseTo bestRect)
                assertThat (byRect.IdxA < byRect.IdxB) (tag "the rectangle pair is ordered" >> isTrue)
        )

        test ("closest pair finds a pair of the first and the last item", fun _ ->
            let rand = Random 2017
            let pts = Array.init 200 (fun _ -> Pt (rand.NextDouble() * 100.0, rand.NextDouble() * 100.0))
            // a near copy of the first point at the end, each item only searches the items after it:
            pts.[pts.Length - 1] <- Pt (pts.[0].X + 0.001, pts.[0].Y + 0.002)
            let sqDist (a: Pt) (b: Pt) = (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)
            let ptRect (p: Pt) = BRect.createXY (p.X, p.Y, p.X + 0.0001, p.Y + 0.0001)
            for leafSize in [ 1; 2; 4; 16 ] do
                let bvh = BVH2D.create (pts, ptRect, leafSize)
                let exact = bvh.ClosestPair sqDist
                assertThat (exact.IdxA, exact.IdxB) (tag $"exact pair with leaf size {leafSize}" >> isEqualTo (0, pts.Length - 1))
                assertThat exact.Distance (tag "exact pair distance" >> isCloseTo (sqrt (0.001 * 0.001 + 0.002 * 0.002)))
                let byRect = bvh.ClosestPair ()
                assertThat (byRect.IdxA, byRect.IdxB) (tag $"rectangle pair with leaf size {leafSize}" >> isEqualTo (0, pts.Length - 1))
        )

        test ("exact queries ignore NaN distances and infinite ones come last", fun _ ->
            let rand = Random 2018
            let disks = randomDisks rand 150
            let bvh = BVH2D.create (Array.init disks.Length id, (fun i -> diskRect disks.[i]))
            // item 0 has no distance to anything, item 1 is infinitely far from everything:
            let sqDist (a: int) (b: int) =
                if a = 0 || b = 0 then nan
                elif a = 1 || b = 1 then infinity
                else diskSqDist disks.[a] disks.[b]
            let pair = bvh.ClosestPair sqDist
            let mutable best = Double.MaxValue
            for i = 2 to disks.Length - 1 do
                for j = i + 1 to disks.Length - 1 do
                    best <- min best (sqrt (diskSqDist disks.[i] disks.[j]))
            assertThat pair.Distance (tag "the closest pair without items 0 and 1" >> isCloseTo best)
            let q = 5
            let found = bvh.ItemsByDistance (diskRect disks.[q], sqDist q, q) |> Seq.toArray
            assertThat found.Length (tag "all items except the query and item 0" >> isEqualTo (disks.Length - 2))
            assertThat (found |> Array.exists (fun (i, _) -> i = 0)) (tag "item 0 is left out" >> isFalse)
            assertThat (fst found.[found.Length - 1]) (tag "item 1 comes last" >> isEqualTo 1)
            for k = 1 to found.Length - 1 do
                assertThat (snd found.[k] >= snd found.[k - 1]) (tag $"distances increase at position {k}" >> isTrue)
            let (nearest, _) = bvh.ClosestItem (diskRect disks.[q], sqDist q, q)
            assertThat nearest (tag "the closest item is the first one enumerated" >> isEqualTo (fst found.[0]))
            assertThat (fun () -> bvh.ClosestItem (diskRect disks.[q], (fun _ -> nan)) |> ignore) (tag "only NaN distances" >> throws)
            assertThat (fun () -> bvh.ClosestItem (disks.[q].Center, (fun _ -> infinity)) |> ignore) (tag "only infinite distances" >> throws)
        )

        test ("close pairs only measure pairs of items whose rectangles are within the maximum distance", fun _ ->
            let rand = Random 2020
            let disks = randomDisks rand 300
            let bvh = BVH2D.create (disks, diskRect)
            let maxDist = 1.0
            let mutable calls = 0
            let found = bvh.ClosePairs (maxDist, fun a b -> calls <- calls + 1; diskSqDist a b) |> Seq.map (fun p -> p.IdxA, p.IdxB) |> Set.ofSeq
            let mutable nearRects = 0
            let mutable expected = Set.empty
            for i = 0 to disks.Length - 1 do
                for j = i + 1 to disks.Length - 1 do
                    if rectDistance (diskRect disks.[i]) (diskRect disks.[j]) <= maxDist then nearRects <- nearRects + 1
                    if sqrt (diskSqDist disks.[i] disks.[j]) <= maxDist then expected <- expected.Add (i, j)
            assertThat calls (tag "one exact distance per pair of near rectangles" >> isEqualTo nearRects)
            assertThat found (tag "the pairs match brute force" >> isEqualTo expected)
        )
    ])
