using ExamApp.Api.Services.AdminUsers;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #152/#153 ortak sayfa hesabı: kırpma ve taşma-güvenli offset.</summary>
public class AdminListPagingTests
{
    [Fact]
    public void Constants_match_the_contract()
    {
        AdminListPaging.DefaultPageSize.ShouldBe(20);
        AdminListPaging.MaxPageSize.ShouldBe(100); // auth-api IncludeAccountStatus limiti
    }

    [Theory]
    [InlineData(1, 20, 1, 20)]
    [InlineData(0, 20, 1, 20)]
    [InlineData(int.MinValue, 20, 1, 20)]
    [InlineData(5, 0, 5, 1)]
    [InlineData(5, -10, 5, 1)]
    [InlineData(5, 100, 5, 100)]
    [InlineData(5, 101, 5, 100)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, 100)]
    public void Normalize_clamps_page_and_page_size(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var (p, s) = AdminListPaging.Normalize(page, pageSize);

        p.ShouldBe(expectedPage);
        s.ShouldBe(expectedPageSize);
    }

    [Theory]
    [InlineData(1, 20, 25, true, 0)]
    [InlineData(2, 20, 25, true, 20)]
    [InlineData(2, 20, 21, true, 20)]   // son sayfada tek kayıt
    [InlineData(2, 20, 20, false, 0)]   // offset == totalCount → boş
    [InlineData(3, 20, 25, false, 0)]
    [InlineData(1, 20, 0, false, 0)]    // hiç kayıt yok
    [InlineData(107374183, 20, 25, false, 0)] // (page-1)*20 int'te taşar
    [InlineData(int.MaxValue, 100, int.MaxValue, false, 0)]
    public void TryGetOffset_is_overflow_safe_and_rejects_pages_past_the_end(
        int page, int pageSize, int totalCount, bool expected, int expectedOffset)
    {
        AdminListPaging.TryGetOffset(page, pageSize, totalCount, out var offset).ShouldBe(expected);
        offset.ShouldBe(expectedOffset);
    }

    [Fact]
    public void EmptyPage_keeps_paging_metadata()
    {
        var page = AdminListPaging.EmptyPage<int>(7, 20, 25);

        page.PageNumber.ShouldBe(7);
        page.PageSize.ShouldBe(20);
        page.TotalCount.ShouldBe(25);
        page.Items.ShouldBeEmpty();
    }
}
