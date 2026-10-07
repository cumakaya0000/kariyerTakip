namespace KariyerTakip.Services;

public static class DrivingLicenseCoverage
{
    // Karayolları Trafik Yönetmeliği, madde 85: https://www.uab.gov.tr/uploads/legislations/karayollari-trafik-yonetmeligi/karayollari-trafik-yonetmeligi-65cefcad0a8ff.pdf
    private static readonly Dictionary<string, string[]> Coverage = new(StringComparer.OrdinalIgnoreCase) {
        ["A1"] = ["M"], ["A2"] = ["M", "A1"], ["A"] = ["M", "A1", "A2"],
        ["B1"] = ["M"], ["B"] = ["M", "B1", "F"], ["BE"] = ["M", "B", "B1", "F"],
        ["C1"] = ["M", "B", "B1", "F"], ["C1E"] = ["M", "B", "BE", "B1", "C1", "F"],
        ["C"] = ["M", "B", "B1", "C1", "F"], ["CE"] = ["M", "B", "BE", "B1", "C", "C1", "C1E", "F"],
        ["D1"] = ["M", "B", "B1", "F"], ["D1E"] = ["M", "B", "BE", "B1", "D1", "F"],
        ["D"] = ["M", "B", "B1", "D1", "F"], ["DE"] = ["M", "B", "BE", "B1", "D", "D1", "D1E", "F"], ["F"] = ["M"]
    };
    public static bool Covers(string held, string required) => held.Trim().Equals(required.Trim(), StringComparison.OrdinalIgnoreCase) ||
        Coverage.TryGetValue(held.Trim(), out var included) && included.Contains(required.Trim(), StringComparer.OrdinalIgnoreCase);
}
