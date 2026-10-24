/// The inputs, the 3D brute force baseline and the correctness check, shared by the .NET and Fable benchmarks.
module Shared

open System
open Euclid

/// A closed circle with the given count of points, the last point is the same as the first.
/// It has no self intersection, so every method has to test all of its candidate segment pairs.
let ring (pointCount: int) : Polyline2D =
    let radius = 100.0
    let segmentCount = pointCount - 1
    Polyline2D.createFromPts [|
        for i in 0 .. segmentCount ->
            let angle = 2.0 * Math.PI * float (i % segmentCount) / float segmentCount
            Pt(radius * cos angle, radius * sin angle) |]

/// The ring in 3D: a closed circle whose height goes up and down three times around it.
let ring3D (pointCount: int) : Polyline3D =
    let segmentCount = pointCount - 1
    Polyline3D.createFromPts [|
        for i in 0 .. segmentCount ->
            let angle = 2.0 * Math.PI * float (i % segmentCount) / float segmentCount
            Pnt(100.0 * cos angle, 100.0 * sin angle, 10.0 * sin (3.0 * angle)) |]

/// A closed figure eight (a lemniscate of Gerono) with the given count of points.
/// Its two lobes cross once at the origin. The angle offset keeps every vertex off the crossing.
let figureEight (pointCount: int) : Polyline2D =
    let segmentCount = pointCount - 1
    Polyline2D.createFromPts [|
        for i in 0 .. segmentCount ->
            let t = 0.1 + 2.0 * Math.PI * float (i % segmentCount) / float segmentCount
            Pt(100.0 * cos t, 50.0 * sin (2.0 * t)) |]

/// The figure eight in the XY plane of 3D space.
let figureEight3D (pointCount: int) : Polyline3D =
    let segmentCount = pointCount - 1
    Polyline3D.createFromPts [|
        for i in 0 .. segmentCount ->
            let t = 0.1 + 2.0 * Math.PI * float (i % segmentCount) / float segmentCount
            Pnt(100.0 * cos t, 50.0 * sin (2.0 * t), 0.0) |]

/// The brute force baseline in 3D, because Euclid has no self intersection test for a Polyline3D.
/// It has the same loops as Polyline2D.tryFindSelfIntersection: it tests every pair of non adjacent segments,
/// with the same XLineXYZ.tryIntersect test and default skew distance as Polyline3D.tryFindSelfIntersectionWithBvh,
/// and stops at the first intersection.
let tryFindSelfIntersection3D (pl: Polyline3D) : option<Pnt * int * int> =
    let xyzs = pl.XYZs
    let segmentCount = xyzs.Count / 3 - 1
    if segmentCount < 2 then
        None
    else
        let vs = pl.SegmentVectorsXYZ
        let isClosed = pl.IsAlmostClosed 1e-7
        let mutable result = None
        let mutable i = 0
        while result.IsNone && i < segmentCount do
            let ia = 3 * i
            let pax, pay, paz = xyzs.[ia], xyzs.[ia + 1], xyzs.[ia + 2]
            let vax, vay, vaz = vs.[ia], vs.[ia + 1], vs.[ia + 2]
            // on a closed polyline the first and the last segment are adjacent:
            let lastJ = if isClosed && i = 0 then segmentCount - 2 else segmentCount - 1
            let mutable j = i + 2
            while result.IsNone && j <= lastJ do
                let ib = 3 * j
                match XLineXYZ.tryIntersect(pax, pay, paz, xyzs.[ib], xyzs.[ib + 1], xyzs.[ib + 2],
                                            vax, vay, vaz, vs.[ib], vs.[ib + 1], vs.[ib + 2],
                                            1e-6, Tangent.``0.25``, 1e-6) with
                | ValueSome pt -> result <- Some (pt, i, j)
                | ValueNone -> ()
                j <- j + 1
            i <- i + 1
        result

let pointCounts = [| 5; 20; 50; 100; 200; 500; 1_000; 2_000; 10_000 |]

/// Checks that all methods agree before anything is timed.
let verify () =
    let show2D (r: option<Pt * int * int>) =
        match r with
        | Some (pt, i, j) -> sprintf "segments %d and %d at %.6f, %.6f" i j pt.X pt.Y
        | None -> "none"

    let show3D (r: option<Pnt * int * int>) =
        match r with
        | Some (pt, i, j) -> sprintf "segments %d and %d at %.6f, %.6f, %.6f" i j pt.X pt.Y pt.Z
        | None -> "none"

    let check name n (results: string[]) expectHit =
        if results |> Array.exists (fun r -> r <> results.[0]) then
            failwith $"""{name} with {n} points: methods disagree: {String.concat " | " results}"""
        match expectHit, results.[0] with
        | Some true, "none" -> failwith $"{name} with {n} points: expected a self intersection."
        | Some false, r when r <> "none" -> failwith $"{name} with {n} points: expected no self intersection, got {r}."
        | _ -> ()

    let check2D name (pl: Polyline2D) expectHit =
        check name pl.PointCount [|
            show2D (Polyline2D.tryFindSelfIntersection pl)
            show2D (Polyline2D.tryFindSelfIntersectionWithBRect pl)
            show2D (Polyline2D.tryFindSelfIntersectionWithBvh pl) |] expectHit

    let check3D name (pl: Polyline3D) expectHit =
        check name pl.PointCount [|
            show3D (tryFindSelfIntersection3D pl)
            show3D (Polyline3D.tryFindSelfIntersectionWithBvh pl) |] expectHit

    for n in pointCounts do
        check2D "ring" (ring n) (Some false)
        check3D "3D ring" (ring3D n) (Some false)
        // With very few points the figure eight is too coarse to be guaranteed to cross itself.
        let expectHit = if n >= 20 then Some true else None
        check2D "figure eight" (figureEight n) expectHit
        check3D "3D figure eight" (figureEight3D n) expectHit
    let counts = pointCounts |> Array.map string |> String.concat ", "
    printfn "Verified: all methods agree on 2D and 3D rings and figure eights for %s points." counts
