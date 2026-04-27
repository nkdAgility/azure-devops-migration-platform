using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI;

[TestClass]
[TestCategory("Unit")]
public class TuiMetricsViewTests
{
    [TestMethod]
    public void View_CanBeConstructedAndDisposed()
    {
        using var view = new TuiMetricsView();
        Assert.IsNotNull(view);
    }
}
