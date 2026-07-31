using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remielle.ChatGPTObserver;

namespace Remielle.Core.Tests;

[TestClass]
public sealed class UiSelectorsTests
{
    [TestMethod]
    public void CodexSelectorsAvoidActivityButtonsAndCoverCurrentComposer()
    {
        var selectors = UiSelectors.Load(
            Path.Combine(AppContext.BaseDirectory, "UiSelectors.json"),
            _ => Assert.Fail("Selector config should be valid."));

        Assert.IsTrue(selectors.Composer.Any(
            matcher => matcher.ClassNameContains == "ProseMirror"));
        Assert.IsTrue(selectors.Stop.All(
            matcher => matcher.ClassNameContains == "button-composer"));
        Assert.IsTrue(selectors.Assistant.Any(
            matcher => matcher.AutomationId == "RootWebArea"));
        Assert.IsFalse(new UiMatcher { ClassNameContains = "composer" }.IsEmpty);
    }
}
