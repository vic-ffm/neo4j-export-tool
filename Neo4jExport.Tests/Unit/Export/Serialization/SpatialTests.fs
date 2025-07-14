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

module Neo4jExport.Tests.Unit.Export.Serialization.SpatialTests

open System
open Expecto
open Swensen.Unquote
open Neo4jExport
open Neo4jExport.SerializationSpatial
open Neo4jExport.Tests.Helpers.TestHelpers
open Neo4j.Driver

[<Tests>]
let tests =
    testList
        "Serialization - Spatial"
        [

          testList
              "Point2D serialization"
              [ testCase "serializes 2D point with WGS84"
                <| fun () ->
                    let point = Point(4326, -73.9857, 40.7484)

                    let json =
                        serializeToJson (fun writer -> serializePoint writer point)

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__type_str =
                            obj.GetProperty("type").GetString()

                        test <@ obj__type_str = "Point" @>

                        let obj__srid_int =
                            obj.GetProperty("srid").GetInt32()

                        test <@ obj__srid_int = 4326 @>

                        let obj__x_dbl =
                            obj.GetProperty("x").GetDouble()

                        test <@ obj__x_dbl = -73.9857 @>

                        let obj__y_dbl =
                            obj.GetProperty("y").GetDouble()

                        test <@ obj__y_dbl = 40.7484 @>
                    | Error msg -> failtest msg

                testCase "serializes 2D point with Cartesian"
                <| fun () ->
                    let point = Point(7203, 100.5, 200.75)

                    let json =
                        serializeToJson (fun writer -> serializePoint writer point)

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__srid_int =
                            obj.GetProperty("srid").GetInt32()

                        test <@ obj__srid_int = 7203 @>

                        let obj__x_dbl =
                            obj.GetProperty("x").GetDouble()

                        test <@ obj__x_dbl = 100.5 @>

                        let obj__y_dbl =
                            obj.GetProperty("y").GetDouble()

                        test <@ obj__y_dbl = 200.75 @>
                    | Error msg -> failtest msg

                testCase "handles coordinate precision"
                <| fun () ->
                    let point =
                        Point(4326, 1.123456789012345, -2.987654321098765)

                    let json =
                        serializeToJson (fun writer -> serializePoint writer point)

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc
                        // Verify precision is maintained
                        let x = obj.GetProperty("x").GetDouble()
                        let y = obj.GetProperty("y").GetDouble()
                        test <@ Math.Abs(x - 1.123456789012345) < 1e-10 @>
                        test <@ Math.Abs(y - (-2.987654321098765)) < 1e-10 @>
                    | Error msg -> failtest msg

                testCase "serializes edge coordinates"
                <| fun () ->
                    let points =
                        [ Point(4326, -180.0, -90.0) // Min bounds
                          Point(4326, 180.0, 90.0) // Max bounds
                          Point(4326, 0.0, 0.0) ] // Origin

                    points
                    |> List.iter (fun point ->
                        let json =
                            serializeToJson (fun writer -> serializePoint writer point)

                        match validateJson json with
                        | Ok _ -> ()
                        | Error msg -> failtest $"Failed to serialize point {point}: {msg}") ]

          testList
              "Point3D serialization"
              [ testCase "serializes 3D point with WGS84-3D"
                <| fun () ->
                    let point =
                        Point(4979, -73.9857, 40.7484, 10.5)

                    let json =
                        serializeToJson (fun writer -> serializePoint writer point)

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__type_str =
                            obj.GetProperty("type").GetString()

                        test <@ obj__type_str = "Point" @>

                        let obj__srid_int =
                            obj.GetProperty("srid").GetInt32()

                        test <@ obj__srid_int = 4979 @>

                        let obj__x_dbl =
                            obj.GetProperty("x").GetDouble()

                        test <@ obj__x_dbl = -73.9857 @>

                        let obj__y_dbl =
                            obj.GetProperty("y").GetDouble()

                        test <@ obj__y_dbl = 40.7484 @>

                        let obj__z_dbl =
                            obj.GetProperty("z").GetDouble()

                        test <@ obj__z_dbl = 10.5 @>
                    | Error msg -> failtest msg

                testCase "serializes 3D point with Cartesian-3D"
                <| fun () ->
                    let point = Point(9157, 100.0, 200.0, 300.0)

                    let json =
                        serializeToJson (fun writer -> serializePoint writer point)

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__srid_int =
                            obj.GetProperty("srid").GetInt32()

                        test <@ obj__srid_int = 9157 @>

                        let obj__x_dbl =
                            obj.GetProperty("x").GetDouble()

                        test <@ obj__x_dbl = 100.0 @>

                        let obj__y_dbl =
                            obj.GetProperty("y").GetDouble()

                        test <@ obj__y_dbl = 200.0 @>

                        let obj__z_dbl =
                            obj.GetProperty("z").GetDouble()

                        test <@ obj__z_dbl = 300.0 @>
                    | Error msg -> failtest msg

                testCase "handles negative Z coordinate"
                <| fun () ->
                    let point = Point(4979, 0.0, 0.0, -1000.0)

                    let json =
                        serializeToJson (fun writer -> serializePoint writer point)

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc

                        let obj__z_dbl =
                            obj.GetProperty("z").GetDouble()

                        test <@ obj__z_dbl = -1000.0 @>
                    | Error msg -> failtest msg

                testCase "maintains Z precision"
                <| fun () ->
                    let point =
                        Point(4979, 1.1, 2.2, 3.333333333333)

                    let json =
                        serializeToJson (fun writer -> serializePoint writer point)

                    match validateJson json with
                    | Ok doc ->
                        let obj = getJsonValue doc
                        let z = obj.GetProperty("z").GetDouble()
                        test <@ Math.Abs(z - 3.333333333333) < 1e-10 @>
                    | Error msg -> failtest msg ] ]
