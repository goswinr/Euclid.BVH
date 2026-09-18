module TestLineBvh

open Euclid
open System
open Scriptorium.Nib.Assertion
open Asserts
open type Scriptorium.Quill.Test

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

// testSequenced, not testList: the tests below share the module level 'rand', so they have
// to run one after the other in declaration order to stay reproducible.
let tests =
    testSequenced ("LineBvh", [

        test ("create fails on empty input", fun _ ->
            assertThat (fun () -> LineBvh.create [||] |> ignore) (tag "empty input should throw" >> throws)
        )

        test ("single line tree", fun _ ->
            let lines = [| Line3D (0., 0., 0., 1., 0., 0.) |]
            let bvh = LineBvh.create lines
            assertThat bvh.Count (tag "count" >> isEqualTo 1)
            let struct (i, d) = bvh.ClosestLine (Line3D (0., 2., 0., 1., 2., 0.))
            assertThat i (tag "closest index" >> isEqualTo 0)
            assertThat d (tag "closest distance" >> isCloseTo 2.0)
        )

        test ("closest line matches brute force", fun _ ->
            let lines = randomLines 500
            let bvh = LineBvh.create lines
            let query = Line3D (10., 10., 5., 15., 12., 6.)
            let struct (_, d) = bvh.ClosestLine query
            let mutable bestD = Double.MaxValue
            for l in lines do
                bestD <- min bestD (dist query l)
            assertThat d (tag "closest distance should match brute force" >> isCloseTo bestD)
        )

        test ("nearest neighbor of each line matches brute force", fun _ ->
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            for i = 0 to lines.Length - 1 do
                let struct (_, d) = bvh.ClosestLine (lines.[i], i)
                let _, bd = bruteNearest lines i
                assertThat d (tag $"nearest neighbor distance of line {i}" >> isCloseTo bd)
        )

        test ("closest pair matches brute force", fun _ ->
            let lines = randomLines 400
            let bvh = LineBvh.create lines
            let pair = bvh.ClosestPair ()
            let _, _, bd = bruteClosestPair lines
            assertThat pair.Distance (tag "closest pair distance should match brute force" >> isCloseTo bd)
            assertThat (pair.IdxA < pair.IdxB) (tag "pair indices should be ordered" >> isTrue)
        )

        test ("nearest neighbors array matches brute force", fun _ ->
            let lines = randomLines 200
            let bvh = LineBvh.create lines
            let nns = bvh.NearestNeighbors ()
            assertThat nns.Length (tag "one entry per line" >> isEqualTo lines.Length)
            for i = 0 to lines.Length - 1 do
                let _, bd = bruteNearest lines i
                assertThat nns.[i].IdxA (tag "IdxA is the line itself" >> isEqualTo i)
                assertThat nns.[i].Distance (tag $"nearest neighbor distance of line {i}" >> isCloseTo bd)
        )

        test ("close pairs match brute force", fun _ ->
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            let maxDist = 2.5
            let pairs =
                bvh.ClosePairs maxDist
                |> Seq.map (fun p -> p.IdxA, p.IdxB)
                |> Set.ofSeq
            let brute = brutePairs lines maxDist
            assertThat pairs (tag "pairs within tolerance should match brute force" >> isEqualTo brute)
        )

        test ("close pairs has no duplicates", fun _ ->
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            let pairs = bvh.ClosePairs 5.0
            let distinct = pairs |> Seq.map (fun p -> p.IdxA, p.IdxB) |> Set.ofSeq
            assertThat pairs.Count (tag "no duplicate pairs" >> isEqualTo distinct.Count)
        )

        test ("close pairs with negative tolerance fails", fun _ ->
            let lines = randomLines 10
            let bvh = LineBvh.create lines
            assertThat (fun () -> bvh.ClosePairs -1.0 |> ignore) (tag "negative tolerance should throw" >> throws)
        )

        test ("lines in box matches brute force", fun _ ->
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            let box = BBox.createFromSeq [ Pnt (20., 20., 0.); Pnt (60., 60., 20.) ]
            let found = bvh.LinesInBox box |> Set.ofSeq
            let brute =
                seq { for i = 0 to lines.Length - 1 do
                        if sqBoxDist box (BBox.createFromLine lines.[i]) <= 0.0 then i }
                |> Set.ofSeq
            assertThat found (tag "lines in box should match brute force" >> isEqualTo brute)
        )

        test ("different leaf sizes give the same result", fun _ ->
            let lines = randomLines 250
            let query = Line3D (50., 50., 10., 55., 52., 11.)
            let results =
                [ 1; 2; 8; 32 ]
                |> List.map (fun ls ->
                    let bvh = LineBvh.create (lines, ls)
                    let struct (_, d) = bvh.ClosestLine query
                    d)
            for d in results do
                assertThat d (tag "distance should not depend on leaf size" >> isCloseTo results.Head)
        )

        test ("tree box contains all lines", fun _ ->
            let lines = randomLines 100
            let bvh = LineBvh.create lines
            for l in lines do
                assertThat (bvh.Box.Contains (BBox.createFromLine l)) (tag "tree box should contain every line box" >> isTrue)
        )

        test ("closest line to point matches brute force", fun _ ->
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
            assertThat d (tag "closest line distance to point should match brute force" >> isCloseTo bestD)
            assertThat i (tag "closest line index should match brute force" >> isEqualTo bestI)
        )

        test ("closest line to point with skip index", fun _ ->
            let lines = randomLines 200
            let bvh = LineBvh.create lines
            let pt = lines.[7].From // on line 7 itself
            let struct (i0, d0) = bvh.ClosestLine pt
            assertThat i0 (tag "without skip, line 7 itself is closest" >> isEqualTo 7)
            assertThat d0 (tag "distance to own start point is zero" >> isCloseTo 0.0)
            let struct (i1, _) = bvh.ClosestLine (pt, 7)
            assertThat i1 (tag "with skip, another line is found" >> isNotEqualTo 7)
        )

        test ("closest point on lines matches brute force", fun _ ->
            let lines = randomLines 300
            let bvh = LineBvh.create lines
            let pt = Pnt (33., 44., 11.)
            let cp = bvh.ClosestPoint pt
            let mutable bestD = Double.MaxValue
            for l in lines do
                bestD <- min bestD (sqrt (l.SqDistanceToPnt pt))
            assertThat (cp.DistanceTo pt) (tag "closest point distance should match brute force" >> isCloseTo bestD)
        )

        test ("lines near point match brute force", fun _ ->
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
            assertThat found (tag "lines near point should match brute force" >> isEqualTo brute)
        )
    ])
