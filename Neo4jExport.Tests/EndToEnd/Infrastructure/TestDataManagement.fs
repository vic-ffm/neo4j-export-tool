module Neo4jExport.Tests.EndToEnd.Infrastructure.TestDataManagement

open System
open System.Collections.Generic
open Neo4j.Driver
open Neo4jExport
open Neo4jExport.Tests.Helpers.TestLog
open Expecto

// Data scale definitions
type DataScale =
    | Small // 5K nodes, 10K relationships
    | Medium // 500K nodes, 1M relationships
    | Large // 10M nodes, 20M relationships

module DataScale =
    let nodeCount =
        function
        | Small -> 5_000
        | Medium -> 500_000
        | Large -> 10_000_000

    let relationshipCount =
        function
        | Small -> 10_000
        | Medium -> 1_000_000
        | Large -> 20_000_000

// Test data patterns
type TestDataPattern =
    | SimpleGraph // Basic nodes and relationships
    | ComplexTypes // All Neo4j data types
    | DeepPaths // Long paths for path serialization testing
    | HighCardinality // Many labels and relationship types
    | EdgeCases // Edge cases and boundary conditions
    | TruncationTests // Data that triggers truncation behaviors

// Database cleanup operations
module DatabaseCleanup =

    // Clean all data from the database
    let cleanDatabase (driver: IDriver) : Async<Result<unit, exn>> =
        async {
            try
                dataSeeding "Cleaning database" 0
                use session = driver.AsyncSession()

                // Delete all nodes and relationships
                let! cursor =
                    session.RunAsync("MATCH (n) DETACH DELETE n")
                    |> Async.AwaitTask

                let! _ = cursor.ConsumeAsync() |> Async.AwaitTask
                dataSeeding "Database cleaned" 0

                // Clear any indexes or constraints (Neo4j 5.x compatible)
                // Note: We support vanilla Neo4j without APOC
                // Skip schema cleanup for now as it's not critical for tests
                ()

                return Ok()
            with ex ->
                return Error ex
        }

    // Verify database is empty
    let verifyEmpty (driver: IDriver) : Async<bool> =
        async {
            use session = driver.AsyncSession()

            let! cursor =
                session.RunAsync("MATCH (n) RETURN count(n) as count")
                |> Async.AwaitTask

            let! record = cursor.SingleAsync() |> Async.AwaitTask
            let count = record.["count"].As<int64>()
            return count = 0L
        }

// Test data generation
module TestDataGeneration =

    // Batch size for data insertion
    let private batchSize = 1000

    // Generate simple graph data
    let generateSimpleGraph (driver: IDriver) (scale: DataScale) : Async<Result<unit, exn>> =
        async {
            try
                let nodeCount = DataScale.nodeCount scale

                let relCount =
                    DataScale.relationshipCount scale

                dataSeeding $"Generating simple graph" (nodeCount + relCount)

                use session = driver.AsyncSession()

                // Create nodes in batches
                let nodeBatches = nodeCount / batchSize

                for batch in 0..nodeBatches do
                    let startId = batch * batchSize

                    let endId =
                        min ((batch + 1) * batchSize) nodeCount

                    let query =
                        """
                        UNWIND range($startId, $endId - 1) as id
                        CREATE (n:TestNode {
                            id: id,
                            name: 'Node ' + toString(id),
                            created: datetime(),
                            value: toFloat(id) * 1.5
                        })
                    """

                    let parameters =
                        dict
                            [ "startId", box startId
                              "endId", box endId ]

                    let! cursor =
                        session.RunAsync(query, parameters)
                        |> Async.AwaitTask

                    let! _ = cursor.ConsumeAsync() |> Async.AwaitTask
                    ()

                // Create relationships in batches
                let relBatches = relCount / batchSize

                for batch in 0..relBatches do
                    let startId = batch * batchSize

                    let endId =
                        min ((batch + 1) * batchSize) relCount

                    let query =
                        """
                        UNWIND range($startId, $endId - 1) as id
                        MATCH (a:TestNode {id: id % $nodeCount})
                        MATCH (b:TestNode {id: (id + 1) % $nodeCount})
                        CREATE (a)-[:CONNECTED {
                            id: id,
                            weight: toFloat(id) / 100.0,
                            created: datetime()
                        }]->(b)
                    """

                    let parameters =
                        dict
                            [ "startId", box startId
                              "endId", box endId
                              "nodeCount", box nodeCount ]

                    let! cursor =
                        session.RunAsync(query, parameters)
                        |> Async.AwaitTask

                    let! _ = cursor.ConsumeAsync() |> Async.AwaitTask
                    ()

                return Ok()
            with ex ->
                return Error ex
        }

    // Generate complex type test data covering all Neo4j data types from Types.md
    let generateComplexTypes (driver: IDriver) : Async<Result<unit, exn>> =
        async {
            try
                use session = driver.AsyncSession()

                // Create nodes with various data types
                let queries =
                    [
                      // 1. Core Primitive Types
                      """CREATE (n:PrimitiveTypes {
                        nullValue: null,
                        boolTrue: true,
                        boolFalse: false,
                        // Integer variants - all sizes
                        byteVal: 127,                          // int8
                        shortVal: 32767,                       // int16  
                        intVal: 2147483647,                    // int32
                        longVal: 9223372036854775807,          // int64
                        negativeInt: -2147483648,              // negative boundary
                        zeroInt: 0
                    })"""

                      // 2. Float special values (NaN and Infinity stored as properties)
                      """CREATE (n:FloatNode {
                        regularFloat: 3.14159,
                        regularDouble: 2.718281828459045,
                        negativeFloat: -1.23,
                        zeroFloat: 0.0,
                        negativeZero: -0.0,
                        verySmallFloat: 1.0e-307,
                        veryLargeFloat: 1.0e+308,
                        // Special float values as strings (Neo4j doesn't support NaN/Infinity)
                        nanString: 'NaN',
                        infinityString: 'Infinity',
                        negInfinityString: '-Infinity'
                    })"""

                      // 3. String edge cases
                      """CREATE (n:UnicodeTest {
                        emptyString: '',
                        singleChar: 'a',
                        unicodeString: '你好世界 🌍 א ב ג',
                        escapedString: 'Line 1\nLine 2\tTabbed\r\nWindows Line',
                        jsonString: '{"key": "value", "nested": {"array": [1, 2, 3]}}',
                        xmlString: '<root><child attr="value">text</child></root>',
                        sqlString: "'; DROP TABLE users; --",
                        veryLongString: reduce(s = '', i IN range(1, 1000) | s + 'Lorem ipsum '),
                        specialChars: '!@#$%^&*()_+-=[]{}|;\':",./<>?`~',
                        nullChar: 'before\u0000after',
                        surrogatePairs: '𝒜𝒷𝒸𝒹𝓔𝓕',  // Mathematical alphanumeric symbols
                        rtlText: 'مرحبا بالعالم',    // Arabic right-to-left
                        emojiSequence: '👨‍👩‍👧‍👦🏳️‍🌈'     // Complex emoji with joiners
                    })"""

                      // 4. Temporal types with edge cases
                      """CREATE (n:TemporalTypes {
                        // Standard temporal values
                        date: date('2024-01-15'),
                        time: time('14:30:45.123456789+02:00'),
                        localTime: localtime('14:30:45.123456789'),
                        dateTime: datetime('2024-01-15T14:30:45.123456789Z'),
                        dateTimeWithZone: datetime('2024-01-15T14:30:45.123456789+01:00[Europe/Berlin]'),
                        localDateTime: localdatetime('2024-01-15T14:30:45.123456789'),
                        duration: duration('P1Y2M3DT4H5M6.123456789S'),
                        
                        // Edge case temporal values
                        minDate: date('0001-01-01'),
                        maxDate: date('9999-12-31'),
                        leapDay: date('2024-02-29'),
                        midnightTime: time('00:00:00.000000000+00:00'),
                        maxNanoTime: time('23:59:59.999999999+00:00'),
                        epochDateTime: datetime('1970-01-01T00:00:00.000000000Z'),
                        negativeDuration: duration('-P1Y2M3DT4H5M6S'),
                        zeroDuration: duration('PT0S'),
                        complexDuration: duration('P400DT25H61M61S')  // Over limits
                    })"""

                      // 5. Spatial types with different SRIDs
                      """CREATE (n:SpatialTypes {
                        // WGS84 (SRID 4326) - Geographic
                        wgs84_2d: point({longitude: -73.935242, latitude: 40.730610}),
                        wgs84_3d: point({longitude: -73.935242, latitude: 40.730610, height: 100.0}),
                        
                        // Cartesian (SRID 7203) - 2D
                        cartesian2d: point({x: 12.994823, y: 55.612191}),
                        
                        // Cartesian 3D (SRID 9157)
                        cartesian3d: point({x: 12.994823, y: 55.612191, z: 100.0}),
                        
                        // Edge cases
                        origin2d: point({x: 0.0, y: 0.0}),
                        negativeCoords: point({x: -180.0, y: -90.0}),
                        maxCoords: point({longitude: 180.0, latitude: 90.0}),
                        highPrecision: point({x: 12.34567890123456789, y: 98.76543210987654321}),
                        
                        // Spatial arrays (Neo4j supports LIST<POINT>)
                        pointList2d: [point({x: 1, y: 1}), point({x: 2, y: 2}), point({x: 3, y: 3})],
                        pointListGeo: [point({longitude: -73.935, latitude: 40.730}), point({longitude: -74.006, latitude: 40.712})],
                        pointList3d: [point({x: 1, y: 1, z: 1}), point({x: 2, y: 2, z: 2})],
                        emptyPointList: [],
                        singlePointList: [point({x: 0, y: 0})]
                    })"""

                      // 6. Collection types with edge cases
                      """CREATE (n:CollectionTypes {
                        // Basic homogeneous collections
                        emptyList: [],
                        singleItemList: [42],
                        intList: [1, 2, 3, 4, 5],
                        stringList: ['a', 'b', 'c'],
                        boolList: [true, false, true],
                        floatList: [1.1, 2.2, 3.3, 4.4, 5.5],
                        
                        // Large collections (testing truncation)
                        largeList: range(1, 1000),
                        veryLargeList: range(1, 15000),  // Over 10K limit
                        
                        // Byte array
                        byteArray: [x IN range(0, 255) | x % 256],
                        
                        // Special homogeneous collections
                        listOfEmptyStrings: ['', '', '', '', ''],
                        listOfZeros: [0, 0, 0, 0, 0],
                        listOfFalse: [false, false, false, false, false],
                        
                        // Boundary value lists
                        listOfMaxInts: [9223372036854775807, 9223372036854775807],
                        listOfMinInts: [-9223372036854775808, -9223372036854775808],
                        
                        // Unicode string array
                        unicodeList: ['🌍', '🌎', '🌏', '🚀', '⭐'],
                        
                        // Large string array
                        largeStringList: [x IN range(1, 100) | 'String number ' + toString(x)]
                    })"""

                      // 7. Additional test nodes for data types that can't be stored as properties
                      """CREATE (n:SerializationTestNode {
                        // Maps cannot be stored as properties in Neo4j
                        // They will be tested during query result serialization
                        description: 'Node for testing serialization of complex query results',
                        nodeId: 'serialization-test-1',
                        
                        // Store data that will be used to create complex results via queries
                        dataForMapTest: 'This node will be used in queries that return maps',
                        dataForNestedTest: 'This node will be used in queries that return nested structures'
                    })"""

                      // Create a node that will be used to test map returns in queries
                      """CREATE (n:MapDataNode {
                        key1: 'value1',
                        key2: 42,
                        key3: true,
                        key4: 3.14,
                        key5: 'nested',
                        description: 'Properties from this node will be returned as maps in queries'
                    })"""

                      // 8. Node with many labels (testing label limits)
                      """CREATE (n:Label1:Label2:Label3:Label4:Label5:Label6:Label7:Label8:Label9:Label10
                            :Label11:Label12:Label13:Label14:Label15:Label16:Label17:Label18:Label19:Label20 {
                        id: 'many-labels',
                        description: 'Node with 20 labels for testing label truncation'
                    })"""

                      // 9. Node with many properties (testing property limits)
                      """CREATE (n:ManyPropertiesNode {
                        prop1: 1, prop2: 2, prop3: 3, prop4: 4, prop5: 5,
                        prop6: 6, prop7: 7, prop8: 8, prop9: 9, prop10: 10,
                        prop11: 'a', prop12: 'b', prop13: 'c', prop14: 'd', prop15: 'e',
                        prop16: true, prop17: false, prop18: null, prop19: 3.14, prop20: 2.71,
                        description: 'Node with many properties for testing limits'
                    })"""

                      // 10. ByteArray edge cases
                      """CREATE (n:ByteArrayNode {
                        emptyBytes: [],
                        singleByte: [255],
                        allBytes: range(0, 255),
                        largeBinary: [x IN range(1, 10000) | x % 256],  // 10KB
                        patternBytes: [0, 255, 0, 255, 0, 255],
                        nullBytes: [0, 0, 0, 0]
                    })"""

                      // 11. Graph elements as properties (testing nested graph elements)
                      // Note: This creates relationships that contain paths/nodes as properties
                      """MATCH (n1:PrimitiveNode), (n2:FloatNode)
                       CREATE (n1)-[r:CONTAINS_PATH {
                           description: 'Relationship with path property',
                           created: datetime()
                       }]->(n2)"""

                      // 12. Very long strings (testing 10MB truncation)
                      """CREATE (n:LongStringNode {
                        id: 'long-strings',
                        mediumString: reduce(s = '', i IN range(1, 10000) | s + 'x'),       // 10K chars
                        largeString: reduce(s = '', i IN range(1, 100000) | s + 'y'),      // 100K chars
                        hugeString: reduce(s = '', i IN range(1, 1000000) | s + 'z')       // 1M chars
                    })""" ]

                for query in queries do
                    let! cursor = session.RunAsync(query) |> Async.AwaitTask
                    let! _ = cursor.ConsumeAsync() |> Async.AwaitTask
                    ()

                return Ok()
            with ex ->
                return Error ex
        }

    // Generate deep path structures
    let generateDeepPaths (driver: IDriver) (depth: int) : Async<Result<unit, exn>> =
        async {
            try
                use session = driver.AsyncSession()

                // Create a linear chain of nodes
                let linearQuery =
                    """
                    CREATE (start:PathStart {id: 0, type: 'linear'})
                    WITH start
                    UNWIND range(1, $depth) as i
                    CREATE (n:PathNode {id: i, description: 'Node ' + toString(i)})
                    WITH collect(n) as nodes, start
                    UNWIND range(0, size(nodes)-2) as i
                    WITH nodes[i] as n1, nodes[i+1] as n2, i
                    CREATE (n1)-[:NEXT {step: i+1, weight: toFloat(i+1) * 0.1}]->(n2)
                    
                    WITH 1 as dummy
                    MATCH (start:PathStart {type: 'linear'})
                    MATCH (end:PathNode {id: $depth})
                    CREATE (start)-[:CONNECTS_TO {distance: $depth}]->(end)
                """

                let parameters = dict [ "depth", box depth ]

                let! cursor =
                    session.RunAsync(linearQuery, parameters)
                    |> Async.AwaitTask

                let! _ = cursor.ConsumeAsync() |> Async.AwaitTask

                // Create a branching path structure
                let branchingQuery =
                    """
                    CREATE (root:PathRoot {id: 'root', type: 'branching'})
                    WITH root
                    UNWIND range(1, 3) as branch
                    CREATE (b:BranchNode {id: 'branch-' + toString(branch), level: 1})
                    CREATE (root)-[:BRANCH {number: branch}]->(b)
                    WITH b
                    UNWIND range(1, 3) as leaf
                    CREATE (l:LeafNode {id: b.id + '-leaf-' + toString(leaf), level: 2})
                    CREATE (b)-[:LEAF {number: leaf}]->(l)
                """

                let! cursor =
                    session.RunAsync(branchingQuery)
                    |> Async.AwaitTask

                let! _ = cursor.ConsumeAsync() |> Async.AwaitTask

                // Create circular path
                let circularQuery =
                    """
                    CREATE (n1:CircularNode {id: 1, name: 'First'}),
                           (n2:CircularNode {id: 2, name: 'Second'}),
                           (n3:CircularNode {id: 3, name: 'Third'}),
                           (n4:CircularNode {id: 4, name: 'Fourth'}),
                           (n1)-[:CIRCULAR_NEXT {order: 1}]->(n2),
                           (n2)-[:CIRCULAR_NEXT {order: 2}]->(n3),
                           (n3)-[:CIRCULAR_NEXT {order: 3}]->(n4),
                           (n4)-[:CIRCULAR_NEXT {order: 4}]->(n1)
                """

                let! cursor = session.RunAsync(circularQuery) |> Async.AwaitTask
                let! _ = cursor.ConsumeAsync() |> Async.AwaitTask

                return Ok()
            with ex ->
                return Error ex
        }

    // Generate edge case test data
    let generateEdgeCases (driver: IDriver) : Async<Result<unit, exn>> =
        async {
            try
                use session = driver.AsyncSession()

                let queries =
                    [
                      // Nodes with properties at Neo4j limits
                      """CREATE (n:BoundaryNode {
                        maxInt: 9223372036854775807,
                        minInt: -9223372036854775808,
                        maxFloat: 1.7976931348623157e+308,
                        minFloat: -1.7976931348623157e+308,
                        epsilon: 2.2250738585072014e-308,
                        // Property names with special characters
                        `prop.with.dots`: 'dotted',
                        `prop-with-dashes`: 'dashed',
                        `prop_with_underscores`: 'underscored',
                        `prop with spaces`: 'spaced',
                        `123numeric`: 'numeric start',
                        `кириллица`: 'cyrillic',
                        `中文属性`: 'chinese property'
                    })"""

                      // Relationships with extreme property counts
                      """CREATE (n1:ExtremePropNode1 {id: 1}),
                              (n2:ExtremePropNode2 {id: 2}),
                              (n1)-[r:EXTREME_REL {
                           prop1: 1, prop2: 2, prop3: 3, prop4: 4, prop5: 5,
                           prop6: 6, prop7: 7, prop8: 8, prop9: 9, prop10: 10,
                           prop11: 11, prop12: 12, prop13: 13, prop14: 14, prop15: 15,
                           prop16: 16, prop17: 17, prop18: 18, prop19: 19, prop20: 20,
                           prop21: 21, prop22: 22, prop23: 23, prop24: 24, prop25: 25,
                           prop26: 26, prop27: 27, prop28: 28, prop29: 29, prop30: 30,
                           prop31: 31, prop32: 32, prop33: 33, prop34: 34, prop35: 35,
                           prop36: 36, prop37: 37, prop38: 38, prop39: 39, prop40: 40,
                           prop41: 41, prop42: 42, prop43: 43, prop44: 44, prop45: 45,
                           prop46: 46, prop47: 47, prop48: 48, prop49: 49, prop50: 50
                       }]->(n2)"""

                      // Self-referencing relationships
                      """CREATE (n:SelfRefNode {id: 'self', type: 'recursive'})
                       WITH n
                       CREATE (n)-[:SELF_REF {reason: 'testing'}]->(n),
                              (n)-[:ALSO_SELF {level: 1}]->(n),
                              (n)-[:TRIPLE_SELF {count: 3}]->(n)"""

                      // Multiple relationships between same nodes
                      """CREATE (n1:MultiRelNode1 {id: 'multi1'}),
                              (n2:MultiRelNode2 {id: 'multi2'}),
                              (n1)-[:REL_TYPE_A {order: 1}]->(n2),
                              (n1)-[:REL_TYPE_B {order: 2}]->(n2),
                              (n1)-[:REL_TYPE_C {order: 3}]->(n2),
                              (n2)-[:REL_TYPE_D {order: 4}]->(n1),
                              (n2)-[:REL_TYPE_E {order: 5}]->(n1)"""

                      // Empty nodes and relationships
                      """CREATE (n1:EmptyNode),
                              (n2:EmptyNode),
                              (n1)-[:EMPTY_REL]->(n2)"""

                      // Nodes with single property of each type
                      """CREATE (n:SinglePropNull {value: null})"""
                      """CREATE (n:SinglePropBool {value: true})"""
                      """CREATE (n:SinglePropInt {value: 42})"""
                      """CREATE (n:SinglePropFloat {value: 3.14})"""
                      """CREATE (n:SinglePropString {value: 'test'})"""
                      """CREATE (n:SinglePropList {value: [1, 2, 3]})"""
                      // Maps are not allowed as property values in Neo4j
                      """CREATE (n:SinglePropMapSimulated {valueType: 'map', mapKey: 'key', mapValue: 'value'})"""
                      """CREATE (n:SinglePropDate {value: date()})"""
                      """CREATE (n:SinglePropTime {value: time()})"""
                      """CREATE (n:SinglePropDateTime {value: datetime()})"""
                      """CREATE (n:SinglePropDuration {value: duration('P1D')})"""
                      """CREATE (n:SinglePropPoint {value: point({x: 0, y: 0})})""" ]

                for query in queries do
                    let! cursor = session.RunAsync(query) |> Async.AwaitTask
                    let! _ = cursor.ConsumeAsync() |> Async.AwaitTask
                    ()

                return Ok()
            with ex ->
                return Error ex
        }

    // Generate data that tests truncation behaviors
    let generateTruncationTests (driver: IDriver) : Async<Result<unit, exn>> =
        async {
            try
                use session = driver.AsyncSession()

                let queries =
                    [
                      // Create node with exactly 10 labels (at default limit)
                      """CREATE (n:L1:L2:L3:L4:L5:L6:L7:L8:L9:L10 {
                        id: 'exactly-10-labels',
                        labelCount: 10
                    })"""

                      // Create node with 11 labels (just over default limit)
                      """CREATE (n:L1:L2:L3:L4:L5:L6:L7:L8:L9:L10:L11 {
                        id: 'eleven-labels',
                        labelCount: 11
                    })"""

                      // Create collection at truncation boundary (10,000 items)
                      """CREATE (n:TruncationBoundary {
                        exactLimit: range(1, 10000),
                        overLimit: range(1, 10001),
                        wayOverLimit: range(1, 50000)
                    })"""

                      // Neo4j doesn't support nested collections as properties
                      // Create node with max depth properties instead
                      """CREATE (n:DepthTest {
                        simpleList: ['a', 'b', 'c'],
                        depth: 11,
                        description: 'Would have deeply nested structure in export'
                    })"""

                      // Create string exactly at 10MB limit
                      """CREATE (n:StringLimitTest {
                        // 10,000,000 character string (10MB)
                        exactLimit: reduce(s = '', i IN range(1, 1000000) | s + '0123456789'),
                        // 10,000,001 character string (just over)
                        overLimit: reduce(s = '', i IN range(1, 1000000) | s + '0123456789') + 'X'
                    })"""

                      // Create byte array at 50MB limit
                      """CREATE (n:ByteArrayLimitTest {
                        // Create 50MB array (at limit)
                        atLimit: [x IN range(1, 50000000) | x % 256],
                        // Create 50MB + 1 byte array
                        overLimit: [x IN range(1, 50000001) | x % 256]
                    })"""

                      // Path length truncation tests
                      """
                    // Create path at compact mode threshold (10 nodes)
                    CREATE p1 = (:PathTest {id: 'start1'})-[:STEP]->(:PathTest)-[:STEP]->(:PathTest)
                               -[:STEP]->(:PathTest)-[:STEP]->(:PathTest)-[:STEP]->(:PathTest)
                               -[:STEP]->(:PathTest)-[:STEP]->(:PathTest)-[:STEP]->(:PathTest)
                               -[:STEP]->(:PathTest {id: 'end1'})
                    
                    // Create path at IDs-only threshold (100 nodes)
                    WITH 1 as dummy
                    CREATE (start:PathTest {id: 'start100'})
                    WITH start
                    CREATE (prev:PathTest {id: 'node1'})
                    CREATE (start)-[:STEP]->(prev)
                    WITH prev
                    UNWIND range(2, 99) as i
                    CREATE (n:PathTest {id: 'node' + toString(i)})
                    CREATE (prev)-[:STEP]->(n)
                    WITH n as prev
                    CREATE (end:PathTest {id: 'end100'})
                    CREATE (prev)-[:STEP]->(end)
                    """ ]

                for query in queries do
                    let! cursor = session.RunAsync(query) |> Async.AwaitTask
                    let! _ = cursor.ConsumeAsync() |> Async.AwaitTask
                    ()

                return Ok()
            with ex ->
                return Error ex
        }

    // Generate high cardinality test data
    let generateHighCardinality (driver: IDriver) : Async<Result<unit, exn>> =
        async {
            try
                use session = driver.AsyncSession()

                // Create nodes with many different labels
                let! cursor =
                    session.RunAsync(
                        """
                        UNWIND range(1, 100) as i
                        CREATE (n:HighCard {id: i})
                        WITH n, i
                        CALL apoc.create.addLabels(n, ['Label' + toString(i), 'Type' + toString(i % 10), 'Category' + toString(i % 5)]) YIELD node
                        RETURN count(node)
                    """
                    )
                    |> Async.AwaitTask
                    |> Async.Catch

                match cursor with
                | Choice1Of2 result ->
                    let! _ = result.ConsumeAsync() |> Async.AwaitTask
                    ()
                | Choice2Of2 _ ->
                    // APOC not available, use vanilla approach
                    for i in 1..20 do
                        let label = $"DynamicLabel{i}"

                        let query =
                            $"CREATE (n:{label} {{id: {i}, category: 'high-cardinality'}})"

                        let! cursor = session.RunAsync(query) |> Async.AwaitTask
                        let! _ = cursor.ConsumeAsync() |> Async.AwaitTask
                        ()

                // Create many relationship types
                let! cursor =
                    session.RunAsync(
                        """
                        CREATE (hub:Hub {id: 'central'})
                        WITH hub
                        UNWIND range(1, 50) as i
                        CREATE (n:Spoke {id: i})
                        WITH hub, n, i
                        CALL apoc.create.relationship(hub, 'REL_TYPE_' + toString(i), {order: i}, n) YIELD rel
                        RETURN count(rel)
                    """
                    )
                    |> Async.AwaitTask
                    |> Async.Catch

                match cursor with
                | Choice1Of2 result ->
                    let! _ = result.ConsumeAsync() |> Async.AwaitTask
                    ()
                | Choice2Of2 _ ->
                    // APOC not available, create hub first then create fewer relationship types
                    let! cursor =
                        session.RunAsync("CREATE (hub:Hub {id: 'central'})")
                        |> Async.AwaitTask

                    let! _ = cursor.ConsumeAsync() |> Async.AwaitTask

                    for i in 1..10 do
                        let relType =
                            match i with
                            | 1 -> "KNOWS"
                            | 2 -> "LIKES"
                            | 3 -> "FOLLOWS"
                            | 4 -> "WORKS_WITH"
                            | 5 -> "MANAGES"
                            | 6 -> "REPORTS_TO"
                            | 7 -> "COLLABORATES"
                            | 8 -> "REVIEWS"
                            | 9 -> "APPROVES"
                            | _ -> "CONNECTED_TO"

                        let query =
                            $"""
                            MATCH (hub:Hub {{id: 'central'}})
                            CREATE (spoke:Spoke {{id: {i}}})
                            CREATE (hub)-[:{relType} {{order: {i}}}]->(spoke)
                        """

                        let! cursor = session.RunAsync(query) |> Async.AwaitTask
                        let! _ = cursor.ConsumeAsync() |> Async.AwaitTask
                        ()

                return Ok()
            with ex ->
                return Error ex
        }

// Test execution helpers
module TestExecution =

    // Run a test with clean database
    let withCleanDatabase (driver: IDriver) (test: unit -> Async<unit>) : Async<unit> =
        async {
            // Clean before test
            match! DatabaseCleanup.cleanDatabase driver with
            | Error ex -> failtest $"Failed to clean database: {ex.Message}"
            | Ok() -> ()

            // Verify empty
            let! isEmpty = DatabaseCleanup.verifyEmpty driver

            if not isEmpty then
                failtest "Database is not empty after cleanup"

            // Run test
            do! test ()
        }

    // Run a test with seeded data
    let withSeededData
        (driver: IDriver)
        (pattern: TestDataPattern)
        (scale: DataScale)
        (test: unit -> Async<unit>)
        : Async<unit> =
        async {
            // Clean and seed
            do!
                withCleanDatabase driver (fun () ->
                    async {
                        match pattern with
                        | SimpleGraph ->
                            match! TestDataGeneration.generateSimpleGraph driver scale with
                            | Error ex -> failtest $"Failed to seed simple graph: {ex.Message}"
                            | Ok() -> ()

                        | ComplexTypes ->
                            match! TestDataGeneration.generateComplexTypes driver with
                            | Error ex -> failtest $"Failed to seed complex types: {ex.Message}"
                            | Ok() -> ()

                        | DeepPaths ->
                            match! TestDataGeneration.generateDeepPaths driver 100 with
                            | Error ex -> failtest $"Failed to seed deep paths: {ex.Message}"
                            | Ok() -> ()

                        | HighCardinality ->
                            match! TestDataGeneration.generateHighCardinality driver with
                            | Error ex -> failtest $"Failed to seed high cardinality: {ex.Message}"
                            | Ok() -> ()

                        | EdgeCases ->
                            match! TestDataGeneration.generateEdgeCases driver with
                            | Error ex -> failtest $"Failed to seed edge cases: {ex.Message}"
                            | Ok() -> ()

                        | TruncationTests ->
                            match! TestDataGeneration.generateTruncationTests driver with
                            | Error ex -> failtest $"Failed to seed truncation tests: {ex.Message}"
                            | Ok() -> ()

                        // Run the actual test
                        do! test ()
                    })
        }
