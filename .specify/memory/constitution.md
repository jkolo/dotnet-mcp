<!--
SYNC IMPACT REPORT
==================
Version change: 0.0.0 → 1.0.0
Bump rationale: Initial constitution establishment (MAJOR)

Modified principles: N/A (new document)

Added sections:
- Core Principles (5 principles)
  - I. Native First
  - II. MCP Compliance
  - III. Test-First
  - IV. Simplicity
  - V. Observability
- MCP Tool Standards
- Governance

Removed sections: N/A (new document)

Templates requiring updates:
- .specify/templates/plan-template.md: ✅ Constitution Check section compatible
- .specify/templates/spec-template.md: ✅ Requirements align with principles
- .specify/templates/tasks-template.md: ✅ Test-first workflow compatible

Follow-up TODOs: None
-->

# DebugMcp Constitution

## Core Principles

### I. Native First

DebugMcp interfaces directly with the .NET runtime using ICorDebug APIs. This is
a non-negotiable architectural decision.

- All debugging operations MUST use ICorDebug APIs directly
- External debuggers (e.g., via DAP protocol) MUST NOT be used as the primary
  debugging mechanism
- Platform-specific native interop is acceptable when required by ICorDebug
- Fallback to external tools is only permitted when ICorDebug lacks capability
  (document the gap and track for future resolution)

**Rationale**: Direct runtime access provides superior performance, accuracy, and
control compared to protocol-based debugger abstractions. This differentiates
DebugMcp from DAP-based alternatives.

### II. MCP Compliance

All exposed functionality MUST conform to the Model Context Protocol specification.

- Tools MUST follow MCP naming conventions: `noun_verb` format (e.g., `debug_attach`,
  `breakpoint_set`)
- Tool parameters MUST use JSON Schema with clear descriptions
- Responses MUST be structured JSON suitable for AI consumption
- Errors MUST return structured error objects with actionable messages
- Tools MUST be idempotent where semantically appropriate
- Long-running operations MUST support progress reporting or timeout mechanisms

**Rationale**: MCP compliance ensures interoperability with AI assistants and
provides a consistent interface for tool consumers.

### III. Test-First (NON-NEGOTIABLE)

Test-Driven Development is mandatory for all feature implementation.

- Tests MUST be written before implementation code
- Tests MUST fail before implementation (Red phase)
- Implementation MUST make tests pass with minimal code (Green phase)
- Refactoring occurs only after tests pass (Refactor phase)
- Integration tests MUST cover: debugger attach/detach, breakpoint lifecycle,
  stepping operations, variable inspection
- Contract tests MUST verify MCP tool schemas match documentation

**Rationale**: TDD ensures correctness in a domain where debugging bugs in the
debugger itself is exceptionally difficult. Tests serve as living documentation
of expected behavior.

### IV. Simplicity

Complexity must be justified. Start simple, stay simple.

- YAGNI: Do not implement features until they are needed
- Prefer direct solutions over abstractions
- Maximum 3 levels of indirection for any operation path
- No premature optimization; profile before optimizing
- Delete dead code immediately; do not comment it out
- Each tool SHOULD do one thing well

**Rationale**: A debugging tool must be predictable and easy to reason about.
Unnecessary complexity introduces bugs and makes maintenance difficult.

### V. Observability

All operations must be traceable for debugging and support purposes.

- Structured logging MUST be used (not Console.WriteLine)
- Log levels: Debug (verbose), Info (operations), Warning (recoverable), Error
  (failures)
- Every tool invocation MUST log: tool name, parameters (sanitized), duration,
  outcome
- Debug session state changes MUST be logged
- Performance-sensitive paths SHOULD include timing metrics

**Rationale**: When users report issues, logs must provide sufficient context to
diagnose problems without requiring reproduction.

## MCP Tool Standards

Guidelines for designing and implementing MCP tools in DebugMcp.

### Naming

- Use `noun_verb` format: `debug_attach`, `breakpoint_set`, `variables_get`
- Nouns: `debug`, `breakpoint`, `thread`, `stacktrace`, `variables`, `evaluate`
- Verbs: `attach`, `launch`, `set`, `remove`, `list`, `get`, `continue`, `pause`,
  `step_over`, `step_into`, `step_out`, `wait`, `disconnect`

### Parameters

- Required parameters: document clearly, fail fast with helpful error
- Optional parameters: provide sensible defaults, document default behavior
- Timeouts: all blocking operations MUST accept optional timeout (default: 30s)
- Identifiers: use consistent types (PID as int, thread ID as int, breakpoint ID
  as string)

### Responses

- Success: return structured data with consistent field naming
- Partial success: return data with warnings array
- Failure: return error object with `code`, `message`, and optional `details`
- State: tools that modify state SHOULD return the new state

### Documentation

- Each tool MUST have JSDoc-style description in code
- Parameter descriptions MUST explain valid values and constraints
- Example invocations MUST be provided in MCP_TOOLS.md

## Governance

This constitution is the authoritative source for project standards. All code,
PRs, and architectural decisions must comply.

### Amendment Process

1. Propose amendment via GitHub Issue with `constitution` label
2. Document rationale and impact analysis
3. Update constitution.md with changes
4. Increment version according to semantic versioning:
   - MAJOR: Backward-incompatible principle changes or removals
   - MINOR: New principles or material guidance expansion
   - PATCH: Clarifications, wording improvements, typo fixes
5. Update dependent templates if affected
6. Merge after review approval

### Compliance

- All PRs MUST pass Constitution Check before merge
- Violations require documented justification in PR description
- Repeated violations indicate need for constitution amendment (not exceptions)

**Version**: 1.0.0 | **Ratified**: 2026-01-17 | **Last Amended**: 2026-01-17
