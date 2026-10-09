using System.Globalization;

namespace GymQ.Services;

public partial class FaultReportService
{
    internal long NextReportNumber => Interlocked.Read(ref _nextReportId);

    internal FaultReport[] ExportState() => _reports.Values.Select(CloneReport).ToArray();

    // GymSession keeps these same report objects so later staff reviews update both views.
    internal IReadOnlyList<FaultReport> ReadAllReports() => _reports.Values.ToArray();

    internal void RestoreState(IEnumerable<FaultReport> reports, long nextReportNumber)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var restored = reports.Select(CloneReport).ToArray();
        var highestReportNumber = 0L;
        foreach (var report in restored)
        {
            if (report.ReportId.StartsWith("R-", StringComparison.Ordinal) &&
                long.TryParse(report.ReportId.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                highestReportNumber = Math.Max(highestReportNumber, number);
        }

        _reports.Clear();
        foreach (var report in restored)
            _reports[report.ReportId] = report;
        Interlocked.Exchange(ref _nextReportId, Math.Max(nextReportNumber, highestReportNumber));
    }

    private static FaultReport CloneReport(FaultReport report) => new()
    {
        ReportId = report.ReportId,
        EquipmentId = report.EquipmentId,
        SubmittedByMemberId = report.SubmittedByMemberId,
        Description = report.Description,
        Status = report.Status,
        SubmittedAt = report.SubmittedAt,
        ReviewedByStaffId = report.ReviewedByStaffId,
        ReviewedAt = report.ReviewedAt
    };
}
