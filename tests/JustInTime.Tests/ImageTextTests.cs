using JustInTime.Core.Services;

namespace JustInTime.Tests;

public sealed class ImageTextTests
{
    [Fact]
    public void Lines_are_joined_into_one_with_single_spaces()
    {
        var text = OcrTextNormalizer.ToSingleLine(["  Привет,  ", "как дела?", "", "Увидимся\tв 5"]);

        Assert.Equal("Привет, как дела? Увидимся в 5", text);
    }

    [Fact]
    public void Invisible_and_control_characters_are_removed()
    {
        Assert.Equal("a b c", OcrTextNormalizer.ToSingleLine(["a\u200B b", "\r\nc\uFEFF"]));
    }

    [Fact]
    public void Nothing_recognized_gives_empty_text()
    {
        Assert.Equal(string.Empty, OcrTextNormalizer.ToSingleLine([]));
        Assert.Equal(string.Empty, OcrTextNormalizer.ToSingleLine(["  ", "\n"]));
    }

    [Theory]
    [InlineData(@"C:\a\shot.png")]
    [InlineData(@"C:\a\photo.JPG")]
    [InlineData(@"C:\a\scan.tiff")]
    public void Supported_images_pass(string path)
    {
        Assert.Null(ImageFileRules.Validate(path, 1024));
    }

    [Theory]
    [InlineData(@"C:\a\doc.pdf", 1024)]
    [InlineData(@"C:\a\noext", 1024)]
    [InlineData(@"C:\a\empty.png", 0)]
    [InlineData(@"C:\a\huge.png", ImageFileRules.MaxBytes + 1)]
    public void Unsuitable_files_are_rejected_with_a_reason(string path, long length)
    {
        Assert.False(string.IsNullOrEmpty(ImageFileRules.Validate(path, length)));
    }
}
