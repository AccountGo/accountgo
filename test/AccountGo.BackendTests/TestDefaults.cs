using System;
using System.Runtime.CompilerServices;

namespace AccountGo.BackendTests;

internal static class TestDefaults
{
    /// <summary>
    /// Test hosts start on a new database without the demo books: seeding them took most of each host's start-up.
    /// A test that needs the demo company opts in with <c>builder.UseSetting("SEED_DEMO_DATA", "true")</c>.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize() => Environment.SetEnvironmentVariable("SEED_DEMO_DATA", "false");
}
