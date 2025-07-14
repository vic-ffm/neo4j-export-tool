// MIT License
//
// Copyright (c) 2025-present State Government of Victoria
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

module Neo4jExport.Tests.Unit.ErrorManagement.NonEmptyListTests

open Expecto
open FsCheck
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.Tests.Helpers.TestHelpers

[<Tests>]
let tests =
    testList
        "NonEmptyList"
        [ testList
              "construction"
              [ testCase "singleton creates single-element list"
                <| fun () ->
                    let nel = NonEmptyList.singleton 42

                    match nel with
                    | NonEmptyList(h, t) ->
                        test <@ h = 42 @>
                        test <@ t = [] @>

                testCase "direct construction preserves elements"
                <| fun () ->
                    let nel = NonEmptyList(1, [ 2; 3; 4 ])

                    match nel with
                    | NonEmptyList(h, t) ->
                        test <@ h = 1 @>
                        test <@ t = [ 2; 3; 4 ] @>

                testCase "ofList returns None for empty list"
                <| fun () ->
                    let result = NonEmptyList.ofList []
                    test <@ result = None @>

                testCase "ofList returns Some for non-empty list"
                <| fun () ->
                    let result = NonEmptyList.ofList [ 1; 2; 3 ]

                    match result with
                    | Some(NonEmptyList(h, t)) ->
                        test <@ h = 1 @>
                        test <@ t = [ 2; 3 ] @>
                    | None -> failtest "Expected Some but got None" ]

          testList
              "operations"
              [ testCase "cons prepends element"
                <| fun () ->
                    let nel = NonEmptyList.singleton 2
                    let result = NonEmptyList.cons 1 nel

                    match result with
                    | NonEmptyList(h, t) ->
                        test <@ h = 1 @>
                        test <@ t = [ 2 ] @>

                testCase "toList converts to regular list"
                <| fun () ->
                    let nel = NonEmptyList(1, [ 2; 3 ])
                    let list = NonEmptyList.toList nel
                    test <@ list = [ 1; 2; 3 ] @>

                testCase "head returns first element"
                <| fun () ->
                    let nel = NonEmptyList(42, [ 1; 2 ])
                    test <@ NonEmptyList.head nel = 42 @>

                testCase "tail returns None for singleton"
                <| fun () ->
                    let nel = NonEmptyList.singleton 1
                    test <@ NonEmptyList.tail nel = None @>

                testCase "tail returns Some for multi-element list"
                <| fun () ->
                    let nel = NonEmptyList(1, [ 2; 3 ])

                    match NonEmptyList.tail nel with
                    | Some(NonEmptyList(h, t)) ->
                        test <@ h = 2 @>
                        test <@ t = [ 3 ] @>
                    | None -> failtest "Expected Some but got None"

                testCase "map transforms all elements"
                <| fun () ->
                    let nel = NonEmptyList(1, [ 2; 3 ])
                    let result = NonEmptyList.map ((*) 2) nel

                    match result with
                    | NonEmptyList(h, t) ->
                        test <@ h = 2 @>
                        test <@ t = [ 4; 6 ] @>

                testCase "append combines two lists"
                <| fun () ->
                    let nel1 = NonEmptyList(1, [ 2 ])
                    let nel2 = NonEmptyList(3, [ 4 ])
                    let result = NonEmptyList.append nel1 nel2
                    test <@ NonEmptyList.toList result = [ 1; 2; 3; 4 ] @> ]

          testList
              "properties"
              [ testPropertyWithConfig fsCheckConfig "ofList . toList = id for non-empty"
                <| fun (head: int) (tail: int list) ->
                    let original = NonEmptyList(head, tail)

                    let converted =
                        NonEmptyList.toList original
                        |> NonEmptyList.ofList

                    match converted with
                    | Some nel -> nel = original
                    | None -> false

                testPropertyWithConfig fsCheckConfig "cons maintains non-empty invariant"
                <| fun (x: int) (xs: int list) ->
                    match NonEmptyList.ofList xs with
                    | Some nel ->
                        let result = NonEmptyList.cons x nel
                        NonEmptyList.toList result = x :: xs
                    | None ->
                        let nel = NonEmptyList.singleton x
                        NonEmptyList.toList nel = [ x ]

                testPropertyWithConfig fsCheckConfig "map preserves structure"
                <| fun (head: int) (tail: int list) ->
                    let nel = NonEmptyList(head, tail)
                    let f = (+) 1
                    let mapped = NonEmptyList.map f nel
                    NonEmptyList.toList mapped = List.map f (head :: tail) ] ]
