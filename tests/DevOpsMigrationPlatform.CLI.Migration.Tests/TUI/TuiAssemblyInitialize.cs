using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI;

/// <summary>
/// Initializes Terminal.Gui once for the entire test assembly to avoid
/// repeated Application.Init/Shutdown calls which cause ConfigurationManager
/// KeyNotFoundException in the FakeDriver when run in the MSTest host.
/// </summary>
[TestClass]
public static class TuiAssemblyInitialize
{
    [AssemblyInitialize]
    public static void InitTerminalGui(TestContext _)
    {
        Application.Init(new FakeDriver());
    }

    [AssemblyCleanup]
    public static void ShutdownTerminalGui()
    {
        Application.Shutdown();
    }
}
