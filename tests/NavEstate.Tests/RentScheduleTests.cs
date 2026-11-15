using FluentAssertions;
using NavEstate.Api.Features;
using Xunit;

namespace NavEstate.Tests;

public class RentScheduleTests
{
    static readonly DateOnly Start = new(2026, 1, 1);

    [Fact]
    public void Five_percent_compounding_matches_the_spec_acceptance_numbers()
    {
        var rows = RentScheduleGenerator.Generate(4200, Start, new DateOnly(2029, 12, 31), new EscalationInput("fixed_percent", 5));
        rows.Select(r => r.MonthlyRent).Should().Equal(4200.00m, 4410.00m, 4630.50m, 4862.03m);
        rows.Select(r => r.From).Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), new DateOnly(2028, 1, 1), new DateOnly(2029, 1, 1));
        rows.Select(r => r.To).Should().Equal(new DateOnly(2026, 12, 31), new DateOnly(2027, 12, 31), new DateOnly(2028, 12, 31), new DateOnly(2029, 12, 31));
        rows[1].Label.Should().Be("+5%");
    }

    [Fact]
    public void Non_compounding_percent_grows_from_the_base()
    {
        var rows = RentScheduleGenerator.Generate(4200, Start, new DateOnly(2029, 12, 31), new EscalationInput("fixed_percent", 5, Compounding: false));
        rows.Select(r => r.MonthlyRent).Should().Equal(4200m, 4410m, 4620m, 4830m);
    }

    [Fact]
    public void Fixed_amount_every_six_months()
    {
        var rows = RentScheduleGenerator.Generate(30000, Start, new DateOnly(2027, 6, 30), new EscalationInput("fixed_amount", 1500, EveryMonths: 6));
        rows.Select(r => r.MonthlyRent).Should().Equal(30000m, 31500m, 33000m);
        rows.Select(r => r.From).Should().Equal(new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1), new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void Stepped_uses_the_typed_rows_and_market_review_has_no_amount()
    {
        var stepped = RentScheduleGenerator.Generate(50000, Start, new DateOnly(2028, 12, 31),
            new EscalationInput("stepped", 0, Steps: [new(new DateOnly(2028, 1, 1), 60000), new(new DateOnly(2027, 1, 1), 55000)]));
        stepped.Select(r => (r.From, r.MonthlyRent)).Should().Equal((Start, 50000m), (new DateOnly(2027, 1, 1), 55000m), (new DateOnly(2028, 1, 1), 60000m));

        var review = RentScheduleGenerator.Generate(50000, Start, new DateOnly(2027, 12, 31), new EscalationInput("market_review", 0, EveryMonths: 24));
        review.Should().HaveCount(1, "the first review would fall after the lease end");
        review = RentScheduleGenerator.Generate(50000, Start, new DateOnly(2028, 12, 31), new EscalationInput("market_review", 0, EveryMonths: 24));
        review[1].MonthlyRent.Should().BeNull();
        review[1].Label.Should().Be("Market review");
    }

    [Fact]
    public void Open_ended_leases_get_five_years_and_the_last_row_stays_open()
    {
        var rows = RentScheduleGenerator.Generate(1000, Start, null, new EscalationInput("fixed_percent", 10));
        rows.Should().HaveCount(5);
        rows[^1].To.Should().BeNull();
    }

    [Fact]
    public void Validation_reports_errors_instead_of_throwing()
    {
        RentScheduleGenerator.Validate(new EscalationInput("stepped", 0), Start, null).Should().Contain("at least one step");
        RentScheduleGenerator.Validate(new EscalationInput("fixed_percent", 0), Start, null).Should().Contain("percentage");
        RentScheduleGenerator.Validate(new EscalationInput("bogus", 5), Start, null).Should().Contain("escalation type");
        RentScheduleGenerator.Validate(new EscalationInput("stepped", 0, Steps: [new(Start, 100)]), Start, null).Should().Contain("after the lease start");
        RentScheduleGenerator.Validate(null, Start, null).Should().BeNull();
    }

    [Fact]
    public void Notice_template_is_filled_in()
    {
        RentScheduleService.Render("Dear {{tenant_name}}, {{old_rent}} to {{new_rent}}", new() { ["tenant_name"] = "Amina", ["old_rent"] = "a", ["new_rent"] = "b" })
            .Should().Be("Dear Amina, a to b");
        RentScheduleService.Money(4862.03m, "KES").Should().Be("KES 4,862.03");
    }
}
