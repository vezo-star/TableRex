using BookingPlatform.Application.Reports;
using BookingPlatform.Application.Subscriptions;

namespace BookingPlatform.UnitTests.Saas;

public sealed class SaasPrimitivesTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Ada", "\"Ada\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("=SUM(A1:A2)", "\"'=SUM(A1:A2)\"")]
    [InlineData("+27 82", "\"'+27 82\"")]
    [InlineData("-1", "\"'-1\"")]
    [InlineData("@cmd", "\"'@cmd\"")]
    [InlineData("\tTab", "\"'\tTab\"")]
    public void Csv_cells_are_quoted_and_formula_triggers_neutralised(string? value, string expected) =>
        ReportService.Cell(value).Should().Be(expected);

    [Theory]
    [InlineData("https://cdn.example.test/logo.png", true)]
    [InlineData("https://cdn.example.test/a/b.svg?v=2", true)]
    [InlineData("http://cdn.example.test/logo.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    [InlineData("https://cdn.example.test/a b.png", false)]
    [InlineData("https://cdn.example.test/a\".png", false)]
    [InlineData("https://cdn.example.test/a).png", false)]
    [InlineData("https://user:pass@cdn.example.test/logo.png", false)]
    [InlineData("//cdn.example.test/logo.png", false)]
    public void Logo_urls_must_be_plain_https(string url, bool expected) =>
        UpdateBrandingRequestValidator.IsSafeLogoUrl(url).Should().Be(expected);

    [Fact]
    public void Overrides_take_precedence_over_plan_limits()
    {
        var plan = new TenantPlanRecord { PlanMaxVenues = 1, PlanMaxActiveStaff = 5, PlanMaxResourcesPerVenue = null, MaxVenuesOverride = 3 };

        plan.MaxVenues.Should().Be(3);
        plan.MaxActiveStaff.Should().Be(5);
        plan.MaxResourcesPerVenue.Should().BeNull();
    }

    [Fact]
    public void Report_ranges_are_limited_to_366_days()
    {
        var validator = new ReportQueryValidator();
        var from = new DateOnly(2026, 1, 1);

        validator.Validate(new ReportQuery(Guid.NewGuid(), from, from.AddDays(365))).IsValid.Should().BeTrue();
        validator.Validate(new ReportQuery(Guid.NewGuid(), from, from.AddDays(366))).IsValid.Should().BeFalse();
        validator.Validate(new ReportQuery(Guid.NewGuid(), from, from.AddDays(-1))).IsValid.Should().BeFalse();
        validator.Validate(new ReportQuery(Guid.Empty, from, from)).IsValid.Should().BeFalse();
    }
}
