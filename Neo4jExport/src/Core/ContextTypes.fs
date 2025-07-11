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

namespace Neo4jExport

open System
open Capabilities

/// Context types for parameter reduction in refactoring
/// Groups error tracking with its associated export ID
/// Used throughout the codebase for error correlation
type ErrorContext =
    { ExportId: Guid
      Funcs: ErrorTracking.ErrorTrackingFunctions }

/// Groups export progress tracking state
/// Used by batch processing and export functions
type ProgressContext =
    { Stats: ExportProgress
      LineState: LineTrackingState }

/// Groups all export-related contexts
/// This is the top-level context for export operations
/// Aggregates smaller contexts to reduce function parameters while maintaining modularity
/// Each sub-context can be passed independently when only specific functionality is needed
type ExportContext =
    { Error: ErrorContext
      Progress: ProgressContext
      Config: ExportConfig
      AppContext: ApplicationContext
      Workflow: WorkflowOperations
      Reporting: ProgressOperations option }

/// Context for monitoring operations
/// Groups monitoring-related configuration
type MonitoringContext =
    { AppContext: ApplicationContext
      OutputDirectory: string
      MinDiskGb: int64 }

/// Context for workflow orchestration
/// Groups all workflow-level dependencies
/// Generic type parameter allows use before SafeSession is defined
/// This pattern avoids circular dependencies between modules by deferring the concrete type
type WorkflowContext<'TSession> =
    { App: ApplicationContext
      Export: ExportContext
      Session: 'TSession
      Metadata: FullMetadata
      Neo4jVersion: Neo4jVersion }

/// Result of export execution
/// Contains all data needed for finalization
/// Generic type parameter allows use before LabelStatsTracker is defined
type ExportResult<'TTracker> =
    { Stats: CompletedExportStats
      LabelStats: 'TTracker
      EnhancedMetadata: FullMetadata
      FinalLineState: LineTrackingState
      PaginationPerformance: PaginationPerformance option }

/// Factory functions for ErrorContext
module ErrorContext =
    let create exportId errorFuncs =
        { ExportId = exportId
          Funcs = errorFuncs }
