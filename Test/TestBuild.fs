module TestBuild

open Euclid
open System
open Scriptorium.Nib.Assertion
open Asserts
open type Scriptorium.Quill.Test

/// Odd and even ranges, including the orders that exhaust quickselect's work budget.
let private structuredInputs () =
    seq {
        for count in [ 2048; 2051 ] do
            yield "sorted", Array.init count float
            yield "reversed", Array.init count (fun i -> float (count - i - 1))
            yield "organ pipe", Array.init count (fun i -> float (min i (count - i - 1)))
            yield "rotated", Array.init count (fun i -> float ((i + 1) % count))
            yield "equal", Array.create count 3.0
            yield "repeated", Array.init count (fun i -> float (i % 7))
            let random = Random count
            yield "random", Array.init count (fun _ -> random.NextDouble() * float count)
    }

let private checkEnumeration name (coordinates: float[]) (found: (int * float)[]) =
    let expected = coordinates |> Array.map (fun x -> x + 1.0) |> Array.sort
    assertThat found.Length (tag $"{name}: every item is returned" >> isEqualTo coordinates.Length)
    assertThat (found |> Array.map fst |> Array.sort) (tag $"{name}: each index occurs once" >> isEqualTo [| 0 .. coordinates.Length - 1 |])
    for i = 0 to found.Length - 1 do
        let idx, distance = found.[i]
        assertThat distance (tag $"{name}: distance order at {i}" >> isCloseTo expected.[i])
        assertThat distance (tag $"{name}: index still identifies its item at {i}" >> isCloseTo (coordinates.[idx] + 1.0))

let tests =
    testList ("Build", [
        test ("3D build preserves every item on structured input", fun _ ->
            for name, coordinates in structuredInputs () do
                let boxes = coordinates |> Array.map (fun x -> BBox.createFromCenter (Pnt (x, 0., 0.), 0., 0., 0.))
                for leafSize in [ 1; 7 ] do
                    let tree = BVH.createFromBoxes (boxes, leafSize)
                    tree.BoxesByDistance (Pnt (-1., 0., 0.)) |> Seq.toArray |> checkEnumeration name coordinates
        )

        test ("2D build partitions structured input into ordered spatial ranges", fun _ ->
            for name, coordinates in structuredInputs () do
                let rects = coordinates |> Array.map (fun x -> BRect.createXY (x, 0., x, 0.))
                for leafSize in [ 1; 7 ] do
                    let tree = BVH2D.createFromRects (rects, leafSize)
                    tree.RectsByDistance (Pt (-1., 0.)) |> Seq.toArray |> checkEnumeration name coordinates
                    // For these point rectangles, a correct median partition has ordered,
                    // non-overlapping X ranges at every depth, including below the root.
                    for level in tree.NodeRectanglesByDepth do
                        for i = 1 to level.Length - 1 do
                            assertThat (level.[i - 1].MaxX <= level.[i].MinX) (tag $"{name}: sibling ranges stay ordered" >> isTrue)
        )

        test ("3D center splits prune long parallel lines on every axis", fun _ ->
            let count = 2048
            // An odd multiplier permutes these power-of-two offsets without sorting them spatially.
            let offsets = Array.init count (fun i -> float ((i * 811) % count))
            let q = float (count / 2) + 0.25
            for axis = 0 to 2 do
                let makeLine offset =
                    match axis with
                    | 0 -> Line3D (offset, 0., 0., offset, 1e6, 0.)
                    | 1 -> Line3D (0., offset, 0., 0., offset, 1e6)
                    | _ -> Line3D (0., 0., offset, 1e6, 0., offset)
                let query =
                    match axis with
                    | 0 -> Pnt (q, 5e5, 0.)
                    | 1 -> Pnt (0., q, 5e5)
                    | _ -> Pnt (5e5, 0., q)
                let tree = BVH.create (Array.map makeLine offsets, BBox.createFromLine)
                let mutable calls = 0
                let idx, distance = tree.ClosestItem (query, fun line ->
                    calls <- calls + 1
                    line.SqDistanceToPnt query)
                assertThat offsets.[idx] (tag $"axis {axis}: nearest line" >> isEqualTo (float (count / 2)))
                assertThat distance (tag $"axis {axis}: nearest distance" >> isCloseTo 0.25)
                // Count exact geometry tests instead of relying on machine-dependent timings.
                assertThat (calls <= 32) (tag $"axis {axis}: should prune most of the {count} lines, tested {calls}" >> isTrue)
        )

        test ("2D center splits prune long parallel lines on either axis", fun _ ->
            let count = 2048
            // Use the same deterministic permutation on .NET and Fable.
            let offsets = Array.init count (fun i -> float ((i * 811) % count))
            let q = float (count / 2) + 0.25
            for axis = 0 to 1 do
                let makeLine offset =
                    if axis = 0 then Line2D (offset, 0., offset, 1e6)
                    else Line2D (0., offset, 1e6, offset)
                let query = if axis = 0 then Pt (q, 5e5) else Pt (5e5, q)
                let tree = BVH2D.create (Array.map makeLine offsets, BRect.createFromLine)
                let mutable calls = 0
                let idx, distance = tree.ClosestItem (query, fun line ->
                    calls <- calls + 1
                    line.SqDistanceToPt query)
                assertThat offsets.[idx] (tag $"axis {axis}: nearest line" >> isEqualTo (float (count / 2)))
                assertThat distance (tag $"axis {axis}: nearest distance" >> isCloseTo 0.25)
                assertThat (calls <= 32) (tag $"axis {axis}: should prune most of the {count} lines, tested {calls}" >> isTrue)
        )
    ])
