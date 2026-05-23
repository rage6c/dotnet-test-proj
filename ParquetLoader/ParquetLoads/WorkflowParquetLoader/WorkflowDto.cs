namespace ParquetLoader.ParquetLoads.WorkflowParquetLoader;

public sealed class WorkflowDto
{
    public int? Id { get; set; }

    public int? WorkflowId { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedOn { get; set; }

    public string? Assignee { get; set; }

    public DateTime? UpdatedOn { get; set; }

    public long? MonthOfYear { get; set; }
}
