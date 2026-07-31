using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remielle.Overlay;

namespace Remielle.Core.Tests;

[TestClass]
public sealed class AnimatedGifTests
{
    [TestMethod]
    public void DeltaFramesAreCompositedToTheLogicalCanvas()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "waiting_user_input.gif");

        var (frames, _) = AnimatedGif.Decode(path);

        Assert.IsTrue(frames.Length > 1);
        Assert.IsTrue(frames.All(frame => frame.PixelWidth == 360 && frame.PixelHeight == 360));
    }
}
