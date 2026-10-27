namespace PipelineForge.Core.Metrics;

public sealed record DeployRecord(string Environment, DateTimeOffset? CommitTime, DateTimeOffset FinishedAt, bool Success);

public sealed record DailyCount(DateOnly Day, int Successes, int Failures);

public sealed record EnvironmentMetrics(
    string Environment,
    int Deploys,
    int Failures,
    double DeploysPerWeek,
    double? ChangeFailureRate,
    double? MedianLeadTimeHours,
    double? MedianTimeToRestoreHours,
    IReadOnlyList<DailyCount> Daily);

/// <summary>
/// The four delivery measures (deploy frequency, lead time for changes, change failure rate, time to restore)
/// per environment over a window, from the deploy events the pipelines report.
/// </summary>
public static class DeliveryMetrics
{
    public static IReadOnlyList<EnvironmentMetrics> Compute(IEnumerable<DeployRecord> events, DateTimeOffset now, int days)
    {
        var from = now.AddDays(-days);
        var firstDay = DateOnly.FromDateTime(from.UtcDateTime).AddDays(1);
        var result = new List<EnvironmentMetrics>();
        foreach (var group in events.Where(e => e.FinishedAt > from && e.FinishedAt <= now).GroupBy(e => e.Environment).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var list = group.OrderBy(e => e.FinishedAt).ToList();
            var failures = list.Count(e => !e.Success);
            var leadTimes = list.Where(e => e.Success && e.CommitTime is { } t && t <= e.FinishedAt)
                .Select(e => (e.FinishedAt - e.CommitTime!.Value).TotalHours).ToList();

            // Time to restore: from a failed deploy to the next successful one in the same environment.
            var restores = new List<double>();
            DateTimeOffset? brokenSince = null;
            foreach (var e in list)
            {
                if (!e.Success) brokenSince ??= e.FinishedAt;
                else if (brokenSince is { } since)
                {
                    restores.Add((e.FinishedAt - since).TotalHours);
                    brokenSince = null;
                }
            }

            var byDay = list.GroupBy(e => DateOnly.FromDateTime(e.FinishedAt.UtcDateTime)).ToDictionary(g => g.Key, g => g.ToList());
            var daily = Enumerable.Range(0, days)
                .Select(i => firstDay.AddDays(i))
                .Select(d => byDay.TryGetValue(d, out var es) ? new DailyCount(d, es.Count(x => x.Success), es.Count(x => !x.Success)) : new DailyCount(d, 0, 0))
                .ToList();

            result.Add(new EnvironmentMetrics(
                group.Key,
                list.Count,
                failures,
                Math.Round(list.Count(e => e.Success) / (days / 7.0), 2),
                list.Count == 0 ? null : Math.Round((double)failures / list.Count, 3),
                Median(leadTimes),
                Median(restores),
                daily));
        }
        return result;
    }

    private static double? Median(List<double> values)
    {
        if (values.Count == 0) return null;
        values.Sort();
        var mid = values.Count / 2;
        var m = values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
        return Math.Round(m, 2);
    }
}
