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

let tests =
    testList ("Polyline self intersection with a BVH", [

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
