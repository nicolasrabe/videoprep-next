using Videoprep.Core;

namespace Videoprep.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void Name_is_always_lowercase() => Assert.Equal("videoprep", AppInfo.Name);

    [Fact]
    public void Version_comes_from_the_build_and_is_0_3()
    {
        Assert.StartsWith("0.3.", AppInfo.Version);
        Assert.DoesNotContain("+", AppInfo.Version);
    }
}
