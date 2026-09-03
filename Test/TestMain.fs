module Euclid.BVH.Tests
open System

open Expecto
open System.Globalization
open System.Threading
Thread.CurrentThread.CurrentCulture   <- CultureInfo.GetCultureInfo "en-US" // so that a float never has a comma as decimal separator
Thread.CurrentThread.CurrentUICulture <- CultureInfo.GetCultureInfo "en-US"
let mutable cliArgs : string[] = [||]
let test x = runTestsWithCLIArgs [] cliArgs x

let run () =
    test TestLineBvh.tests

[<EntryPoint>]
let main (args: string[]) =
    cliArgs <- args
    let r = run ()
    if r = 0 then
        printfn "All tests passed"
    else
        printfn "%d tests failed" r
    r
