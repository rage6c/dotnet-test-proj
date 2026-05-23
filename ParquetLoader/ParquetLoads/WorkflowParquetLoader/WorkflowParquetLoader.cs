using Microsoft.Extensions.Logging;
using ParquetLoader.Configuration;

namespace ParquetLoader.ParquetLoads.WorkflowParquetLoader;

public sealed class WorkflowParquetLoader : ParquetLoader.ParquetLoads.ParquetLoader<WorkflowDto>
{
    public WorkflowParquetLoader(
        ParquetLoaderConfig config,
        ILogger<WorkflowParquetLoader> logger)
        : base(config, logger)
    {
    }

    public override string DatasetName => "workflow";

    protected override IReadOnlyDictionary<string, string> ColumnMap { get; } =
        new Dictionary<string, string>
        {
            [nameof(WorkflowDto.Id)] = "id",
            [nameof(WorkflowDto.WorkflowId)] = "workflow_id",
            [nameof(WorkflowDto.Status)] = "status",
            [nameof(WorkflowDto.CreatedOn)] = "created_on",
            [nameof(WorkflowDto.Assignee)] = "assignee",
            [nameof(WorkflowDto.UpdatedOn)] = "updated_on",
            [nameof(WorkflowDto.MonthOfYear)] = "monthOfYear"
        };
}
