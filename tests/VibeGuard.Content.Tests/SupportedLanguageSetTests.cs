#pragma warning disable CA1707 // xUnit Method_State_Expected naming

using VibeGuard.Content;

namespace VibeGuard.Content.Tests;

public class SupportedLanguageSetTests
{
    [Fact]
    public void Default_IncludesAllConfiguredLanguages()
    {
        var set = SupportedLanguageSet.Default();

        set.Should().BeEquivalentTo(["csharp", "python", "c", "go", "rust", "javascript", "typescript", "java", "kotlin", "swift", "ruby", "php"]);
        set.Count.Should().Be(12);
    }

    [Fact]
    public void Contains_IsCaseSensitiveOrdinal()
    {
        var set = SupportedLanguageSet.Default();

        set.Contains("python").Should().BeTrue();
        set.Contains("Python").Should().BeFalse();
        set.Contains("PYTHON").Should().BeFalse();
        set.Contains("klingon").Should().BeFalse();
        set.Contains(null).Should().BeFalse();
    }

    [Fact]
    public void Constructor_Deduplicates()
    {
        var set = new SupportedLanguageSet(["go", "go", "rust", "rust", "rust"]);
        set.Count.Should().Be(2);
    }

    [Fact]
    public void Constructor_EmptySet_Throws()
    {
        var act = () => new SupportedLanguageSet([]);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*at least one entry*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("CSharp")]
    [InlineData("c++")]
    [InlineData("c sharp")]
    [InlineData("1337")]
    [InlineData("-rust")]
    public void Constructor_InvalidWireName_Throws(string bad)
    {
        var act = () => new SupportedLanguageSet(["csharp", bad]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EntryTooLong_Throws()
    {
        var tooLong = new string('a', SupportedLanguageSet.MaxWireLength + 1);
        var act = () => new SupportedLanguageSet(["csharp", tooLong]);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*characters*");
    }

    [Fact]
    public void Constructor_NullEntry_Throws()
    {
        var act = () => new SupportedLanguageSet(["csharp", null!]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToSortedList_IsOrdinalSorted()
    {
        var set = new SupportedLanguageSet(["rust", "csharp", "go", "c", "python"]);

        // Ordinal sort: c, csharp, go, python, rust
        set.ToSortedList().Should().Be("c, csharp, go, python, rust");
    }

    [Fact]
    public void Constructor_AcceptsHyphenatedWireNames()
    {
        // Hyphens are explicitly allowed in the regex so identifiers like
        // "objective-c" stay available for future languages.
        var set = new SupportedLanguageSet(["objective-c", "csharp"]);
        set.Contains("objective-c").Should().BeTrue();
    }
}
