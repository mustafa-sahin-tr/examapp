using ExamApp.Foundation.Security;

namespace ExamApp.Foundation.Tests.Security;

public class DisplayTextSanitizerTests
{
    [Fact]
    public void Strips_bidi_and_zero_width_characters()
        => DisplayTextSanitizer.Clean("A‮b​c⁦d﻿e‏", 100).ShouldBe("Abcde");

    [Fact]
    public void Control_characters_and_line_breaks_become_single_spaces()
        => DisplayTextSanitizer.Clean("a\r\n\tb\u0000\u0085c", 100).ShouldBe("a b c");

    [Fact]
    public void Trims_and_returns_empty_for_null_or_only_invisible_text()
    {
        DisplayTextSanitizer.Clean(null, 10).ShouldBeEmpty();
        DisplayTextSanitizer.Clean("  ​‮ ", 10).ShouldBeEmpty();
    }

    [Fact]
    public void Truncates_to_the_maximum_length()
        => DisplayTextSanitizer.Clean(new string('x', 500), 200).Length.ShouldBe(200);

    [Fact]
    public void Truncation_never_splits_a_surrogate_pair()
    {
        var text = "ab" + "\U0001F600";
        var cleaned = DisplayTextSanitizer.Clean(text, 3);
        cleaned.ShouldBe("ab");
    }

    [Fact]
    public void Drops_lone_surrogates_and_keeps_valid_pairs()
        => DisplayTextSanitizer.Clean("a\uD800b\U0001F600", 100).ShouldBe("ab\U0001F600");

    [Fact]
    public void Keeps_turkish_letters_intact()
        => DisplayTextSanitizer.Clean("İğdır Öğrenci Ş.", 100).ShouldBe("İğdır Öğrenci Ş.");
}
