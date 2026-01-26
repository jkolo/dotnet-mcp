# Feature Specification: Module and Assembly Inspection

**Feature Branch**: `005-module-ops`
**Created**: 2026-01-25
**Status**: Draft
**Input**: User description: "Module and assembly inspection - list loaded modules, explore types and methods, browse namespaces for code navigation during debugging"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - List Loaded Modules (Priority: P1) MVP

AI assistants can list all loaded modules (assemblies/DLLs) in the debugged process. This provides an overview of what code is loaded, including version information and file paths, enabling the assistant to understand the application's composition.

**Why this priority**: Module listing is the entry point for all code navigation. Without knowing what modules are loaded, the assistant cannot explore types or set meaningful breakpoints. This is foundational for any code exploration task.

**Independent Test**: Attach to a running process, invoke module list tool. Tool returns all loaded assemblies with names, versions, and paths. Works whether process is running or paused.

**Acceptance Scenarios**:

1. **Given** an active debug session (running or paused), **When** AI requests module list, **Then** system returns all loaded assemblies with name, version, and file path
2. **Given** a process with dynamically loaded assemblies, **When** AI requests module list after new assembly loads, **Then** system includes the newly loaded assembly
3. **Given** a process with native and managed modules, **When** AI requests module list, **Then** system distinguishes between managed (.NET) and native modules
4. **Given** a module list request, **When** system responds, **Then** results include whether symbols (PDB) are loaded for each module

---

### User Story 2 - Browse Types in Module (Priority: P2)

AI assistants can explore types defined in a specific module, organized by namespace. This enables understanding what classes, interfaces, enums, and structs are available in an assembly for debugging or setting breakpoints.

**Why this priority**: Type browsing is the next logical step after identifying modules. Understanding available types is essential for setting function breakpoints, understanding application structure, and navigating to relevant code.

**Independent Test**: Attach to process, list modules, select a module, invoke type browser. Tool returns types organized by namespace with basic type information (kind, visibility).

**Acceptance Scenarios**:

1. **Given** a loaded module name, **When** AI requests types in module, **Then** system returns all defined types grouped by namespace
2. **Given** a namespace filter, **When** AI requests types with filter, **Then** system returns only types in matching namespaces
3. **Given** a module with many types, **When** AI requests types, **Then** system supports pagination or limits results with continuation
4. **Given** type results, **When** system responds, **Then** each type includes kind (class, interface, struct, enum), visibility (public, internal), and whether it's generic

---

### User Story 3 - Inspect Type Members (Priority: P3)

AI assistants can inspect the members of a specific type - methods, properties, fields, and events. This enables understanding what functionality a type provides and helps identify methods for breakpoints or investigation.

**Why this priority**: Member inspection builds on type browsing to provide detailed understanding of type structure. This is essential for setting method breakpoints and understanding available functionality.

**Independent Test**: Attach to process, identify a type by full name, invoke member inspection. Tool returns all members with signatures, visibility, and whether they're static or instance.

**Acceptance Scenarios**:

1. **Given** a full type name, **When** AI requests type members, **Then** system returns all methods, properties, fields, and events
2. **Given** method results, **When** system responds, **Then** each method includes name, return type, parameters, visibility, and static/instance indicator
3. **Given** a type with inherited members, **When** AI requests members with inheritance flag, **Then** system includes base class members
4. **Given** a generic type, **When** AI requests members, **Then** system shows generic parameters and constraints

---

### User Story 4 - Search Across Modules (Priority: P4)

AI assistants can search for types or methods across all loaded modules by name pattern. This enables quickly finding relevant code without manually browsing each module.

**Why this priority**: Search is a convenience feature that accelerates debugging workflows but isn't essential when manual browsing is available. Useful for large applications with many modules.

**Independent Test**: Attach to process with multiple modules, invoke search with pattern (e.g., "*Controller", "Get*"). Tool returns matching types/methods across all modules.

**Acceptance Scenarios**:

1. **Given** a type name pattern, **When** AI searches for types, **Then** system returns matching types from all loaded modules
2. **Given** a method name pattern, **When** AI searches for methods, **Then** system returns matching methods with their declaring types
3. **Given** a search with many results, **When** system responds, **Then** results are limited with count indicator and support filtering by module
4. **Given** case-insensitive search option, **When** AI searches, **Then** system matches regardless of case

---

### Edge Cases

- What happens when querying a module that was unloaded? Return clear error indicating module is no longer available.
- How does system handle assemblies loaded from memory (no file path)? Indicate in-memory module with appropriate marker.
- What happens with obfuscated assemblies? Return available metadata; names may be obfuscated but structure is preserved.
- How are dynamically generated types (e.g., from Reflection.Emit) displayed? Include with indicator that they're dynamic.
- How does system handle modules still loading? Wait briefly or return partial results with loading indicator.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST list all loaded managed modules in the debug session
- **FR-002**: System MUST return module name, version, file path, and symbol status for each module
- **FR-003**: System MUST distinguish between managed and native modules
- **FR-004**: System MUST allow browsing types within a specified module
- **FR-005**: System MUST organize types by namespace hierarchy
- **FR-006**: System MUST return type kind (class, interface, struct, enum, delegate)
- **FR-007**: System MUST return type visibility (public, internal, nested types)
- **FR-008**: System MUST support filtering types by namespace pattern
- **FR-009**: System MUST allow inspecting members of a specified type
- **FR-010**: System MUST return method signatures including parameters and return types
- **FR-011**: System MUST return property and field information with types
- **FR-012**: System MUST indicate member visibility (public, private, protected, internal)
- **FR-013**: System MUST indicate static vs instance members
- **FR-014**: System MUST support searching types by name pattern across all modules
- **FR-015**: System MUST support searching methods by name pattern
- **FR-016**: System MUST limit search results to prevent overwhelming output (max 100 results)
- **FR-017**: System MUST work with both running and paused debug sessions for module/type queries
- **FR-018**: System MUST handle generic types and methods showing type parameters

### Key Entities

- **ModuleInfo**: Represents a loaded assembly/DLL with name, version, path, symbol status, and whether it's managed or native
- **TypeInfo**: Represents a type with full name, namespace, kind, visibility, generic parameters, and declaring module
- **MemberInfo**: Represents a type member (method, property, field, event) with name, signature, visibility, and static indicator
- **NamespaceNode**: Represents a namespace in the hierarchy with child namespaces and contained types

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: AI assistants can retrieve the complete module list within 1 second
- **SC-002**: Type browsing for a module returns results within 2 seconds for modules with up to 1000 types
- **SC-003**: Member inspection for a type completes within 500 milliseconds
- **SC-004**: Search across all modules completes within 3 seconds
- **SC-005**: All module inspection operations provide clear error messages when target is unavailable
- **SC-006**: 95% of module/type queries successfully return results without errors
- **SC-007**: Module operations work correctly whether the process is running or paused

## Assumptions

- Module information is available through CLR debugging APIs (ICorDebug) and metadata interfaces
- Symbol (PDB) availability is reported but not required for basic type/member enumeration
- Generic type information uses display names (e.g., `List<T>`) rather than CLR internal names
- Nested types are included with their declaring type context
- Module list is refreshed on each request to reflect dynamic loading/unloading
- Namespace hierarchy is computed from type full names rather than stored separately
