using ProcurementRadar.Collector;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Parsers;
using ProcurementRadar.Scoring;

namespace ProcurementRadar.Cli;

/// <summary>
/// Reads the data files under config/. Kept apart from everything that uses
/// them, because nothing on the parse path is allowed to touch the disk.
/// </summary>
internal sealed record ConfigFiles(
    TurkishDatePatterns DatePatterns,
    NormalizationRules Normalization,
    PersonalDataRules PersonalData,
    KeywordRules Keywords)
{
    public static ConfigFiles LoadFrom(string? directory)
    {
        var resolved = ConfigDirectory.Find(directory);

        return Load(resolved);
    }

    private static ConfigFiles Load(string directory)
        => new(
            TurkishDatePatterns.Load(Read(directory, "tr-date-patterns.json")),
            NormalizationRules.Load(Read(directory, "tr-normalization.json")),
            PersonalDataRules.Load(Read(directory, "tr-personal-data.json")),
            KeywordRules.Load(Read(directory, "keywords.json")));

    private static string Read(string directory, string fileName)
        => ConfigDirectory.Read(directory, fileName);
}
