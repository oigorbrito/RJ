namespace RJ.Application.Benchmarking;

public sealed class EmpiricalSelectionService
{
    public EmpiricalSelectionReport Compare(
        EmpiricalTreatmentDefinition baseline,
        EmpiricalTreatmentDefinition challenger,
        IReadOnlyList<EmpiricalMetricDefinition> metrics,
        IReadOnlyList<EmpiricalCaseObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(challenger);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(observations);

        if (baseline.Kind != challenger.Kind)
        {
            throw new ArgumentException("Baseline and challenger must belong to the same treatment kind.");
        }

        if (StringComparer.Ordinal.Equals(baseline.TreatmentId, challenger.TreatmentId))
        {
            throw new ArgumentException("Baseline and challenger treatment ids must differ.");
        }

        var requiredMetrics = metrics
            .Where(metric => metric.Required)
            .ToArray();
        if (requiredMetrics.Length == 0)
        {
            throw new ArgumentException("At least one pre-specified required metric is necessary.", nameof(metrics));
        }

        var duplicateMetric = metrics
            .GroupBy(metric => metric.MetricId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateMetric is not null)
        {
            throw new ArgumentException($"Duplicate metric id '{duplicateMetric.Key}'.", nameof(metrics));
        }

        var relevant = observations
            .Where(item => StringComparer.Ordinal.Equals(item.TreatmentId, baseline.TreatmentId)
                || StringComparer.Ordinal.Equals(item.TreatmentId, challenger.TreatmentId))
            .ToArray();

        var duplicateObservation = relevant
            .GroupBy(item => (item.CaseId, item.TreatmentId))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateObservation is not null)
        {
            return Blocked(
                baseline,
                challenger,
                requiredMetrics,
                relevant,
                $"Duplicate observation for case '{duplicateObservation.Key.CaseId}' and treatment '{duplicateObservation.Key.TreatmentId}'.");
        }

        var baselineByCase = relevant
            .Where(item => StringComparer.Ordinal.Equals(item.TreatmentId, baseline.TreatmentId))
            .ToDictionary(item => item.CaseId, StringComparer.Ordinal);
        var challengerByCase = relevant
            .Where(item => StringComparer.Ordinal.Equals(item.TreatmentId, challenger.TreatmentId))
            .ToDictionary(item => item.CaseId, StringComparer.Ordinal);

        if (baselineByCase.Count == 0 || challengerByCase.Count == 0)
        {
            return Blocked(baseline, challenger, requiredMetrics, relevant, "Paired baseline and challenger observations are required.");
        }

        var allCases = baselineByCase.Keys
            .Union(challengerByCase.Keys, StringComparer.Ordinal)
            .OrderBy(caseId => caseId, StringComparer.Ordinal)
            .ToArray();

        foreach (var caseId in allCases)
        {
            if (!baselineByCase.ContainsKey(caseId) || !challengerByCase.ContainsKey(caseId))
            {
                return Blocked(baseline, challenger, requiredMetrics, relevant, $"Case '{caseId}' is not paired across both treatments.");
            }
        }

        foreach (var observation in relevant)
        {
            if (observation.Status is EmpiricalExecutionStatus.Blocked or EmpiricalExecutionStatus.NotTested)
            {
                return Blocked(
                    baseline,
                    challenger,
                    requiredMetrics,
                    relevant,
                    $"Case '{observation.CaseId}' treatment '{observation.TreatmentId}' is {observation.Status.ToString().ToUpperInvariant()}.");
            }
        }

        var baselineGateFailures = baselineByCase.Values
            .Where(HasNonCompensableFailure)
            .Select(item => item.CaseId)
            .OrderBy(caseId => caseId, StringComparer.Ordinal)
            .ToArray();
        var challengerGateFailures = challengerByCase.Values
            .Where(HasNonCompensableFailure)
            .Select(item => item.CaseId)
            .OrderBy(caseId => caseId, StringComparer.Ordinal)
            .ToArray();

        if (baselineGateFailures.Length > 0 || challengerGateFailures.Length > 0)
        {
            if (baselineGateFailures.Length == 0)
            {
                return Report(
                    baseline,
                    challenger,
                    requiredMetrics,
                    allCases,
                    EmpiricalSelectionDecision.KeepBaseline,
                    [$"Challenger failed a non-compensable gate on case(s): {string.Join(", ", challengerGateFailures)}."],
                    0,
                    0,
                    0);
            }

            if (challengerGateFailures.Length == 0)
            {
                return Report(
                    baseline,
                    challenger,
                    requiredMetrics,
                    allCases,
                    EmpiricalSelectionDecision.SelectChallenger,
                    [$"Baseline failed a non-compensable gate on case(s): {string.Join(", ", baselineGateFailures)}; challenger passed all executed non-compensable gates."],
                    0,
                    0,
                    0);
            }

            return Report(
                baseline,
                challenger,
                requiredMetrics,
                allCases,
                EmpiricalSelectionDecision.NoClearWinner,
                [
                    $"Baseline failed a non-compensable gate on case(s): {string.Join(", ", baselineGateFailures)}.",
                    $"Challenger failed a non-compensable gate on case(s): {string.Join(", ", challengerGateFailures)}."
                ],
                0,
                0,
                0);
        }

        foreach (var caseId in allCases)
        {
            foreach (var metric in requiredMetrics)
            {
                if (!baselineByCase[caseId].Measurements.ContainsKey(metric.MetricId)
                    || !challengerByCase[caseId].Measurements.ContainsKey(metric.MetricId))
                {
                    return Blocked(
                        baseline,
                        challenger,
                        requiredMetrics,
                        relevant,
                        $"Required paired metric '{metric.MetricId}' is missing for case '{caseId}'.");
                }
            }
        }

        var better = 0;
        var worse = 0;
        var equal = 0;
        foreach (var caseId in allCases)
        {
            foreach (var metric in requiredMetrics)
            {
                var baselineValue = baselineByCase[caseId].Measurements[metric.MetricId];
                var challengerValue = challengerByCase[caseId].Measurements[metric.MetricId];
                if (!double.IsFinite(baselineValue) || !double.IsFinite(challengerValue))
                {
                    return Blocked(
                        baseline,
                        challenger,
                        requiredMetrics,
                        relevant,
                        $"Required metric '{metric.MetricId}' for case '{caseId}' must be finite.");
                }

                var relation = CompareCoordinate(baselineValue, challengerValue, metric.Direction);
                if (relation > 0)
                {
                    better++;
                }
                else if (relation < 0)
                {
                    worse++;
                }
                else
                {
                    equal++;
                }
            }
        }

        if (better > 0 && worse == 0)
        {
            return Report(
                baseline,
                challenger,
                requiredMetrics,
                allCases,
                EmpiricalSelectionDecision.SelectChallenger,
                ["Challenger Pareto-dominates the baseline across every pre-specified required case×metric coordinate."],
                better,
                worse,
                equal);
        }

        if (worse > 0 && better == 0)
        {
            return Report(
                baseline,
                challenger,
                requiredMetrics,
                allCases,
                EmpiricalSelectionDecision.KeepBaseline,
                ["Baseline Pareto-dominates the challenger across every pre-specified required case×metric coordinate."],
                better,
                worse,
                equal);
        }

        return Report(
            baseline,
            challenger,
            requiredMetrics,
            allCases,
            EmpiricalSelectionDecision.NoClearWinner,
            better == 0 && worse == 0
                ? ["All pre-specified required paired measurements are equal; evidence does not distinguish treatments."]
                : ["Observed trade-offs prevent Pareto dominance; no weighted score or post-hoc tie-break is permitted."],
            better,
            worse,
            equal);
    }

    private static bool HasNonCompensableFailure(EmpiricalCaseObservation observation) =>
        observation.Status == EmpiricalExecutionStatus.Fail
        || observation.FailedNonCompensableGates.Count > 0;

    private static int CompareCoordinate(double baseline, double challenger, EmpiricalMetricDirection direction)
    {
        var comparison = challenger.CompareTo(baseline);
        return direction == EmpiricalMetricDirection.HigherIsBetter
            ? comparison
            : -comparison;
    }

    private static EmpiricalSelectionReport Blocked(
        EmpiricalTreatmentDefinition baseline,
        EmpiricalTreatmentDefinition challenger,
        IReadOnlyList<EmpiricalMetricDefinition> metrics,
        IReadOnlyList<EmpiricalCaseObservation> observations,
        string reason)
    {
        var caseIds = observations
            .Select(item => item.CaseId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        return Report(
            baseline,
            challenger,
            metrics,
            caseIds,
            EmpiricalSelectionDecision.Blocked,
            [reason],
            0,
            0,
            0);
    }

    private static EmpiricalSelectionReport Report(
        EmpiricalTreatmentDefinition baseline,
        EmpiricalTreatmentDefinition challenger,
        IReadOnlyList<EmpiricalMetricDefinition> metrics,
        IReadOnlyList<string> caseIds,
        EmpiricalSelectionDecision decision,
        IReadOnlyList<string> reasons,
        int better,
        int worse,
        int equal) =>
        new(
            baseline.Kind,
            baseline.TreatmentId,
            challenger.TreatmentId,
            decision,
            reasons,
            caseIds,
            metrics.Select(item => item.MetricId).ToArray(),
            better,
            worse,
            equal);
}
