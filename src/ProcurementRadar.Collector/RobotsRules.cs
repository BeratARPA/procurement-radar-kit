using System.Text.RegularExpressions;

namespace ProcurementRadar.Collector;

/// <summary>
/// The rules from one robots.txt, reduced to the group that applies to us.
/// </summary>
/// <remarks>
/// Follows RFC 9309 closely enough for the sites this project polls: groups keyed
/// by User-agent, <c>*</c> and <c>$</c> in paths, and the longest matching rule
/// wins with Allow breaking a tie.
/// </remarks>
public sealed class RobotsRules
{
    private readonly IReadOnlyList<Rule> _rules;

    private RobotsRules(IReadOnlyList<Rule> rules)
    {
        _rules = rules;
    }

    public static RobotsRules AllowAll { get; } = new([]);

    public static RobotsRules DenyAll { get; } = new([new Rule(false, "/", ToRegex("/"))]);

    public static RobotsRules Parse(string content, string userAgentToken)
    {
        var groups = new Dictionary<string, List<Rule>>(StringComparer.OrdinalIgnoreCase);
        var currentAgents = new List<string>();
        var previousLineWasAgent = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var commentStart = rawLine.IndexOf('#');
            var line = (commentStart >= 0 ? rawLine[..commentStart] : rawLine).Trim();

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (string.Equals(key, "user-agent", StringComparison.OrdinalIgnoreCase))
            {
                // Consecutive User-agent lines share one group of rules.
                if (!previousLineWasAgent)
                {
                    currentAgents.Clear();
                }

                if (value.Length > 0)
                {
                    currentAgents.Add(value);
                    groups.TryAdd(value, []);
                }

                previousLineWasAgent = true;
                continue;
            }

            previousLineWasAgent = false;

            var isAllow = string.Equals(key, "allow", StringComparison.OrdinalIgnoreCase);
            var isDisallow = string.Equals(key, "disallow", StringComparison.OrdinalIgnoreCase);

            if (!isAllow && !isDisallow)
            {
                continue;
            }

            // "Disallow:" with nothing after it is the documented way of saying
            // "everything is permitted", so it must not become a rule.
            if (value.Length == 0)
            {
                continue;
            }

            foreach (var agent in currentAgents)
            {
                groups[agent].Add(new Rule(isAllow, value, ToRegex(value)));
            }
        }

        if (groups.TryGetValue(userAgentToken, out var ours))
        {
            return new RobotsRules(ours);
        }

        return groups.TryGetValue("*", out var wildcard)
            ? new RobotsRules(wildcard)
            : AllowAll;
    }

    public bool IsAllowed(string path)
    {
        if (_rules.Count == 0)
        {
            return true;
        }

        if (path.Length == 0)
        {
            path = "/";
        }

        Rule? best = null;

        foreach (var rule in _rules)
        {
            if (!rule.Pattern.IsMatch(path))
            {
                continue;
            }

            // Longest pattern wins; Allow breaks a tie, as RFC 9309 requires.
            if (best is null
                || rule.Path.Length > best.Path.Length
                || (rule.Path.Length == best.Path.Length && rule.Allow))
            {
                best = rule;
            }
        }

        return best?.Allow ?? true;
    }

    private static Regex ToRegex(string pattern)
    {
        var anchored = pattern.EndsWith('$');
        var body = anchored ? pattern[..^1] : pattern;
        var escaped = string.Join(".*", body.Split('*').Select(Regex.Escape));

        return new Regex(
            $"^{escaped}{(anchored ? "$" : string.Empty)}",
            RegexOptions.CultureInvariant);
    }

    private sealed record Rule(bool Allow, string Path, Regex Pattern);
}
