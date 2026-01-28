Feature: Module Enumeration
    As a debugger user
    I want to list all loaded modules after attaching to a process
    So that I can set breakpoints in any loaded assembly

    Scenario: All modules are visible after attach
        Given a running test target process
        And the debugger is attached to the test target
        When I list all modules without system filter
        Then the module list should contain "TestTargetApp"
        And the module list should contain "TestLib1"
        And the module list should contain "TestLib2"
        And the module list should contain "TestLib3"
        And the module list should contain "TestLib4"
        And the module list should contain "TestLib5"
        And the module list should contain "TestLib6"
        And the module list should contain "TestLib7"
        And the module list should contain "TestLib8"
        And the module list should contain "TestLib9"
        And the module list should contain "TestLib10"
