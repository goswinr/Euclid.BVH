module TestBvh

open Euclid
open System
open Scriptorium.Nib.Assertion
open Asserts
open type Scriptorium.Quill.Test

// Every test below creates its own seeded generator instead of sharing one. That keeps
// each test repeatable on its own, whatever order the tests run in and whichever of them
// run at all, and the seeds differ per test so they do not all see the same input.

/// Creates random small boxes, clustered unevenly in space to mimic real world input.
let private randomBoxes (rand: Random) (count: int) : BBox[] =
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

let private randomBalls (rand: Random) (count: int) : Ball[] =
    Array.init count (fun _ ->
        { Center = Pnt (rand.NextDouble() * 100.0, rand.NextDouble() * 100.0, rand.NextDouble() * 20.0)
          Radius = rand.NextDouble() * 1.5 })

let tests =
    testList ("Bvh", [

        test ("createFromBoxes fails on empty input", fun _ ->
            assertThat (fun () -> Bvh.createFromBoxes [||] |> ignore) (tag "empty input should throw" >> throws)
        )

        test ("create accepts ResizeArray and seq inputs", fun _ ->
            let rand = Random 1001
            let balls = randomBalls rand 10
            let fromResizeArray = Bvh.create (ResizeArray balls, ballBox)
            let fromSeq = Bvh.create (balls |> Seq.map id, ballBox)
            assertThat fromResizeArray.Count (tag "ResizeArray count" >> isEqualTo balls.Length)
            assertThat fromSeq.Count (tag "sequence count" >> isEqualTo balls.Length)
            for i = 0 to balls.Length - 1 do
                assertThat (obj.ReferenceEquals (fromResizeArray.Items.[i], balls.[i])) (tag "ResizeArray items" >> isTrue)
                assertThat (obj.ReferenceEquals (fromSeq.Items.[i], balls.[i])) (tag "sequence items" >> isTrue)
        )

        test ("single box tree", fun _ ->
            let boxes = [| BBox.createFromSeq [ Pnt (0., 0., 0.); Pnt (1., 1., 1.) ] |]
            let bvh = Bvh.createFromBoxes boxes
            assertThat bvh.Count (tag "count" >> isEqualTo 1)
            let queryBox = BBox.createFromSeq [ Pnt (0., 3., 0.); Pnt (1., 4., 1.) ]
            let (i, d) = bvh.ClosestBox queryBox
            assertThat i (tag "closest index" >> isEqualTo 0)
            assertThat d (tag "closest box distance" >> isCloseTo 2.0)
        )

        test ("closest box matches brute force", fun _ ->
            let rand = Random 1002
            let boxes = randomBoxes rand 500
            let bvh = Bvh.createFromBoxes boxes
            let queryBox = BBox.createFromSeq [ Pnt (10., 10., 5.); Pnt (15., 12., 6.) ]
            let (_, d) = bvh.ClosestBox queryBox
            let mutable bestD = Double.MaxValue
            for b in boxes do
                bestD <- min bestD (boxDist queryBox b)
            assertThat d (tag "closest box distance should match brute force" >> isCloseTo bestD)
        )

        test ("nearest neighbor box of each box matches brute force", fun _ ->
            let rand = Random 1003
            let boxes = randomBoxes rand 300
            let bvh = Bvh.createFromBoxes boxes
            for i = 0 to boxes.Length - 1 do
                let (_, d) = bvh.ClosestBox (boxes.[i], i)
                let _, bd = bruteNearest boxes i
                assertThat d (tag $"nearest neighbor box distance of box {i}" >> isCloseTo bd)
        )

        test ("box based closest pair matches brute force", fun _ ->
            let rand = Random 1004
            let boxes = randomBoxes rand 400
            let bvh = Bvh.createFromBoxes boxes
            let pair = bvh.ClosestPair ()
            let mutable bd = Double.MaxValue
            for i = 0 to boxes.Length - 1 do
                for j = i + 1 to boxes.Length - 1 do
                    bd <- min bd (boxDist boxes.[i] boxes.[j])
            assertThat pair.Distance (tag "closest pair box distance should match brute force" >> isCloseTo bd)
            assertThat (pair.IdxA < pair.IdxB) (tag "pair indices should be ordered" >> isTrue)
        )

        test ("box based nearest neighbors match brute force", fun _ ->
            let rand = Random 1005
            let boxes = randomBoxes rand 200
            let bvh = Bvh.createFromBoxes boxes
            let nns = bvh.NearestNeighbors ()
            assertThat nns.Length (tag "one entry per box" >> isEqualTo boxes.Length)
            for i = 0 to boxes.Length - 1 do
                let _, bd = bruteNearest boxes i
                assertThat nns.[i].IdxA (tag "IdxA is the box itself" >> isEqualTo i)
                assertThat nns.[i].Distance (tag $"nearest neighbor box distance of box {i}" >> isCloseTo bd)
        )

        test ("box based close pairs match brute force", fun _ ->
            let rand = Random 1006
            let boxes = randomBoxes rand 300
            let bvh = Bvh.createFromBoxes boxes
            let maxDist = 2.5
            let pairs =
                bvh.ClosePairs maxDist
                |> Seq.map (fun p -> p.IdxA, p.IdxB)
                |> Set.ofSeq
            let brute = brutePairs boxes maxDist
            assertThat pairs (tag "box pairs within tolerance should match brute force" >> isEqualTo brute)
        )

        test ("overlapping boxes found with zero tolerance", fun _ ->
            let rand = Random 1007
            let boxes = randomBoxes rand 300
            let bvh = Bvh.createFromBoxes boxes
            let pairs =
                bvh.ClosePairs 0.0
                |> Seq.map (fun p -> p.IdxA, p.IdxB)
                |> Set.ofSeq
            let brute = brutePairs boxes 0.0
            assertThat pairs (tag "overlapping box pairs should match brute force" >> isEqualTo brute)
        )

        test ("items in box matches brute force", fun _ ->
            let rand = Random 1008
            let boxes = randomBoxes rand 300
            let bvh = Bvh.createFromBoxes boxes
            let box = BBox.createFromSeq [ Pnt (20., 20., 0.); Pnt (60., 60., 20.) ]
            let found = bvh.ItemsInBox box |> Set.ofSeq
            let brute =
                seq { for i = 0 to boxes.Length - 1 do
                        if sqBoxDist box boxes.[i] <= 0.0 then i }
                |> Set.ofSeq
            assertThat found (tag "items in box should match brute force" >> isEqualTo brute)
        )

        test ("generic create with custom items and exact distance", fun _ ->
            let rand = Random 1009
            let balls = randomBalls rand 300
            let bvh = Bvh.create (balls, ballBox)
            // exact closest pair via callback, compared to brute force:
            let pair = bvh.ClosestPair (fun a b -> ballSqDist a b)
            let mutable bd = Double.MaxValue
            for i = 0 to balls.Length - 1 do
                for j = i + 1 to balls.Length - 1 do
                    bd <- min bd (sqrt (ballSqDist balls.[i] balls.[j]))
            assertThat pair.Distance (tag "exact closest ball pair should match brute force" >> isCloseTo bd)
        )

        test ("generic close pairs with exact distance match brute force", fun _ ->
            let rand = Random 1010
            let balls = randomBalls rand 300
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
            assertThat pairs (tag "exact ball pairs within tolerance should match brute force" >> isEqualTo brute)
        )

        test ("closest item with exact distance matches brute force", fun _ ->
            let rand = Random 1011
            let balls = randomBalls rand 300
            let bvh = Bvh.create (balls, ballBox)
            let query = { Center = Pnt (50., 50., 10.); Radius = 1.0 }
            let (_, d) = bvh.ClosestItem (ballBox query, ballSqDist query)
            let mutable bd = Double.MaxValue
            for b in balls do
                bd <- min bd (sqrt (ballSqDist query b))
            assertThat d (tag "closest ball distance should match brute force" >> isCloseTo bd)
        )

        test ("different leaf sizes give the same result", fun _ ->
            let rand = Random 1012
            let boxes = randomBoxes rand 250
            let queryBox = BBox.createFromSeq [ Pnt (50., 50., 10.); Pnt (55., 52., 11.) ]
            let results =
                [ 1; 2; 8; 32 ]
                |> List.map (fun ls ->
                    let bvh = Bvh.createFromBoxes (boxes, ls)
                    let (_, d) = bvh.ClosestBox queryBox
                    d)
            for d in results do
                assertThat d (tag "distance should not depend on leaf size" >> isCloseTo results.Head)
        )

        test ("tree box contains all item boxes", fun _ ->
            let rand = Random 1013
            let boxes = randomBoxes rand 100
            let bvh = Bvh.createFromBoxes boxes
            for b in boxes do
                assertThat (bvh.Box.Contains b) (tag "tree box should contain every item box" >> isTrue)
        )

        test ("closest box to point matches brute force", fun _ ->
            let rand = Random 1014
            let boxes = randomBoxes rand 400
            let bvh = Bvh.createFromBoxes boxes
            let pt = Pnt (42., 61., 7.)
            let (_, d) = bvh.ClosestBox pt
            let queryBox = BBox.createFromSeq [ pt ]
            let mutable bestD = Double.MaxValue
            for b in boxes do
                bestD <- min bestD (boxDist queryBox b)
            assertThat d (tag "closest box distance to point should match brute force" >> isCloseTo bestD)
        )

        test ("closest item to point with exact distance matches brute force", fun _ ->
            let rand = Random 1015
            let balls = randomBalls rand 300
            let bvh = Bvh.create (balls, ballBox)
            let pt = Pnt (50., 50., 10.)
            let sqDistTo (b: Ball) =
                let d = max 0.0 (b.Center.DistanceTo pt - b.Radius)
                d * d
            let (_, d) = bvh.ClosestItem (pt, sqDistTo)
            let mutable bestD = Double.MaxValue
            for b in balls do
                bestD <- min bestD (sqrt (sqDistTo b))
            assertThat d (tag "closest ball distance to point should match brute force" >> isCloseTo bestD)
        )

        test ("items near point match brute force", fun _ ->
            let rand = Random 1016
            let boxes = randomBoxes rand 300
            let bvh = Bvh.createFromBoxes boxes
            let pt = Pnt (50., 50., 10.)
            let tol = 8.0
            let found = bvh.ItemsNearPoint (pt, tol) |> Set.ofSeq
            let queryBox = BBox.createFromSeq [ pt ]
            let brute =
                seq { for i = 0 to boxes.Length - 1 do
                        if boxDist queryBox boxes.[i] <= tol then i }
                |> Set.ofSeq
            assertThat found (tag "items near point should match brute force" >> isEqualTo brute)
        )

        test ("items near point with zero tolerance finds containing boxes", fun _ ->
            let rand = Random 1017
            let boxes = randomBoxes rand 300
            let bvh = Bvh.createFromBoxes boxes
            // use the center of the first box, it is guaranteed to be inside it:
            let pt = boxes.[0].Center
            let found = bvh.ItemsNearPoint pt |> Set.ofSeq
            assertThat (found.Contains 0) (tag "the containing box should be found" >> isTrue)
            let queryBox = BBox.createFromSeq [ pt ]
            let brute =
                seq { for i = 0 to boxes.Length - 1 do
                        if boxDist queryBox boxes.[i] <= 0.0 then i }
                |> Set.ofSeq
            assertThat found (tag "containing boxes should match brute force" >> isEqualTo brute)
        )

        test ("boxes by distance enumerates every box in increasing distance order", fun _ ->
            let rand = Random 1018
            let boxes = randomBoxes rand 300
            let bvh = Bvh.createFromBoxes boxes
            let queryBox = BBox.createFromSeq [ Pnt (30., 70., 4.); Pnt (33., 72., 5.) ]
            let found = bvh.BoxesByDistance queryBox |> Seq.toArray
            assertThat found.Length (tag "every box should be enumerated" >> isEqualTo boxes.Length)
            assertThat (found |> Array.map fst |> Set.ofArray |> Set.count) (tag "every index only once" >> isEqualTo boxes.Length)
            // the distances must be non decreasing and match the brute force distances sorted:
            let brute = boxes |> Array.map (boxDist queryBox) |> Array.sort
            for i = 0 to found.Length - 1 do
                assertThat (snd found.[i]) (tag $"distance at position {i}" >> isCloseTo brute.[i])
                assertThat (snd found.[i]) (tag $"reported distance of box {fst found.[i]}" >> isCloseTo (boxDist queryBox boxes.[fst found.[i]]))
        )

        test ("boxes by distance starts at the closest box and is re-enumerable", fun _ ->
            let rand = Random 1019
            let boxes = randomBoxes rand 400
            let bvh = Bvh.createFromBoxes boxes
            let queryBox = BBox.createFromSeq [ Pnt (10., 10., 5.); Pnt (15., 12., 6.) ]
            let (_, closestD) = bvh.ClosestBox queryBox
            let lazySeq = bvh.BoxesByDistance queryBox
            // taking only the first few entries must not need the whole tree, and must agree with ClosestBox:
            let firstFive = lazySeq |> Seq.truncate 5 |> Seq.toArray
            assertThat firstFive.Length (tag "five entries taken" >> isEqualTo 5)
            assertThat (snd firstFive.[0]) (tag "first entry should be the closest box" >> isCloseTo closestD)
            // a second enumeration starts a new traversal and gives the same result:
            let again = lazySeq |> Seq.truncate 5 |> Seq.toArray
            assertThat (again |> Array.map snd |> Array.toList) (tag "re-enumeration" >> isEqualTo (firstFive |> Array.map snd |> Array.toList))
        )

        test ("boxes by distance skips the given index", fun _ ->
            let rand = Random 1020
            let boxes = randomBoxes rand 200
            let bvh = Bvh.createFromBoxes boxes
            let skip = 17
            let found = bvh.BoxesByDistance (boxes.[skip], skip) |> Seq.toArray
            assertThat found.Length (tag "all but the skipped box" >> isEqualTo (boxes.Length - 1))
            assertThat (found |> Array.exists (fun (i, _) -> i = skip)) (tag "the skipped box should not appear" >> isFalse)
            let _, nearestD = bruteNearest boxes skip
            assertThat (snd found.[0]) (tag "first entry should be the nearest neighbor" >> isCloseTo nearestD)
        )

        test ("items by distance with exact distance matches brute force order", fun _ ->
            let rand = Random 1021
            let balls = randomBalls rand 300
            let bvh = Bvh.create (balls, ballBox)
            let query = { Center = Pnt (50., 50., 10.); Radius = 1.0 }
            let found = bvh.ItemsByDistance (ballBox query, ballSqDist query) |> Seq.toArray
            assertThat found.Length (tag "every ball should be enumerated" >> isEqualTo balls.Length)
            let brute = balls |> Array.map (fun b -> sqrt (ballSqDist query b)) |> Array.sort
            for i = 0 to found.Length - 1 do
                assertThat (snd found.[i]) (tag $"exact distance at position {i}" >> isCloseTo brute.[i])
                assertThat (snd found.[i]) (tag $"reported distance of ball {fst found.[i]}" >> isCloseTo (sqrt (ballSqDist query balls.[fst found.[i]])))
        )

        test ("boxes by distance to point matches brute force order", fun _ ->
            let rand = Random 1022
            let boxes = randomBoxes rand 300
            let bvh = Bvh.createFromBoxes boxes
            let pt = Pnt (42., 61., 7.)
            let ptBox = BBox.createFromSeq [ pt ]
            let found = bvh.BoxesByDistance pt |> Seq.toArray
            assertThat found.Length (tag "every box should be enumerated" >> isEqualTo boxes.Length)
            let brute = boxes |> Array.map (boxDist ptBox) |> Array.sort
            for i = 0 to found.Length - 1 do
                assertThat (snd found.[i]) (tag $"distance to point at position {i}" >> isCloseTo brute.[i])
        )

        test ("items by distance to point with exact distance matches brute force order", fun _ ->
            let rand = Random 1023
            let balls = randomBalls rand 300
            let bvh = Bvh.create (balls, ballBox)
            let pt = Pnt (50., 50., 10.)
            let sqDistTo (b: Ball) =
                let d = max 0.0 (b.Center.DistanceTo pt - b.Radius)
                d * d
            let found = bvh.ItemsByDistance (pt, sqDistTo) |> Seq.toArray
            assertThat found.Length (tag "every ball should be enumerated" >> isEqualTo balls.Length)
            let brute = balls |> Array.map (fun b -> sqrt (sqDistTo b)) |> Array.sort
            for i = 0 to found.Length - 1 do
                assertThat (snd found.[i]) (tag $"exact distance to point at position {i}" >> isCloseTo brute.[i])
        )

        test ("by distance order does not depend on leaf size", fun _ ->
            let rand = Random 1024
            let boxes = randomBoxes rand 250
            let queryBox = BBox.createFromSeq [ Pnt (50., 50., 10.); Pnt (55., 52., 11.) ]
            let distancesWithLeafSize leafSize =
                let bvh = Bvh.createFromBoxes (boxes, leafSize)
                bvh.BoxesByDistance queryBox |> Seq.map snd |> Seq.toArray
            let reference = distancesWithLeafSize 1
            for leafSize in [ 2; 8; 32 ] do
                let distances = distancesWithLeafSize leafSize
                assertThat distances.Length (tag $"count with leaf size {leafSize}" >> isEqualTo reference.Length)
                for i = 0 to distances.Length - 1 do
                    assertThat distances.[i] (tag $"distance at position {i} with leaf size {leafSize}" >> isCloseTo reference.[i])
        )
    ])
