namespace Finance.Engine;

/// <summary>A shape for how spending (including other income) changes with age, used by <see cref="SpendingFitter"/>.</summary>
public sealed record SpendingPattern(string Id, string Label, string Description, string? Source, Func<int, int, double> Shape)
{
    /// <summary>Spending at an age relative to spending at retirement (1 = the same).</summary>
    public double At(int retirementAge, int age) => Shape(retirementAge, age);
}

public static class SpendingPatterns
{
    public static readonly SpendingPattern Level = new(
        "level",
        "Level",
        "The same spending power every year.",
        null,
        (_, _) => 1);

    /// <summary>
    /// UK households: spending per person stays roughly level in real terms to about 80, then flat or falling
    /// (IFS: about -1% a year at 82-88). Modelled as level to 80, then easing 1% a year. Care costs are not included.
    /// </summary>
    public static readonly SpendingPattern UkIfs = new(
        "uk-ifs",
        "UK average (IFS)",
        "Roughly the same spending power until about 80, then easing by about 1% a year, as the Institute for Fiscal Studies found for UK retirees. Care costs are not included, so add those as an outgoing.",
        "Crawford, Karjalainen & Sturrock (2022), How does spending change through retirement?, IFS Report R209",
        (_, age) => age <= 80 ? 1 : Math.Pow(0.99, age - 80));

    /// <summary>US reference level for Blanchett's formula, which depends on the spending level in dollars.</summary>
    public const double BlanchettReferenceSpending = 50_000;

    /// <summary>
    /// Blanchett (2014), Equation 1: the annual real change in spending is
    /// 0.00008 Age² - 0.0125 Age - 0.0066 ln(spending in $) + 0.546, evaluated here at $50,000.
    /// Falls about 1% a year through the 70s and early 80s (lowest near 78) and rises again in the 90s.
    /// </summary>
    public static double BlanchettChange(int age, double spending = BlanchettReferenceSpending) =>
        0.00008 * age * age - 0.0125 * age - 0.0066 * Math.Log(spending) + 0.546;

    public static readonly SpendingPattern UsBlanchett = new(
        "us-blanchett",
        "US research (Blanchett)",
        "Spending power eases by about 1% a year through the 70s and early 80s, then rises again late in life, as David Blanchett found for US retirees (the \"retirement spending smile\", at $50,000 a year).",
        "Blanchett (2014), Exploring the Retirement Consumption Puzzle, Journal of Financial Planning 27(5)",
        (retirementAge, age) =>
        {
            var factor = 1.0;
            for (var a = retirementAge; a < age; a++) factor *= 1 + BlanchettChange(a);
            return factor;
        });

    public static readonly IReadOnlyList<SpendingPattern> All = [UkIfs, UsBlanchett, Level];

    public static SpendingPattern? Find(string? id) => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
