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
                    let tree = Bvh.createFromBoxes (boxes, leafSize)
                    tree.BoxesByDistance (Pnt (-1., 0., 0.)) |> Seq.toArray |> checkEnumeration name coordinates
        )

        test ("2D build partitions structured input into ordered spatial ranges", fun _ ->
            for name, coordinates in structuredInputs () do
                let rects = coordinates |> Array.map (fun x -> BRect.createXY (x, 0., x, 0.))
                for leafSize in [ 1; 7 ] do
                    let tree = Bvh2D.createFromRects (rects, leafSize)
                    tree.RectsByDistance (Pt (-1., 0.)) |> Seq.toArray |> checkEnumeration name coordinates
                    // For these point rectangles, a correct median partition has ordered,
                    // non-overlapping X ranges at every depth, including below the root.
                    for level in tree.NodeRectanglesByDepth do
                        for i = 1 to level.Length - 1 do
                            assertThat (level.[i - 1].MaxX <= level.[i].MinX) (tag $"{name}: sibling ranges stay ordered" >> isTrue)
        )
    ])
