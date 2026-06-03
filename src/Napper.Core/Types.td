# Napper.Core domain models — CANONICAL declarations (typeDiagram DSL).
#
# This .td file is the single source of truth for the DTOs in Types.fs.
# Per CLAUDE.md "Type Models": all models are declared in typeDiagram markup and
# the F# ADTs are produced by the typeDiagram code generator.
#
# NOTE: typeDiagram 0.8.0 does not yet emit F# (--to supports
# typescript|python|rust|go|csharp only). Until F# emit lands, Types.fs is kept
# hand-synced to this file. Tracking: https://github.com/Nimblesite/typeDiagram/issues/36
#
# `Duration` is an opaque host type (maps to System.TimeSpan in F#); typeDiagram
# renders undeclared types as inline text, same as UUID in the language reference.

# Assertion operators used in [assert] blocks
union AssertOp {
  Equals(String)
  Exists
  Contains(String)
  Matches(String)
  LessThan(String)
  GreaterThan(String)
}

# A single assertion line, e.g. status = 200, body.id exists
type Assertion {
  Target: String
  Op:     AssertOp
}

# HTTP method
union HttpMethod {
  GET
  POST
  PUT
  PATCH
  DELETE
  HEAD
  OPTIONS
}

# Script references (pre/post hooks)
type ScriptRef {
  Pre:  Option<String>
  Post: Option<String>
}

# Metadata block [meta]
type NapMeta {
  Name:        Option<String>
  Description: Option<String>
  Tags:        List<String>
}

# Request body
type RequestBody {
  ContentType: String
  Content:     String
}

# The request definition from a .nap file
type NapRequest {
  Method:  HttpMethod
  Url:     String
  Headers: Map<String, String>
  Body:    Option<RequestBody>
}

# A fully parsed .nap file
type NapFile {
  Meta:       NapMeta
  Vars:       Map<String, String>
  Request:    NapRequest
  Assertions: List<Assertion>
  Script:     ScriptRef
}

# Result of evaluating a single assertion
type AssertionResult {
  Assertion: Assertion
  Passed:    Bool
  Expected:  String
  Actual:    String
}

# The HTTP response captured after running a request
type NapResponse {
  StatusCode: Int
  Headers:    Map<String, String>
  Body:       String
  Duration:   Duration
}

# Overall result of running a single .nap file
type NapResult {
  File:       String
  Request:    NapRequest
  Response:   Option<NapResponse>
  Assertions: List<AssertionResult>
  Passed:     Bool
  Error:      Option<String>
  Log:        List<String>
}

# A step in a .naplist playlist
union PlaylistStep {
  NapFileStep(String)
  PlaylistRef(String)
  FolderRef(String)
  ScriptStep(String)
}

# A parsed .naplist file
type NapPlaylist {
  Meta:  NapMeta
  Env:   Option<String>
  Vars:  Map<String, String>
  Steps: List<PlaylistStep>
}
