using Videoprep.App.Cli;

namespace Videoprep.App.Tests;

public class DemoCommandTests
{
    [Theory]
    [InlineData("frames must be 0-29 for this video (30 fps)", "este video tiene 30 fotogramas por segundo")]
    [InlineData("frames must be 0-29 for this video (30 fps)", "de 00 a 29")]
    [InlineData("Start is after the end of the video (180 frames)", "tiene 180 fotogramas")]
    [InlineData("End is before Start", "el fin está antes del inicio")]
    [InlineData("the result did not pass the check: the result has 30 frames, expected 31", "no pasó la revisión")]
    [InlineData("", "mirá el registro")]
    public void Review_reasons_are_explained_in_spanish(string reason, string expected) =>
        Assert.Contains(expected, DemoCommand.ExplainReview(reason));
}
