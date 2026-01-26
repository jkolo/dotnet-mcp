# Data Model: Module and Assembly Inspection

**Feature**: 005-module-ops
**Date**: 2026-01-25
**Source**: spec.md functional requirements, research.md decisions

## Entities

### ModuleInfo

Represents a loaded assembly/DLL in the debugged process.

```
ModuleInfo
├── Name: string              # Assembly simple name (e.g., "MyApp")
├── FullName: string          # Full assembly name with version/culture/key
├── Path: string?             # File path (null for in-memory modules)
├── Version: string           # Assembly version (e.g., "1.0.0.0")
├── IsManaged: bool           # True for .NET assemblies
├── IsDynamic: bool           # True for Reflection.Emit assemblies
├── HasSymbols: bool          # True if PDB loaded
├── ModuleId: string          # Unique identifier for this module
├── BaseAddress: long         # Memory base address
└── Size: int                 # Module size in bytes
```

**Validation**:
- Name must not be empty
- ModuleId must be unique within session

**Source**: FR-001, FR-002, FR-003

---

### TypeInfo

Represents a type defined in a module.

```
TypeInfo
├── FullName: string          # Namespace.TypeName (e.g., "MyApp.Models.Customer")
├── Name: string              # Simple type name (e.g., "Customer")
├── Namespace: string         # Namespace (e.g., "MyApp.Models")
├── Kind: TypeKind            # Class, Interface, Struct, Enum, Delegate
├── Visibility: Visibility    # Public, Internal, etc.
├── IsGeneric: bool           # True if has generic parameters
├── GenericParameters: string[] # ["T", "TKey", "TValue"]
├── IsNested: bool            # True if nested inside another type
├── DeclaringType: string?    # Parent type if nested
├── ModuleName: string        # Which module defines this type
├── BaseType: string?         # Base class (null for interfaces/Object)
└── Interfaces: string[]      # Implemented interfaces
```

**Enums**:
```
TypeKind = Class | Interface | Struct | Enum | Delegate

Visibility = Public | Internal | Private | Protected | ProtectedInternal | PrivateProtected
```

**Validation**:
- FullName must not be empty
- Kind must be valid enum value

**Source**: FR-004, FR-005, FR-006, FR-007, FR-018

---

### MethodMemberInfo

Represents a method in a type.

```
MethodMemberInfo
├── Name: string              # Method name (e.g., "GetCustomer")
├── Signature: string         # Full signature (e.g., "Customer GetCustomer(int id)")
├── ReturnType: string        # Return type name
├── Parameters: ParameterInfo[] # Method parameters
├── Visibility: Visibility    # Public, Private, etc.
├── IsStatic: bool            # True for static methods
├── IsVirtual: bool           # True for virtual/override
├── IsAbstract: bool          # True for abstract methods
├── IsGeneric: bool           # True if generic method
├── GenericParameters: string[] # Generic type parameters
└── DeclaringType: string     # Type that declares this method
```

**ParameterInfo**:
```
ParameterInfo
├── Name: string              # Parameter name
├── Type: string              # Parameter type
├── IsOptional: bool          # Has default value
├── IsOut: bool               # Out parameter
├── IsRef: bool               # Ref parameter
└── DefaultValue: string?     # Default value if optional
```

**Source**: FR-009, FR-010, FR-012, FR-013, FR-018

---

### PropertyMemberInfo

Represents a property in a type.

```
PropertyMemberInfo
├── Name: string              # Property name
├── Type: string              # Property type
├── Visibility: Visibility    # Getter visibility (most accessible)
├── IsStatic: bool            # True for static properties
├── HasGetter: bool           # Has get accessor
├── HasSetter: bool           # Has set accessor
├── GetterVisibility: Visibility?
├── SetterVisibility: Visibility?
├── IsIndexer: bool           # True for indexers (this[])
└── IndexerParameters: ParameterInfo[] # Indexer parameters
```

**Source**: FR-009, FR-011, FR-012, FR-013

---

### FieldMemberInfo

Represents a field in a type.

```
FieldMemberInfo
├── Name: string              # Field name
├── Type: string              # Field type
├── Visibility: Visibility    # Field visibility
├── IsStatic: bool            # True for static fields
├── IsReadOnly: bool          # True for readonly fields
├── IsConst: bool             # True for const fields
└── ConstValue: string?       # Const value if applicable
```

**Source**: FR-009, FR-011, FR-012, FR-013

---

### EventMemberInfo

Represents an event in a type.

```
EventMemberInfo
├── Name: string              # Event name
├── Type: string              # Event handler type
├── Visibility: Visibility    # Event visibility
├── IsStatic: bool            # True for static events
├── AddMethod: string         # Add accessor signature
└── RemoveMethod: string      # Remove accessor signature
```

**Source**: FR-009, FR-012, FR-013

---

### TypeMembersResult

Aggregated result for type member inspection.

```
TypeMembersResult
├── TypeName: string          # Full type name
├── Methods: MethodMemberInfo[]
├── Properties: PropertyMemberInfo[]
├── Fields: FieldMemberInfo[]
├── Events: EventMemberInfo[]
├── IncludesInherited: bool   # True if base class members included
├── MethodCount: int          # Total method count
├── PropertyCount: int        # Total property count
├── FieldCount: int           # Total field count
└── EventCount: int           # Total event count
```

---

### SearchResult

Result from searching across modules.

```
SearchResult
├── Query: string             # Original search pattern
├── SearchType: SearchType    # Types, Methods, or Both
├── Types: TypeInfo[]         # Matching types
├── Methods: MethodSearchMatch[] # Matching methods
├── TotalMatches: int         # Total matches found
├── ReturnedMatches: int      # Matches returned (may be limited)
├── Truncated: bool           # True if results were limited
└── ContinuationToken: string? # Token to get more results
```

**MethodSearchMatch**:
```
MethodSearchMatch
├── DeclaringType: string     # Type containing the method
├── ModuleName: string        # Module containing the type
├── Method: MethodMemberInfo  # The matching method
└── MatchReason: string       # Why it matched (name, signature)
```

**Enum**:
```
SearchType = Types | Methods | Both
```

**Source**: FR-014, FR-015, FR-016

---

### TypesResult

Result from type browsing in a module.

```
TypesResult
├── ModuleName: string        # Module that was queried
├── Namespace: string?        # Namespace filter (if applied)
├── Types: TypeInfo[]         # Types matching criteria
├── Namespaces: NamespaceNode[] # Namespace hierarchy
├── TotalCount: int           # Total types in module
├── ReturnedCount: int        # Types returned
├── Truncated: bool           # True if paginated
└── ContinuationToken: string? # Token for next page
```

**NamespaceNode**:
```
NamespaceNode
├── Name: string              # Namespace name
├── FullName: string          # Full namespace path
├── TypeCount: int            # Types directly in namespace
├── ChildNamespaces: string[] # Child namespace names
└── Depth: int                # Nesting level (0 = root)
```

**Source**: FR-004, FR-005, FR-008

---

## Relationships

```
Session (1) ──────────┬──── (*) Module
                      │
Module (1) ───────────┼──── (*) Type
                      │
Type (1) ─────────────┼──── (*) Method
                      ├──── (*) Property
                      ├──── (*) Field
                      └──── (*) Event

Type (1) ─────────────── (0..1) DeclaringType (for nested types)
Type (1) ─────────────── (0..1) BaseType
Type (1) ─────────────── (*) Interfaces
```

## State Transitions

Module inspection entities are read-only views of debuggee metadata. No state transitions
apply - data is fetched fresh on each request.

**Note**: Module list may change during session if assemblies are dynamically loaded/unloaded.
Each `modules_list` call returns current state.
