// Test target for debugger attach/launch and breakpoint tests
using TestTargetApp;

Console.WriteLine($"TestTargetApp started. PID: {Environment.ProcessId}");
Console.WriteLine("READY");
Console.Out.Flush();

// Command loop for test orchestration
while (true)
{
    var command = Console.ReadLine();
    if (string.IsNullOrEmpty(command) || command == "exit")
        break;

    switch (command)
    {
        case "loop":
            // Run a simple loop - breakpoints can be set on LoopTarget.RunLoop
            LoopTarget.RunLoop(5);
            Console.WriteLine("LOOP_DONE");
            Console.Out.Flush();
            break;

        case "method":
            // Call a method - breakpoints can be set on MethodTarget.SayHello
            var result = MethodTarget.SayHello("World");
            Console.WriteLine($"METHOD_RESULT:{result}");
            Console.Out.Flush();
            break;

        case "exception":
            // Throw an exception - for exception breakpoint testing
            try
            {
                ExceptionTarget.ThrowException();
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"EXCEPTION_CAUGHT:{ex.Message}");
                Console.Out.Flush();
            }
            break;

        case "nested":
            // Call nested methods - for stack trace testing
            NestedTarget.Level1();
            Console.WriteLine("NESTED_DONE");
            Console.Out.Flush();
            break;

        case "object":
            // Create and process object - for nested property inspection testing
            var objectTarget = new ObjectTarget("TestUser");
            objectTarget.ProcessUser();
            Console.WriteLine("OBJECT_DONE");
            Console.Out.Flush();
            break;

        case "deep":
            // 5-level nesting test - for deep property chain testing (T044)
            var deepTarget = new DeepNestingTarget();
            deepTarget.ProcessCompany();
            Console.WriteLine("DEEP_DONE");
            Console.Out.Flush();
            break;

        default:
            Console.WriteLine($"UNKNOWN_COMMAND:{command}");
            Console.Out.Flush();
            break;
    }
}

Console.WriteLine("TestTargetApp exiting.");
