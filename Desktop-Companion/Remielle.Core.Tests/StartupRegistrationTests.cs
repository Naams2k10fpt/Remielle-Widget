using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remielle.Overlay;

namespace Remielle.Core.Tests;

[TestClass]
public sealed class StartupRegistrationTests
{
    [TestMethod]
    public void StartupCommandQuotesExecutablePath()
    {
        const string executablePath = @"C:\Program Files\Remielle Widget\Remielle.Overlay.exe";

        Assert.AreEqual(
            $"\"{executablePath}\" --background",
            StartupRegistration.CommandFor(executablePath));
    }
}
