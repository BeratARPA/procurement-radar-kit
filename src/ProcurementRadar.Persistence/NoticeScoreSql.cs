namespace ProcurementRadar.Persistence;

/// <summary>Hand-written SQL for <c>notice_score</c> (D-01).</summary>
internal static class NoticeScoreSql
{
    // llm_score is absent from both the column list and the conflict branch on
    // purpose: v1 never writes it (D-03), and a later rule re-run must not
    // overwrite an answer that cost money to get.
    internal const string Upsert = """
        INSERT INTO notice_score (
            notice_id, rule_score, rationale, estimated_man_days, risk_note, model, scored_at)
        VALUES (
            @NoticeId, @RuleScore, @Rationale, @EstimatedManDays, @RiskNote, @Model, @ScoredAt)
        ON CONFLICT (notice_id) DO UPDATE
            SET rule_score = EXCLUDED.rule_score,
                rationale  = EXCLUDED.rationale,
                model      = EXCLUDED.model,
                scored_at  = EXCLUDED.scored_at
        """;
}
