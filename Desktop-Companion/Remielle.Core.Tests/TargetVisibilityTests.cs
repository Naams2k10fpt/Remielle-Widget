using System.Windows.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remielle.ChatGPTObserver;

namespace Remielle.Core.Tests;

[TestClass]
public sealed class TargetVisibilityTests
{
    [TestMethod]
    [DataRow(false, WindowVisualState.Normal, true, true)]
    [DataRow(false, WindowVisualState.Minimized, true, false)]
    [DataRow(true, WindowVisualState.Normal, true, false)]
    [DataRow(false, WindowVisualState.Normal, false, false)]
    public void WidgetFollowsTargetWindow(
        bool isOffscreen,
        WindowVisualState visualState,
        bool isForeground,
        bool expected) =>
        Assert.AreEqual(
            expected,
            ChatGptObserver.ShouldShowTarget(isOffscreen, visualState, isForeground));
}
