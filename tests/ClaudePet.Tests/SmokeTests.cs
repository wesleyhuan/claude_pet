namespace ClaudePet.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProjectCanReferenceAppProject()
    {
        var mood = ClaudePet.Models.Mood.Happy;
        Assert.Equal(ClaudePet.Models.Mood.Happy, mood);
    }
}
