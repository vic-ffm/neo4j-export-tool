module Neo4jExport.RecordTypes

open Neo4jExport

let standardRecordTypes: RecordTypeDefinition list =
    [ { TypeName = "node"
        Description = "A graph node with labels and properties"
        RequiredFields =
          [ "type"
            "id"
            "export_id"
            "labels"
            "properties" ]
        OptionalFields = None }

      { TypeName = "relationship"
        Description = "A directed relationship between two nodes"
        RequiredFields =
          [ "type"
            "id"
            "export_id"
            "label"
            "start"
            "end"
            "properties" ]
        OptionalFields = None }

      { TypeName = "error"
        Description = "An error that occurred during export"
        RequiredFields = [ "type"; "timestamp"; "message" ]
        OptionalFields =
          Some
              [ "line"
                "details"
                "node_id"
                "relationship_id" ] }

      { TypeName = "warning"
        Description = "A warning that occurred during export"
        RequiredFields = [ "type"; "timestamp"; "message" ]
        OptionalFields =
          Some
              [ "line"
                "details"
                "node_id"
                "relationship_id" ] } ]

let getRecordTypes () = standardRecordTypes
