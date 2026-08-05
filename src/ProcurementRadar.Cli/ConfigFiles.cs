using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Parsers;

namespace ProcurementRadar.Cli;

/// <summary>
/// Reads the data files under config/. Kept apart from everything that uses
/// them, because nothing on the parse path is allowed to touch the disk.
/// </summary>
internal sealed record ConfigFiles(
    TurkishDatePatterns DatePatterns,
    NormalizationRules Normalization,
    PersonalDataRules PersonalData)
{
    public const string DefaultDirectory = "config";

    public static ConfigFiles LoadFrom(string directory)
        => new(
            TurkishDatePatterns.Load(Read(directory, "tr-date-patterns.json")),
            NormalizationRules.Load(Read(directory, "tr-normalization.json")),
            PersonalDataRules.Load(Read(directory, "tr-personal-data.json")));

    private static string Read(string directory, string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(directory, fileName));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Config file not found at {path}. Run from the repository root, or pass --config-dir.",
                path);
        }

        return File.ReadAllText(path);
    }
}
