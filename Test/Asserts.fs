/// Assertions that Scriptorium.Nib does not provide out of the box.
module Asserts

open Scriptorium.Nib.Assertion

/// The absolute part of the tolerance used by isCloseTo.
[<Literal>]
let private absoluteTolerance = 1e-10

/// The relative part of the tolerance used by isCloseTo.
[<Literal>]
let private relativeTolerance = 1e-9

/// Passes when the subject is within a small absolute and relative tolerance of expected.
/// The tolerance is the one Expecto called Accuracy.high, so the ported tests compare
/// floats exactly as strictly as they did before.
let isCloseTo (expected: float) : Assertion<float> =
    assertion
        (fun actual -> abs (actual - expected) <= absoluteTolerance + relativeTolerance * max (abs actual) (abs expected))
        (fun actual -> $"given {actual} should be close to {expected}")
