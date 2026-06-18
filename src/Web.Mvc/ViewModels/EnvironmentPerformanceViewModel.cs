namespace Stocky.Web.Mvc.ViewModels;

public sealed class EnvironmentPerformanceViewModel
{
    public required string EnvironmentName { get; init; }
    public required string MachineName { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required string Uptime { get; init; }
    public required double WorkingSetMb { get; init; }
    public required double GcHeapMb { get; init; }
    public required int ThreadCount { get; init; }
    public required int HealthyServiceCount { get; init; }
    public required int TotalServiceCount { get; init; }
    public required List<ServicePerformanceRow> Services { get; init; }
}

public sealed class ServicePerformanceRow
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public required bool IsHealthy { get; init; }
    public required int StatusCode { get; init; }
    public required long ResponseTimeMs { get; init; }
    public required string Details { get; init; }
}
