namespace ProcurementRadar.Domain.Enums;

/// <summary>
/// Why a source is out of the rotation. Stored in
/// <c>source.deactivated_reason</c> as snake_case text.
/// </summary>
/// <remarks>
/// A closed set rather than a sentence, because the question worth asking is
/// "which of these might have healed by now" and free text cannot be asked that.
/// Before this existed the reason lived in <c>notes</c>, and finding the one
/// source that had come back meant reading twenty-four Turkish paragraphs.
///
/// <see cref="CanRecover"/> is the whole point: it splits the sources whose
/// reason is a condition that can change on its own from the ones whose reason
/// is a fact about the page.
/// </remarks>
public enum DeactivationReason
{
    /// <summary>Ten consecutive failures. Set by the health monitor, not by a person.</summary>
    HealthRule,

    /// <summary>Switched off by a person because the server was not answering.</summary>
    SiteUnreachable,

    /// <summary>Switched off by a person; the reason is in <c>notes</c>.</summary>
    Manual,

    /// <summary>The page carries no tender listing at all - it is the wrong address.</summary>
    NoTenderList,

    /// <summary>robots.txt forbids it. This one is not ours to revisit.</summary>
    Robots,

    /// <summary>Another source already collects this authority.</summary>
    Superseded
}

public static class DeactivationReasons
{
    /// <summary>
    /// Whether a source switched off for this reason is worth asking again.
    /// </summary>
    /// <remarks>
    /// Only the two conditions that live on somebody else's server. A page with
    /// no tender list does not grow one overnight, a robots rule is a decision
    /// rather than a fault, and a superseded source coming back would just
    /// duplicate the source that replaced it.
    /// </remarks>
    public static bool CanRecover(this DeactivationReason reason)
        => reason is DeactivationReason.HealthRule or DeactivationReason.SiteUnreachable;
}
