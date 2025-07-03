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

module Neo4jExport.RecordTypes

open Neo4jExport

let standardRecordTypes: RecordTypeDefinition list =
    [ { TypeName = "node"
        Description = "A graph node with labels and properties"
        RequiredFields =
          [ "type"
            "element_id"
            "export_id"
            "labels"
            "properties" ]
        OptionalFields = None }

      { TypeName = "relationship"
        Description = "A directed relationship between two nodes"
        RequiredFields =
          [ "type"
            "element_id"
            "export_id"
            "label"
            "start_element_id"
            "end_element_id"
            "properties" ]
        OptionalFields = None }

      { TypeName = "error"
        Description = "An error that occurred during export"
        RequiredFields = [ "type"; "timestamp"; "message" ]
        OptionalFields = Some [ "line"; "details"; "element_id" ] }

      { TypeName = "warning"
        Description = "A warning that occurred during export"
        RequiredFields = [ "type"; "timestamp"; "message" ]
        OptionalFields = Some [ "line"; "details"; "element_id" ] } ]

let getRecordTypes () = standardRecordTypes
