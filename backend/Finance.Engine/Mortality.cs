using System.Globalization;

namespace Finance.Engine;

/// <summary>Whose lifespan the plan must cover when judged with life tables.</summary>
public enum LifeTable
{
    /// <summary>Money must last to the age of death; no life tables.</summary>
    None,
    Male,
    Female,
    /// <summary>A man and a woman of the same age; the money must last while either is alive.</summary>
    Couple,
}

/// <summary>
/// UK mortality rates by calendar year and age (ONS 2024-based principal projection, period tables, Open Government
/// Licence v3.0). A person is followed along their own cohort: at age a, someone born in year b uses the rate for
/// year b + a, which is how ONS builds its cohort tables.
/// </summary>
public sealed class Mortality
{
    /// <summary>Beyond the table's last age, the chance of dying within a year keeps rising by this much a year…</summary>
    const double OldAgeGrowth = 1.05;

    /// <summary>…up to this ceiling (mortality at very old ages levels off; our assumption, not ONS data).</summary>
    const double OldAgeCeiling = 0.5;

    readonly Dictionary<(int Year, int Age), (double Male, double Female)> _qx;
    readonly int _minYear, _maxYear, _maxAge;

    public Mortality(IEnumerable<(int Year, int Age, double Male, double Female)> rows)
    {
        _qx = rows.ToDictionary(r => (r.Year, r.Age), r => (r.Male, r.Female));
        if (_qx.Count == 0) throw new ArgumentException("The mortality table is empty.");
        _minYear = _qx.Keys.Min(k => k.Year);
        _maxYear = _qx.Keys.Max(k => k.Year);
        _maxAge = _qx.Keys.Max(k => k.Age);
    }

    /// <summary>Reads the CSV built by tools/build_mortality.py (lines starting with # are notes).</summary>
    public static Mortality Load(string path)
    {
        var inv = CultureInfo.InvariantCulture;
        var rows = File.ReadLines(path)
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Skip(1)
            .Select(l => l.Split(','))
            .Select(f => (int.Parse(f[0], inv), int.Parse(f[1], inv), double.Parse(f[2], inv), double.Parse(f[3], inv)));
        return new Mortality(rows);
    }

    /// <summary>Chance of dying before the next birthday at an age in a calendar year.</summary>
    public double Qx(bool male, int year, int age)
    {
        year = Math.Clamp(year, _minYear, _maxYear);
        var capped = Math.Min(age, _maxAge);
        if (!_qx.TryGetValue((year, capped), out var q)) throw new ArgumentOutOfRangeException(nameof(age), $"No mortality rate for age {age}.");
        var rate = male ? q.Male : q.Female;
        return age <= _maxAge ? rate : Math.Min(OldAgeCeiling, rate * Math.Pow(OldAgeGrowth, age - _maxAge));
    }

    /// <summary>
    /// Chance of being alive at each whole age from <paramref name="fromAge"/> to <paramref name="toAge"/>, given alive
    /// at <paramref name="fromAge"/>, for someone who is <paramref name="currentAge"/> in <paramref name="currentYear"/>.
    /// For a couple: the chance that at least one of them is alive.
    /// </summary>
    public double[] Survival(LifeTable table, int currentAge, int currentYear, int fromAge, int toAge)
    {
        var born = currentYear - currentAge;
        double[] One(bool male)
        {
            var s = new double[toAge - fromAge + 1];
            s[0] = 1;
            for (var a = fromAge; a < toAge; a++) s[a - fromAge + 1] = s[a - fromAge] * (1 - Qx(male, born + a, a));
            return s;
        }
        return table switch
        {
            LifeTable.Male => One(true),
            LifeTable.Female => One(false),
            LifeTable.Couple => One(true).Zip(One(false), (m, f) => 1 - (1 - m) * (1 - f)).ToArray(),
            _ => throw new ArgumentException("No life table chosen.", nameof(table)),
        };
    }

    /// <summary>Survival at a fractional age, interpolating between birthdays.</summary>
    public static double At(double[] survival, int fromAge, double age)
    {
        var x = Math.Clamp(age - fromAge, 0, survival.Length - 1);
        var lo = (int)Math.Floor(x);
        var hi = Math.Min(lo + 1, survival.Length - 1);
        return survival[lo] + (survival[hi] - survival[lo]) * (x - lo);
    }
}
