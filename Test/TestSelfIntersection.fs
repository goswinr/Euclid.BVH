module TestSelfIntersection

open Euclid
open System
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

/// A circle with the given count of points, closed if the last point repeats the first.
let private ring2D (pointCount: int) (closed: bool) : Polyline2D =
    let segmentCount = pointCount - 1
    let span = if closed then 2.0 * Math.PI else 1.5 * Math.PI
    Polyline2D.createFromPts [|
        for i in 0 .. segmentCount ->
            let angle = span * float (if closed then i % segmentCount else i) / float segmentCount
            Pt(100.0 * cos angle, 100.0 * sin angle) |]

/// A closed figure eight whose lobes cross once at the origin.
/// lift moves the two strands apart in Z where they cross, by twice its value.
let private figureEight3D (pointCount: int) (lift: float) : Polyline3D =
    let segmentCount = pointCount - 1
    Polyline3D.createFromPts [|
        for i in 0 .. segmentCount ->
            let t = 0.1 + 2.0 * Math.PI * float (i % segmentCount) / float segmentCount
            Pnt(100.0 * cos t, 50.0 * sin (2.0 * t), lift * sin t) |]

/// A random walk, so that it crosses itself many times. If closed, the first point is repeated at the end.
let private randomPolyline2D (rand: Random) (pointCount: int) (closed: bool) : Polyline2D =
    let pts = [| for _ in 1 .. pointCount -> Pt(rand.NextDouble() * 100.0, rand.NextDouble() * 100.0) |]
    Polyline2D.createFromPts (if closed then Array.append pts [| pts.[0] |] else pts)

/// A random walk in 3D. If closed, the first point is repeated at the end.
/// Exact crossings are rare in 3D, so the tests use larger skew distances on it.
let private randomPolyline3D (rand: Random) (pointCount: int) (closed: bool) : Polyline3D =
    let pts = [| for _ in 1 .. pointCount -> Pnt(rand.NextDouble() * 100.0, rand.NextDouble() * 100.0, rand.NextDouble() * 20.0) |]
    Polyline3D.createFromPts (if closed then Array.append pts [| pts.[0] |] else pts)

/// Brute force reference in 3D: tests every pair of non adjacent segments with XLine3D.tryIntersect,
/// in the order that gives the lowest first index, then the lowest second index.
let private bruteForce3D (pl: Polyline3D) (maxSkewDistance: float) : Option<Pnt * int * int> =
    let xyzs = pl.XYZs
    let segmentCount = xyzs.Count / 3 - 1
    let segment s = Line3D(xyzs.[3*s], xyzs.[3*s + 1], xyzs.[3*s + 2], xyzs.[3*s + 3], xyzs.[3*s + 4], xyzs.[3*s + 5])
    let isClosed = pl.IsAlmostClosed 1e-7
    let mutable result = None
    let mutable i = 0
    while result.IsNone && i < segmentCount do
        let mutable j = i + 2
        while result.IsNone && j < segmentCount do
            if not (isClosed && i = 0 && j = segmentCount - 1) then
                match XLine3D.tryIntersect(segment i, segment j, maxSkewDistance) with
                | ValueSome pt -> result <- Some (pt, i, j)
                | ValueNone -> ()
            j <- j + 1
        i <- i + 1
    result

let private show2D (r: Option<Pt * int * int>) =
    match r with
    | Some (pt, i, j) -> sprintf "segments %d and %d at %.6f, %.6f" i j pt.X pt.Y
    | None -> "none"

let private show3D (r: Option<Pnt * int * int>) =
    match r with
    | Some (pt, i, j) -> sprintf "segments %d and %d at %.6f, %.6f, %.6f" i j pt.X pt.Y pt.Z
    | None -> "none"

/// All pairs in segment-index order, without BVH pruning.
let private bruteForceAll2D (pl: Polyline2D) =
    let xys = pl.XYs
    let segmentCount = xys.Count / 2 - 1
    let segment s = Line2D(xys.[2*s], xys.[2*s + 1], xys.[2*s + 2], xys.[2*s + 3])
    let isClosed = pl.IsAlmostClosed 1e-7
    [ for i in 0 .. segmentCount - 1 do
        for j in i + 2 .. segmentCount - 1 do
            if not (isClosed && i = 0 && j = segmentCount - 1) then
                match Line2D.tryIntersect (segment i) (segment j) with
                | ValueSome pt -> yield show2D (Some (pt, i, j))
                | ValueNone -> () ]

let private bruteForceAll3D (pl: Polyline3D) maxSkewDistance =
    let xyzs = pl.XYZs
    let segmentCount = xyzs.Count / 3 - 1
    let segment s = Line3D(xyzs.[3*s], xyzs.[3*s + 1], xyzs.[3*s + 2], xyzs.[3*s + 3], xyzs.[3*s + 4], xyzs.[3*s + 5])
    let isClosed = pl.IsAlmostClosed 1e-7
    [ for i in 0 .. segmentCount - 1 do
        for j in i + 2 .. segmentCount - 1 do
            if not (isClosed && i = 0 && j = segmentCount - 1) then
                match XLine3D.tryIntersect(segment i, segment j, maxSkewDistance) with
                | ValueSome pt -> yield show3D (Some (pt, i, j))
                | ValueNone -> () ]

let tests =
    testList ("Polyline self intersection with a BVH", [

        test ("all: short polylines and rings return empty ResizeArrays", fun _ ->
            for pts in [ [||]; [| Pt(0., 0.) |]; [| Pt(0., 0.); Pt(1., 0.) |] ] do
                let pl = Polyline2D.createFromPts pts
                assertThat (Polyline2D.findAllSelfIntersectionsWithBvh pl).Count (tag "short 2D" >> isEqualTo 0)
                let pl3 = Polyline3D.createFromPts (pts |> Array.map (fun p -> Pnt(p.X, p.Y, 0.)))
                assertThat (Polyline3D.findAllSelfIntersectionsWithBvh pl3).Count (tag "short 3D" >> isEqualTo 0)
            for closed in [ true; false ] do
                let pl = ring2D 50 closed
                assertThat (Polyline2D.findAllSelfIntersectionsWithBvh pl).Count (tag $"2D ring, closed {closed}" >> isEqualTo 0)
                let xy = pl.XYs
                let pl3 = Polyline3D.createFromPts [| for i in 0 .. xy.Count / 2 - 1 -> Pnt(xy.[2*i], xy.[2*i + 1], 0.) |]
                assertThat (Polyline3D.findAllSelfIntersectionsWithBvh pl3).Count (tag $"3D ring, closed {closed}" >> isEqualTo 0)
        )

        test ("all: touching non adjacent segments are returned in 2D and 3D", fun _ ->
            let pts = [| Pt(0., 0.); Pt(10., 0.); Pt(10., 5.); Pt(5., 5.); Pt(5., 0.) |]
            let hits = Polyline2D.findAllSelfIntersectionsWithBvh (Polyline2D.createFromPts pts)
            assertThat (hits |> Seq.map (Some >> show2D) |> Seq.toList) (isEqualTo [ "segments 0 and 3 at 5.000000, 0.000000" ])
            let pl3 = Polyline3D.createFromPts (pts |> Array.map (fun p -> Pnt(p.X, p.Y, 0.)))
            let hits3 = Polyline3D.findAllSelfIntersectionsWithBvh pl3
            assertThat (hits3 |> Seq.map (Some >> show3D) |> Seq.toList) (isEqualTo [ "segments 0 and 3 at 5.000000, 0.000000, 0.000000" ])
        )

        test ("all: distinct segment pairs meeting at one point are retained", fun _ ->
            let pts = [| Pt(-2., 0.); Pt(2., 0.); Pt(-2., -2.); Pt(2., 2.); Pt(0., -2.); Pt(0., 2.) |]
            let pl = Polyline2D.createFromPts pts
            let hits = Polyline2D.findAllSelfIntersectionsWithBvh pl
            let atOrigin = hits |> Seq.choose (fun (p, i, j) -> if p.X = 0. && p.Y = 0. then Some (i, j) else None) |> Seq.toList
            assertThat atOrigin (isEqualTo [ (0, 2); (0, 4); (2, 4) ])
            let pl3 = Polyline3D.createFromPts (pts |> Array.map (fun p -> Pnt(p.X, p.Y, 0.)))
            let hits3 = Polyline3D.findAllSelfIntersectionsWithBvh pl3
            let atOrigin3 = hits3 |> Seq.choose (fun (p, i, j) -> if p.X = 0. && p.Y = 0. then Some (i, j) else None) |> Seq.toList
            assertThat atOrigin3 (isEqualTo atOrigin)
        )

        test ("all 2D: every result matches brute force in segment-index order", fun _ ->
            let rand = Random 4011
            for n in [ 3; 4; 8; 20; 60 ] do
                for closed in [ true; false ] do
                    let pl = randomPolyline2D rand n closed
                    let actual = Polyline2D.findAllSelfIntersectionsWithBvh pl |> Seq.map (Some >> show2D) |> Seq.toList
                    assertThat actual (tag $"{n} points, closed {closed}" >> isEqualTo (bruteForceAll2D pl))
                    assertThat (List.tryHead actual) (isEqualTo (Polyline2D.tryFindSelfIntersectionWithBvh pl |> Option.map (Some >> show2D)))
        )

        test ("all 3D: every result matches brute force in segment-index order", fun _ ->
            let rand = Random 4012
            for n in [ 3; 4; 8; 20; 60 ] do
                for closed in [ true; false ] do
                    let pl = randomPolyline3D rand n closed
                    for skew in [ 1e-6; 0.5; 3.0 ] do
                        let actual = Polyline3D.findAllSelfIntersectionsWithBvh (pl, skew) |> Seq.map (Some >> show3D) |> Seq.toList
                        assertThat actual (tag $"{n} points, closed {closed}, skew {skew}" >> isEqualTo (bruteForceAll3D pl skew))
                        assertThat (List.tryHead actual) (isEqualTo (Polyline3D.tryFindSelfIntersectionWithBvh (pl, skew) |> Option.map (Some >> show3D)))
        )

        test ("all 3D: skew distance controls separated strands and rejects negative values", fun _ ->
            let pl = figureEight3D 50 0.25
            assertThat (Polyline3D.findAllSelfIntersectionsWithBvh pl).Count (tag "default skew" >> isEqualTo 0)
            assertThat (Polyline3D.findAllSelfIntersectionsWithBvh (pl, 0.4)).Count (tag "skew 0.4" >> isEqualTo 0)
            assertThat (Polyline3D.findAllSelfIntersectionsWithBvh (pl, 0.6)).Count (tag "skew 0.6" >> isEqualTo 1)
            assertThat (fun () -> Polyline3D.findAllSelfIntersectionsWithBvh (pl, -1.) |> ignore) (tag "negative skew" >> throws)
        )

        test ("2D: fewer than three points have no self intersection", fun _ ->
            let line = Polyline2D.createFromPts [| Pt(0., 0.); Pt(1., 0.) |]
            let single = Polyline2D.createFromPts [| Pt(0., 0.) |]
            assertThat (Polyline2D.tryFindSelfIntersectionWithBvh line).IsNone (tag "two points" >> isTrue)
            assertThat (Polyline2D.tryFindSelfIntersectionWithBvh single).IsNone (tag "one point" >> isTrue)
        )

        test ("2D: closed and open rings have no self intersection", fun _ ->
            for n in [ 4; 5; 20; 300 ] do
                for closed in [ true; false ] do
                    let r = Polyline2D.tryFindSelfIntersectionWithBvh (ring2D n closed)
                    assertThat (show2D r) (tag $"ring with {n} points, closed {closed}" >> isEqualTo "none")
        )

        test ("2D: touching non adjacent segments are found", fun _ ->
            // the fourth segment ends on the first one:
            let pl = Polyline2D.createFromPts [| Pt(0., 0.); Pt(10., 0.); Pt(10., 5.); Pt(5., 5.); Pt(5., 0.) |]
            match Polyline2D.tryFindSelfIntersectionWithBvh pl with
            | Some (_, i, j) ->
                assertThat i (tag "first segment" >> isEqualTo 0)
                assertThat j (tag "second segment" >> isEqualTo 3)
            | None -> failwith "the touching segments were not found"
        )

        test ("2D: random polylines give the same result as the Euclid functions", fun _ ->
            let rand = Random 4001
            let mutable hits = 0
            for n in [ 3; 4; 5; 8; 20; 60; 200 ] do
                for k in 1 .. 20 do
                    let pl = randomPolyline2D rand n (k % 2 = 0)
                    let expected = show2D (Polyline2D.tryFindSelfIntersection pl)
                    let expectedWithBRect = show2D (Polyline2D.tryFindSelfIntersectionWithBRect pl)
                    let actual = show2D (Polyline2D.tryFindSelfIntersectionWithBvh pl)
                    assertThat actual (tag $"{n} points, case {k}, like tryFindSelfIntersection" >> isEqualTo expected)
                    assertThat actual (tag $"{n} points, case {k}, like tryFindSelfIntersectionWithBRect" >> isEqualTo expectedWithBRect)
                    if actual <> "none" then hits <- hits + 1
            assertThat hits (tag "most random walks should cross themselves" >> isGreaterThan 70)
        )

        test ("3D: fewer than three points have no self intersection", fun _ ->
            let line = Polyline3D.createFromPts [| Pnt(0., 0., 0.); Pnt(1., 0., 0.) |]
            assertThat (Polyline3D.tryFindSelfIntersectionWithBvh line).IsNone (tag "two points" >> isTrue)
        )

        test ("3D: a negative skew distance fails", fun _ ->
            let pl = figureEight3D 20 0.0
            assertThat (fun () -> Polyline3D.tryFindSelfIntersectionWithBvh (pl, -1.0) |> ignore) (tag "negative skew distance should throw" >> throws)
        )

        test ("3D: the skew distance decides if strands that pass each other intersect", fun _ ->
            // the two strands pass each other 0.5 apart in Z:
            let pl = figureEight3D 50 0.25
            assertThat (show3D (Polyline3D.tryFindSelfIntersectionWithBvh pl)) (tag "default skew distance" >> isEqualTo "none")
            assertThat (show3D (Polyline3D.tryFindSelfIntersectionWithBvh (pl, 0.4))) (tag "skew distance 0.4" >> isEqualTo "none")
            let found = Polyline3D.tryFindSelfIntersectionWithBvh (pl, 0.6)
            assertThat found.IsSome (tag "skew distance 0.6 should find the crossing" >> isTrue)
            assertThat (show3D found) (tag "like brute force" >> isEqualTo (show3D (bruteForce3D pl 0.6)))
            // flat, the strands cross exactly:
            let flat = figureEight3D 50 0.0
            assertThat (show3D (Polyline3D.tryFindSelfIntersectionWithBvh flat)) (tag "flat figure eight like brute force" >> isEqualTo (show3D (bruteForce3D flat 1e-6)))
            assertThat (Polyline3D.tryFindSelfIntersectionWithBvh flat).IsSome (tag "flat figure eight crosses itself" >> isTrue)
        )

        test ("3D: random polylines give the same result as brute force", fun _ ->
            let rand = Random 4002
            let mutable hits = 0
            for n in [ 3; 4; 5; 8; 20; 60; 200 ] do
                for maxSkew in [ 1e-6; 0.5; 3.0 ] do
                    for k in 1 .. 10 do
                        let pl = randomPolyline3D rand n (k % 2 = 0)
                        let expected = show3D (bruteForce3D pl maxSkew)
                        let actual = show3D (Polyline3D.tryFindSelfIntersectionWithBvh (pl, maxSkew))
                        assertThat actual (tag $"{n} points, skew distance {maxSkew}, case {k}" >> isEqualTo expected)
                        if actual <> "none" then hits <- hits + 1
            assertThat hits (tag "the larger skew distances should find intersections" >> isGreaterThan 40)
        )
    ])
