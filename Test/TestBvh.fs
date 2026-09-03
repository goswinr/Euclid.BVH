module TestBvh

open Euclid
open System

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
open Fable.Mocha
#else
open Expecto
#endif

/// A deterministic pseudo random generator so tests are repeatable.
let private rand = Random 4242

/// Creates random small boxes, clustered unevenly in space to mimic real world input.
let private randomBoxes (count: int) : BBox[] =
    Array.init count (fun _ ->
        let cx = rand.NextDouble() * 100.0
        let cy = rand.NextDouble() * 100.0
        let cz = rand.NextDouble() * 20.0
        // cluster by rounding centers to a coarse grid on some boxes:
        let cx = if rand.NextDouble() < 0.5 then Math.Round(cx / 25.0) * 25.0 + rand.NextDouble() * 3.0 else cx
        let cy = if rand.NextDouble() < 0.5 then Math.Round(cy / 25.0) * 25.0 + rand.NextDouble() * 3.0 else cy
        let sx = rand.NextDouble() * 2.0
        let sy = rand.NextDouble() * 2.0
        let sz = rand.NextDouble() * 2.0
        BBox.createFromSeq [ Pnt (cx, cy, cz); Pnt (cx + sx, cy + sy, cz + sz) ])

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

let private boxDist a b = sqrt (sqBoxDist a b)

/// Brute force nearest neighbor box of box i.
let private bruteNearest (boxes: BBox[]) (i: int) : int * float =
    let mutable bestJ = -1
    let mutable bestD = Double.MaxValue
    for j = 0 to boxes.Length - 1 do
        if j <> i then
            let d = boxDist boxes.[i] boxes.[j]
            if d < bestD then
                bestD <- d
                bestJ <- j
    bestJ, bestD

/// Brute force all pairs of boxes closer than maxDist.
let private brutePairs (boxes: BBox[]) (maxDist: float) : Set<int * int> =
    let mutable result = Set.empty
    for i = 0 to boxes.Length - 1 do
        for j = i + 1 to boxes.Length - 1 do
            if boxDist boxes.[i] boxes.[j] <= maxDist then
                result <- result.Add (i, j)
    result

/// A custom item type to test the generic build with a box function.
type private Ball = { Center: Pnt; Radius: float }

let private ballBox (b: Ball) : BBox =
    BBox.createFromCenter (b.Center, 2.0 * b.Radius, 2.0 * b.Radius, 2.0 * b.Radius)

/// The exact squared distance between the surfaces of two balls (0.0 if they intersect).
let private ballSqDist (a: Ball) (b: Ball) : float =
    let d = a.Center.DistanceTo b.Center - a.Radius - b.Radius
    let d = max 0.0 d
    d * d

let private randomBalls (count: int) : Ball[] =
    Array.init count (fun _ ->
        { Center = Pnt (rand.NextDouble() * 100.0, rand.NextDouble() * 100.0, rand.NextDouble() * 20.0)
          Radius = rand.NextDouble() * 1.5 })

let tests =
    testList "Bvh" [

        test "createFromBoxes fails on empty input" {
            Expect.throws (fun () -> Bvh.createFromBoxes [||] |> ignore) "empty input should throw"
        }

        test "single box tree" {
            let boxes = [| BBox.createFromSeq [ Pnt (0., 0., 0.); Pnt (1., 1., 1.) ] |]
            let bvh = Bvh.createFromBoxes boxes
            Expect.equal bvh.Count 1 "count"
            let queryBox = BBox.createFromSeq [ Pnt (0., 3., 0.); Pnt (1., 4., 1.) ]
            let struct (i, d) = bvh.ClosestBox queryBox
            Expect.equal i 0 "closest index"
            Expect.floatClose Accuracy.high d 2.0 "closest box distance"
        }

        test "closest box matches brute force" {
            let boxes = randomBoxes 500
            let bvh = Bvh.createFromBoxes boxes
            let queryBox = BBox.createFromSeq [ Pnt (10., 10., 5.); Pnt (15., 12., 6.) ]
            let struct (_, d) = bvh.ClosestBox queryBox
            let mutable bestD = Double.MaxValue
            for b in boxes do
                bestD <- min bestD (boxDist queryBox b)
            Expect.floatClose Accuracy.high d bestD "closest box distance should match brute force"
        }

        test "nearest neighbor box of each box matches brute force" {
            let boxes = randomBoxes 300
            let bvh = Bvh.createFromBoxes boxes
            for i = 0 to boxes.Length - 1 do
                let struct (_, d) = bvh.ClosestBox (boxes.[i], i)
                let _, bd = bruteNearest boxes i
                Expect.floatClose Accuracy.high d bd $"nearest neighbor box distance of box {i}"
        }

        test "box based closest pair matches brute force" {
            let boxes = randomBoxes 400
            let bvh = Bvh.createFromBoxes boxes
            let pair = bvh.ClosestPair ()
            let mutable bd = Double.MaxValue
            for i = 0 to boxes.Length - 1 do
                for j = i + 1 to boxes.Length - 1 do
                    bd <- min bd (boxDist boxes.[i] boxes.[j])
            Expect.floatClose Accuracy.high pair.Distance bd "closest pair box distance should match brute force"
            Expect.isTrue (pair.IdxA < pair.IdxB) "pair indices should be ordered"
        }

        test "box based nearest neighbors match brute force" {
            let boxes = randomBoxes 200
            let bvh = Bvh.createFromBoxes boxes
            let nns = bvh.NearestNeighbors ()
            Expect.equal nns.Length boxes.Length "one entry per box"
            for i = 0 to boxes.Length - 1 do
                let _, bd = bruteNearest boxes i
                Expect.equal nns.[i].IdxA i "IdxA is the box itself"
                Expect.floatClose Accuracy.high nns.[i].Distance bd $"nearest neighbor box distance of box {i}"
        }

        test "box based close pairs match brute force" {
            let boxes = randomBoxes 300
            let bvh = Bvh.createFromBoxes boxes
            let maxDist = 2.5
            let pairs =
                bvh.ClosePairs maxDist
                |> Seq.map (fun p -> p.IdxA, p.IdxB)
                |> Set.ofSeq
            let brute = brutePairs boxes maxDist
            Expect.equal pairs brute "box pairs within tolerance should match brute force"
        }

        test "overlapping boxes found with zero tolerance" {
            let boxes = randomBoxes 300
            let bvh = Bvh.createFromBoxes boxes
            let pairs =
                bvh.ClosePairs 0.0
                |> Seq.map (fun p -> p.IdxA, p.IdxB)
                |> Set.ofSeq
            let brute = brutePairs boxes 0.0
            Expect.equal pairs brute "overlapping box pairs should match brute force"
        }

        test "items in box matches brute force" {
            let boxes = randomBoxes 300
            let bvh = Bvh.createFromBoxes boxes
            let box = BBox.createFromSeq [ Pnt (20., 20., 0.); Pnt (60., 60., 20.) ]
            let found = bvh.ItemsInBox box |> Set.ofSeq
            let brute =
                seq { for i = 0 to boxes.Length - 1 do
                        if sqBoxDist box boxes.[i] <= 0.0 then i }
                |> Set.ofSeq
            Expect.equal found brute "items in box should match brute force"
        }

        test "generic create with custom items and exact distance" {
            let balls = randomBalls 300
            let bvh = Bvh.create (balls, ballBox)
            // exact closest pair via callback, compared to brute force:
            let pair = bvh.ClosestPair (fun a b -> ballSqDist a b)
            let mutable bd = Double.MaxValue
            for i = 0 to balls.Length - 1 do
                for j = i + 1 to balls.Length - 1 do
                    bd <- min bd (sqrt (ballSqDist balls.[i] balls.[j]))
            Expect.floatClose Accuracy.high pair.Distance bd "exact closest ball pair should match brute force"
        }

        test "generic close pairs with exact distance match brute force" {
            let balls = randomBalls 300
            let bvh = Bvh.create (balls, ballBox)
            let maxDist = 2.0
            let pairs =
                bvh.ClosePairs (maxDist, fun a b -> ballSqDist a b)
                |> Seq.map (fun p -> p.IdxA, p.IdxB)
                |> Set.ofSeq
            let brute =
                seq { for i = 0 to balls.Length - 1 do
                        for j = i + 1 to balls.Length - 1 do
                            if sqrt (ballSqDist balls.[i] balls.[j]) <= maxDist then (i, j) }
                |> Set.ofSeq
            Expect.equal pairs brute "exact ball pairs within tolerance should match brute force"
        }

        test "closest item with exact distance matches brute force" {
            let balls = randomBalls 300
            let bvh = Bvh.create (balls, ballBox)
            let query = { Center = Pnt (50., 50., 10.); Radius = 1.0 }
            let struct (_, d) = bvh.ClosestItem (ballBox query, ballSqDist query)
            let mutable bd = Double.MaxValue
            for b in balls do
                bd <- min bd (sqrt (ballSqDist query b))
            Expect.floatClose Accuracy.high d bd "closest ball distance should match brute force"
        }

        test "different leaf sizes give the same result" {
            let boxes = randomBoxes 250
            let queryBox = BBox.createFromSeq [ Pnt (50., 50., 10.); Pnt (55., 52., 11.) ]
            let results =
                [ 1; 2; 8; 32 ]
                |> List.map (fun ls ->
                    let bvh = Bvh.createFromBoxes (boxes, ls)
                    let struct (_, d) = bvh.ClosestBox queryBox
                    d)
            for d in results do
                Expect.floatClose Accuracy.high d results.Head "distance should not depend on leaf size"
        }

        test "tree box contains all item boxes" {
            let boxes = randomBoxes 100
            let bvh = Bvh.createFromBoxes boxes
            for b in boxes do
                Expect.isTrue (bvh.Box.Contains b) "tree box should contain every item box"
        }

        test "closest box to point matches brute force" {
            let boxes = randomBoxes 400
            let bvh = Bvh.createFromBoxes boxes
            let pt = Pnt (42., 61., 7.)
            let struct (_, d) = bvh.ClosestBox pt
            let queryBox = BBox.createFromSeq [ pt ]
            let mutable bestD = Double.MaxValue
            for b in boxes do
                bestD <- min bestD (boxDist queryBox b)
            Expect.floatClose Accuracy.high d bestD "closest box distance to point should match brute force"
        }

        test "closest item to point with exact distance matches brute force" {
            let balls = randomBalls 300
            let bvh = Bvh.create (balls, ballBox)
            let pt = Pnt (50., 50., 10.)
            let sqDistTo (b: Ball) =
                let d = max 0.0 (b.Center.DistanceTo pt - b.Radius)
                d * d
            let struct (_, d) = bvh.ClosestItem (pt, sqDistTo)
            let mutable bestD = Double.MaxValue
            for b in balls do
                bestD <- min bestD (sqrt (sqDistTo b))
            Expect.floatClose Accuracy.high d bestD "closest ball distance to point should match brute force"
        }

        test "items near point match brute force" {
            let boxes = randomBoxes 300
            let bvh = Bvh.createFromBoxes boxes
            let pt = Pnt (50., 50., 10.)
            let tol = 8.0
            let found = bvh.ItemsNearPoint (pt, tol) |> Set.ofSeq
            let queryBox = BBox.createFromSeq [ pt ]
            let brute =
                seq { for i = 0 to boxes.Length - 1 do
                        if boxDist queryBox boxes.[i] <= tol then i }
                |> Set.ofSeq
            Expect.equal found brute "items near point should match brute force"
        }

        test "items near point with zero tolerance finds containing boxes" {
            let boxes = randomBoxes 300
            let bvh = Bvh.createFromBoxes boxes
            // use the center of the first box, it is guaranteed to be inside it:
            let pt = boxes.[0].Center
            let found = bvh.ItemsNearPoint pt |> Set.ofSeq
            Expect.isTrue (found.Contains 0) "the containing box should be found"
            let queryBox = BBox.createFromSeq [ pt ]
            let brute =
                seq { for i = 0 to boxes.Length - 1 do
                        if boxDist queryBox boxes.[i] <= 0.0 then i }
                |> Set.ofSeq
            Expect.equal found brute "containing boxes should match brute force"
        }
    ]
