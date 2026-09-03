module TestBvh2d

open Euclid
open System

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
open Fable.Mocha
#else
open Expecto
#endif

let private rectDistance (a: BRect) (b: BRect) =
    let axis aMin aMax bMin bMax =
        if bMin > aMax then bMin - aMax
        elif aMin > bMax then aMin - bMax
        else 0.0
    let dx = axis a.MinX a.MaxX b.MinX b.MaxX
    let dy = axis a.MinY a.MaxY b.MinY b.MaxY
    sqrt (dx * dx + dy * dy)

let tests =
    testList "Bvh2d" [
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

        test "2D line wrapper uses rectangle bounds" {
            let lines = [| Line2D (0., 0., 1., 0.); Line2D (5., 0., 6., 0.) |]
            let bvh = LineBvh2d.create lines
            let struct (idx, distance) = bvh.ClosestLine (Pt (0.5, 2.))
            Expect.equal idx 0 "first line is closest"
            Expect.floatClose Accuracy.high distance 2.0 "exact planar line distance"
        }
    ]
