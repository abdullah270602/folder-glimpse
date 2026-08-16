namespace FolderGlimpse.Core.Application;

public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim().TrimStart('v', 'V');
        var build = text.IndexOf('+');
        if (build >= 0) text = text[..build];
        var dash = text.IndexOf('-');
        var core = dash >= 0 ? text[..dash] : text;
        var prerelease = dash >= 0 ? text[(dash + 1)..] : null;
        var parts = core.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch) || major < 0 || minor < 0 || patch < 0 ||
            dash >= 0 && string.IsNullOrWhiteSpace(prerelease)) return false;
        version = new(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core == 0) core = Minor.CompareTo(other.Minor);
        if (core == 0) core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        var left = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            if (index >= left.Length) return -1;
            if (index >= right.Length) return 1;
            var leftNumeric = int.TryParse(left[index], out var leftNumber);
            var rightNumeric = int.TryParse(right[index], out var rightNumber);
            var comparison = leftNumeric && rightNumeric ? leftNumber.CompareTo(rightNumber) :
                leftNumeric ? -1 : rightNumeric ? 1 : string.CompareOrdinal(left[index], right[index]);
            if (comparison != 0) return comparison;
        }
        return 0;
    }
}
