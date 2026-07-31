using System.Windows.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remielle.ChatGPTObserver;

namespace Remielle.Core.Tests;

[TestClass]
public sealed class TargetVisibilityTests
{
    [TestMethod]
    [DataRow(false, WindowVisualState.Normal, true)]
    [DataRow(false, WindowVisualState.Minimized, false)]
    [DataRow(true, WindowVisualState.Normal, false)]
    public void WidgetFollowsTargetWindow(
        bool isOffscreen,
        WindowVisualState visualState,
        bool expected) =>
        Assert.AreEqual(expected, ChatGptObserver.ShouldShowTarget(isOffscreen, visualState));
}
