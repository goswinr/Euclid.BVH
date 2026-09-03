module TestLineBvh

open Euclid
open System
open Expecto

/// A deterministic pseudo random generator so tests are repeatable.
let private rand = Random 42

/// Creates random lines, clustered unevenly in space to mimic real world input.
let private randomLines (count: int) : Line3D[] =
    Array.init count (fun _ ->
        // random cluster center, then a short line near it:
        let cx = rand.NextDouble() * 100.0
        let cy = rand.NextDouble() * 100.0
        let cz = rand.NextDouble() * 20.0
        // cluster by rounding centers to a coarse grid on some lines:
        let cx = if rand.NextDouble() < 0.5 then Math.Round(cx / 25.0) * 25.0 + rand.NextDouble() * 3.0 else cx
        let cy = if rand.NextDouble() < 0.5 then Math.Round(cy / 25.0) * 25.0 + rand.NextDouble() * 3.0 else cy
        let dx = (rand.NextDouble() - 0.5) * 4.0
        let dy = (rand.NextDouble() - 0.5) * 4.0
        let dz = (rand.NextDouble() - 0.5) * 4.0
        Line3D (cx, cy, cz, cx + dx, cy + dy, cz + dz))

/// Brute force distance between two lines.
let private dist (a: Line3D) (b: Line3D) = sqrt (XLine3D.getSqDistance (a, b))

/// The squared distance between two axis aligned bounding boxes, 0.0 if they overlap.
let private sqBoxDist (a: BBox) (b: BBox) : float =
    let inline axis aMin aMax bMin bMax =
        if   bMin > aMax then bMin - aMax
        elif aMin > bMax then aMin - bMax
        else 0.0
    let dx = axis a.MinX a.MaxX b.MinX b.MaxX
    let dy = axis a.MinY a.MaxY b.MinY b.MaxY
    let dz = axis a.MinZ a.MaxZ b.MinZ b.MaxZ
    dx*dx + dy*dy + dz*dz

/// Brute force nearest neighbor of line i.
let private bruteNearest (lines: Line3D[]) (i: int) : int * float =
    let mutable bestJ = -1
    let mutable bestD = Double.MaxValue
    for j = 0 to lines.Length - 1 do
        if j <> i then
            let d = dist lines.[i] lines.[j]
            if d < bestD then
                bestD <- d
                bestJ <- j
    bestJ, bestD

/// Brute force closest pair over all lines.
let private bruteClosestPair (lines: Line3D[]) : int * int * float =
    let mutable best = (-1, -1, Double.MaxValue)
    for i = 0 to lines.Length - 1 do
        for j = i + 1 to lines.Length - 1 do
            let d = dist lines.[i] lines.[j]
            let (_, _, bd) = best
            if d < bd then best <- (i, j, d)
    best

/// Brute force all pairs closer than maxDist.
let private brutePairs (lines: Line3D[]) (maxDist: float) : Set<int * int> =
    let mutable result = Set.empty
    for i = 0 to lines.Length - 1 do
        for j = i + 1 to lines.Length - 1 do
            if dist lines.[i] lines.[j] <= maxDist then
                result <- result.Add (i, j)
    result

let tests =
    testList "LineBvh" [

        test "create fails on empty input" {
            Expect.throws (fun () -> LineBvh.create [||] |> ignore) "empty input should throw"
        }

        test "single line tree" {
            let lines = [| Line3D (0., 0., 0., 1., 0., 0.) |]
            let bvh = LineBvh.create lines
            Expect.equal bvh.Count 1 "count"
            let struct (i, d) = bvh.ClosestLine (Line3D (0., 2., 0., 1., 2., 0.))
            Expect.equal i 0 "closest index"
            Expect.floatClose Accuracy.high d 2.0 "closest distance"
        }

        test "closest line matches brute force" {
            let lines = randomLines 500
            let bvh = LineBvh.create lines
            let query = Line3D (10., 10., 5., 15., 12., 6.)
            let struct (_, d) = bvh.ClosestLine query
            let mutable bestD = Double.MaxValue
            for l in lines do
                bestD <- min bestD (dist query l)
            Expect.floatClose Accuracy.high d bestD "closest distance should match brute force"
        }

        test "nearest neighbor of each line matches brute force" {
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            for i = 0 to lines.Length - 1 do
                let struct (_, d) = bvh.ClosestLine (lines.[i], i)
                let _, bd = bruteNearest lines i
                Expect.floatClose Accuracy.high d bd $"nearest neighbor distance of line {i}"
        }

        test "closest pair matches brute force" {
            let lines = randomLines 400
            let bvh = LineBvh.create lines
            let pair = bvh.ClosestPair ()
            let _, _, bd = bruteClosestPair lines
            Expect.floatClose Accuracy.high pair.Distance bd "closest pair distance should match brute force"
            Expect.isTrue (pair.IdxA < pair.IdxB) "pair indices should be ordered"
        }

        test "nearest neighbors array matches brute force" {
            let lines = randomLines 200
            let bvh = LineBvh.create lines
            let nns = bvh.NearestNeighbors ()
            Expect.equal nns.Length lines.Length "one entry per line"
            for i = 0 to lines.Length - 1 do
                let _, bd = bruteNearest lines i
                Expect.equal nns.[i].IdxA i "IdxA is the line itself"
                Expect.floatClose Accuracy.high nns.[i].Distance bd $"nearest neighbor distance of line {i}"
        }

        test "close pairs match brute force" {
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            let maxDist = 2.5
            let pairs =
                bvh.ClosePairs maxDist
                |> Seq.map (fun p -> p.IdxA, p.IdxB)
                |> Set.ofSeq
            let brute = brutePairs lines maxDist
            Expect.equal pairs brute "pairs within tolerance should match brute force"
        }

        test "close pairs has no duplicates" {
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            let pairs = bvh.ClosePairs 5.0
            let distinct = pairs |> Seq.map (fun p -> p.IdxA, p.IdxB) |> Set.ofSeq
            Expect.equal pairs.Count distinct.Count "no duplicate pairs"
        }

        test "close pairs with negative tolerance fails" {
            let lines = randomLines 10
            let bvh = LineBvh.create lines
            Expect.throws (fun () -> bvh.ClosePairs -1.0 |> ignore) "negative tolerance should throw"
        }

        test "lines in box matches brute force" {
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            let box = BBox.createFromSeq [ Pnt (20., 20., 0.); Pnt (60., 60., 20.) ]
            let found = bvh.LinesInBox box |> Set.ofSeq
            let brute =
                seq { for i = 0 to lines.Length - 1 do
                        if sqBoxDist box (BBox.createFromLine lines.[i]) <= 0.0 then i }
                |> Set.ofSeq
            Expect.equal found brute "lines in box should match brute force"
        }

        test "different leaf sizes give the same result" {
            let lines = randomLines 250
            let query = Line3D (50., 50., 10., 55., 52., 11.)
            let results =
                [ 1; 2; 8; 32 ]
                |> List.map (fun ls ->
                    let bvh = LineBvh.create (lines, ls)
                    let struct (_, d) = bvh.ClosestLine query
                    d)
            for d in results do
                Expect.floatClose Accuracy.high d results.Head "distance should not depend on leaf size"
        }

        test "tree box contains all lines" {
            let lines = randomLines 100
            let bvh = LineBvh.create lines
            for l in lines do
                Expect.isTrue (bvh.Box.Contains (BBox.createFromLine l)) "tree box should contain every line box"
        }

        test "closest line to point matches brute force" {
            let lines = randomLines 500
            let bvh = LineBvh.create lines
            let pt = Pnt (42., 61., 7.)
            let struct (i, d) = bvh.ClosestLine pt
            let mutable bestD = Double.MaxValue
            let mutable bestI = -1
            for j = 0 to lines.Length - 1 do
                let dj = sqrt (lines.[j].SqDistanceToPnt pt)
                if dj < bestD then
                    bestD <- dj
                    bestI <- j
            Expect.floatClose Accuracy.high d bestD "closest line distance to point should match brute force"
            Expect.equal i bestI "closest line index should match brute force"
        }

        test "closest line to point with skip index" {
            let lines = randomLines 200
            let bvh = LineBvh.create lines
            let pt = lines.[7].From // on line 7 itself
            let struct (i0, d0) = bvh.ClosestLine pt
            Expect.equal i0 7 "without skip, line 7 itself is closest"
            Expect.floatClose Accuracy.high d0 0.0 "distance to own start point is zero"
            let struct (i1, _) = bvh.ClosestLine (pt, 7)
            Expect.notEqual i1 7 "with skip, another line is found"
        }

        test "closest point on lines matches brute force" {
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            let pt = Pnt (33., 44., 11.)
            let cp = bvh.ClosestPoint pt
            let mutable bestD = Double.MaxValue
            for l in lines do
                bestD <- min bestD (sqrt (l.SqDistanceToPnt pt))
            Expect.floatClose Accuracy.high (cp.DistanceTo pt) bestD "closest point distance should match brute force"
        }

        test "lines near point match brute force" {
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            let pt = Pnt (50., 50., 10.)
            let tol = 8.0
            let found = bvh.LinesNearPoint (pt, tol) |> Set.ofSeq
            let queryBox = BBox.createFromSeq [ pt ]
            let brute =
                seq { for i = 0 to lines.Length - 1 do
                        if sqrt (sqBoxDist queryBox (BBox.createFromLine lines.[i])) <= tol then i }
                |> Set.ofSeq
            Expect.equal found brute "lines near point should match brute force"
        }
    ]
