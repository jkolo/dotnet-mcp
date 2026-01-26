# Quickstart: Module and Assembly Inspection

**Feature**: 005-module-ops
**Prerequisites**: Active debug session (running or paused)

## Overview

Module inspection tools enable AI assistants to explore the code structure of debugged
applications - listing loaded assemblies, browsing types by namespace, inspecting type
members, and searching for code across modules.

**Key difference from inspection tools**: Module operations work with **both running and
paused** debug sessions since they only read metadata, not runtime state.

## Tools

| Tool | Purpose | Common Use Case |
|------|---------|-----------------|
| `modules_list` | List loaded assemblies | Understand application composition |
| `types_get` | Browse types in module | Find classes to set breakpoints |
| `members_get` | Inspect type members | Understand type structure |
| `modules_search` | Search across modules | Find specific code quickly |

---

## Tool: modules_list

### Basic Usage

```json
{
  "tool": "modules_list",
  "parameters": {}
}
```

### Response

```json
{
  "success": true,
  "modules": [
    {
      "name": "MyApp",
      "fullName": "MyApp, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
      "path": "/app/MyApp.dll",
      "version": "1.0.0.0",
      "isManaged": true,
      "isDynamic": false,
      "hasSymbols": true,
      "moduleId": "mod-12345",
      "baseAddress": "0x00007FF8A1230000",
      "size": 45056
    },
    {
      "name": "System.Private.CoreLib",
      "fullName": "System.Private.CoreLib, Version=8.0.0.0, ...",
      "path": "/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.1/System.Private.CoreLib.dll",
      "version": "8.0.0.0",
      "isManaged": true,
      "isDynamic": false,
      "hasSymbols": false,
      "moduleId": "mod-12346",
      "baseAddress": "0x00007FF8A0000000",
      "size": 12582912
    }
  ],
  "count": 2
}
```

### Filter Application Modules Only

```json
{
  "tool": "modules_list",
  "parameters": {
    "include_system": false
  }
}
```

### Common Scenarios

**Scenario 1: Understand Application Structure**

```
AI: Let me see what modules are loaded in this application.
[Calls modules_list]

AI: The application has 3 main assemblies:
    - MyApp (your application) - symbols loaded ✓
    - MyApp.Data (data layer) - symbols loaded ✓
    - MyApp.Services (business logic) - symbols loaded ✓

    Plus 45 framework assemblies (System.*, Microsoft.*)
```

**Scenario 2: Check Symbol Availability**

```
AI: I want to set breakpoints. Let me check which modules have symbols.
[Calls modules_list]

AI: Symbols are available for:
    - MyApp.dll ✓
    - MyApp.Data.dll ✓

    Missing symbols for:
    - ThirdParty.Library.dll ✗ (breakpoints will be limited)
```

---

## Tool: types_get

### Basic Usage

```json
{
  "tool": "types_get",
  "parameters": {
    "module_name": "MyApp"
  }
}
```

### Filter by Namespace

```json
{
  "tool": "types_get",
  "parameters": {
    "module_name": "MyApp",
    "namespace": "MyApp.Models"
  }
}
```

### Response

```json
{
  "success": true,
  "moduleName": "MyApp",
  "types": [
    {
      "fullName": "MyApp.Models.Customer",
      "name": "Customer",
      "namespace": "MyApp.Models",
      "kind": "class",
      "visibility": "public",
      "isGeneric": false,
      "baseType": "System.Object"
    },
    {
      "fullName": "MyApp.Models.Order",
      "name": "Order",
      "namespace": "MyApp.Models",
      "kind": "class",
      "visibility": "public",
      "isGeneric": false,
      "baseType": "System.Object"
    },
    {
      "fullName": "MyApp.Models.OrderStatus",
      "name": "OrderStatus",
      "namespace": "MyApp.Models",
      "kind": "enum",
      "visibility": "public",
      "baseType": "System.Enum"
    }
  ],
  "namespaces": [
    { "name": "Models", "fullName": "MyApp.Models", "typeCount": 3 }
  ],
  "totalCount": 3,
  "returnedCount": 3,
  "truncated": false
}
```

### Common Scenarios

**Scenario: Find Controllers for Breakpoints**

```
AI: I need to find the controller handling this request.
[Calls types_get with namespace: "MyApp.Controllers"]

AI: Found 5 controllers in MyApp.Controllers:
    - CustomerController (public class)
    - OrderController (public class)
    - ProductController (public class)
    - AuthController (public class)
    - AdminController (internal class)

    Let me inspect CustomerController to find the right method.
```

---

## Tool: members_get

### Basic Usage

```json
{
  "tool": "members_get",
  "parameters": {
    "type_name": "MyApp.Controllers.CustomerController"
  }
}
```

### Include Inherited Members

```json
{
  "tool": "members_get",
  "parameters": {
    "type_name": "MyApp.Controllers.CustomerController",
    "include_inherited": true
  }
}
```

### Response

```json
{
  "success": true,
  "typeName": "MyApp.Controllers.CustomerController",
  "methods": [
    {
      "name": "GetCustomer",
      "signature": "Task<Customer> GetCustomer(int id)",
      "returnType": "System.Threading.Tasks.Task<MyApp.Models.Customer>",
      "parameters": [
        { "name": "id", "type": "System.Int32", "isOptional": false }
      ],
      "visibility": "public",
      "isStatic": false,
      "isVirtual": false,
      "isAsync": true
    },
    {
      "name": "CreateCustomer",
      "signature": "Task<Customer> CreateCustomer(CustomerDto dto)",
      "returnType": "System.Threading.Tasks.Task<MyApp.Models.Customer>",
      "parameters": [
        { "name": "dto", "type": "MyApp.Dtos.CustomerDto", "isOptional": false }
      ],
      "visibility": "public",
      "isStatic": false
    }
  ],
  "properties": [
    {
      "name": "CustomerService",
      "type": "MyApp.Services.ICustomerService",
      "visibility": "private",
      "isStatic": false,
      "hasGetter": true,
      "hasSetter": false
    }
  ],
  "fields": [],
  "events": [],
  "methodCount": 2,
  "propertyCount": 1,
  "fieldCount": 0,
  "eventCount": 0
}
```

### Common Scenarios

**Scenario: Find Method to Debug**

```
AI: The bug is in customer creation. Let me find the method.
[Calls members_get for CustomerController]

AI: Found the method:
    CreateCustomer(CustomerDto dto) → Task<Customer>

    This is the entry point. Let me set a breakpoint here.
[Calls breakpoint_set with function: "MyApp.Controllers.CustomerController.CreateCustomer"]
```

---

## Tool: modules_search

### Search for Types

```json
{
  "tool": "modules_search",
  "parameters": {
    "pattern": "*Service",
    "search_type": "types"
  }
}
```

### Search for Methods

```json
{
  "tool": "modules_search",
  "parameters": {
    "pattern": "Validate*",
    "search_type": "methods",
    "module_filter": "MyApp*"
  }
}
```

### Response

```json
{
  "success": true,
  "query": "*Service",
  "searchType": "types",
  "types": [
    {
      "fullName": "MyApp.Services.CustomerService",
      "name": "CustomerService",
      "namespace": "MyApp.Services",
      "kind": "class",
      "moduleName": "MyApp"
    },
    {
      "fullName": "MyApp.Services.OrderService",
      "name": "OrderService",
      "namespace": "MyApp.Services",
      "kind": "class",
      "moduleName": "MyApp"
    },
    {
      "fullName": "MyApp.Services.ICustomerService",
      "name": "ICustomerService",
      "namespace": "MyApp.Services",
      "kind": "interface",
      "moduleName": "MyApp"
    }
  ],
  "methods": [],
  "totalMatches": 3,
  "returnedMatches": 3,
  "truncated": false
}
```

### Common Scenarios

**Scenario: Find All Validation Methods**

```
AI: The validation is failing somewhere. Let me find all validation methods.
[Calls modules_search with pattern: "Validate*", search_type: "methods"]

AI: Found 8 validation methods across the application:
    - CustomerValidator.ValidateCustomer()
    - OrderValidator.ValidateOrder()
    - OrderValidator.ValidateLineItems()
    - ...

    Let me set breakpoints on all of them to trace the flow.
```

---

## Workflow Example: Navigating Unfamiliar Code

```
1. AI: Let me understand this application's structure.
   [Calls modules_list with include_system: false]

2. AI: Found 4 application assemblies. The main one is MyApp.
   Let me explore its namespaces.
   [Calls types_get with module_name: "MyApp"]

3. AI: The app has these namespaces:
   - MyApp.Controllers (5 types)
   - MyApp.Services (8 types)
   - MyApp.Models (12 types)
   - MyApp.Data (6 types)

   The bug seems to be in order processing. Let me search.
   [Calls modules_search with pattern: "*Order*"]

4. AI: Found OrderController, OrderService, Order, OrderRepository.
   Let me inspect OrderService to understand the flow.
   [Calls members_get with type_name: "MyApp.Services.OrderService"]

5. AI: OrderService has these methods:
   - CreateOrder(OrderDto) → Order
   - ProcessPayment(Order) → PaymentResult
   - ValidateOrder(Order) → bool

   The bug is likely in ProcessPayment. Setting breakpoint there.
   [Calls breakpoint_set]
```

---

## Error Handling

All tools return structured errors:

```json
{
  "success": false,
  "error": {
    "code": "MODULE_NOT_FOUND",
    "message": "Module 'NonExistent' not found in loaded assemblies",
    "details": {
      "moduleName": "NonExistent",
      "availableModules": ["MyApp", "MyApp.Data", "System.Private.CoreLib"]
    }
  }
}
```

Common errors:
- `NO_SESSION` - No debug session active
- `MODULE_NOT_FOUND` - Specified module not loaded
- `TYPE_NOT_FOUND` - Type not found in specified module
- `INVALID_PATTERN` - Search pattern is malformed
