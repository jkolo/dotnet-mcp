namespace TestTargetApp;

/// <summary>
/// Target for object inspection testing.
/// Used to test nested property access like: this._currentUser.HomeAddress.City
/// Line numbers are significant - tests depend on them!
/// </summary>
public class ObjectTarget
{
    private Person _currentUser = new Person();
    private readonly string _name;

    public ObjectTarget(string name)
    {
        _name = name;
    }

    /// <summary>
    /// Method where we can set breakpoint to inspect this._currentUser and nested properties.
    /// Set breakpoint on line 23 to test object inspection.
    /// </summary>
    public void ProcessUser()
    {
        // LINE 24 - Set breakpoint here to inspect _currentUser and nested properties
        var userName = _currentUser.Name;
        var userCity = _currentUser.HomeAddress.City;
        Console.WriteLine($"Processing user {userName} from {userCity}");
    }
}

/// <summary>
/// Address class with string properties.
/// </summary>
public class Address
{
    public string Street { get; set; } = "Main Street";
    public int Number { get; set; } = 123;
    public string City { get; set; } = "Warsaw";
    public string? Country { get; set; }  // Null property for error testing
}

/// <summary>
/// Base entity with common properties for inheritance testing.
/// Used to test base type property access in expressions.
/// </summary>
public class BaseEntity
{
    public int Id { get; set; } = 1001;
    public DateTime CreatedAt { get; set; } = new DateTime(2026, 1, 1);
}

/// <summary>
/// Person class with nested Address property.
/// Inherits from BaseEntity to test base type property access.
/// </summary>
public class Person : BaseEntity
{
    public string Name { get; set; } = "John";
    public int Age { get; set; } = 30;
    public Address HomeAddress { get; set; } = new Address();
    public Address? WorkAddress { get; set; }  // Null property for error testing
}

// ============================================================================
// 5-Level Nesting Test Classes (T044)
// Path: company.Department.Team.Manager.Contact.Email (5 levels from company)
// ============================================================================

/// <summary>
/// Contact info - Level 5 (deepest)
/// </summary>
public class ContactInfo
{
    public string Email { get; set; } = "manager@company.com";
    public string Phone { get; set; } = "+48 123 456 789";
}

/// <summary>
/// Manager - Level 4
/// </summary>
public class Manager : BaseEntity
{
    public string FullName { get; set; } = "Jane Smith";
    public ContactInfo Contact { get; set; } = new ContactInfo();
}

/// <summary>
/// Team - Level 3
/// </summary>
public class Team
{
    public string Name { get; set; } = "Backend Team";
    public int Size { get; set; } = 5;
    public Manager Manager { get; set; } = new Manager();
}

/// <summary>
/// Department - Level 2
/// </summary>
public class Department
{
    public string Name { get; set; } = "Engineering";
    public string Code { get; set; } = "ENG";
    public Team Team { get; set; } = new Team();
}

/// <summary>
/// Company - Level 1 (root for 5-level nesting test)
/// </summary>
public class Company : BaseEntity
{
    public string Name { get; set; } = "Acme Corp";
    public Department Department { get; set; } = new Department();
}

/// <summary>
/// Target for 5-level nesting test.
/// Tests path: this._company.Department.Team.Manager.Contact.Email
/// </summary>
public class DeepNestingTarget
{
    private Company _company = new Company();

    /// <summary>
    /// Method for 5-level nesting test. Set breakpoint on line with var statement.
    /// </summary>
    public void ProcessCompany()
    {
        // Set breakpoint here to test 5-level nesting
        var email = _company.Department.Team.Manager.Contact.Email;
        Console.WriteLine($"Manager email: {email}");
    }
}
