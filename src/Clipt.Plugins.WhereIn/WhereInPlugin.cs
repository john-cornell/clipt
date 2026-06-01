using Clipt.Plugins;

namespace Clipt.Plugins.WhereIn;

public sealed class WhereInPlugin : ICliptTrayActionPlugin
{
    public string Id => "clipt.plugins.where-in";

    public string Name => "Where In";

    public string Description =>
        "Build a SQL WHERE IN clause from multi-line clipboard text (GUIDs, one per line).";

    public IReadOnlyList<CliptPluginOption> Options { get; } =
    [
        new CliptPluginOption
        {
            Key = WhereInSqlBuilder.UseFirstLineAsColumnHeaderOptionKey,
            Label = "First line is column name",
            Kind = CliptPluginOptionKind.Checkbox,
            DefaultValue = true,
        },
    ];

    public bool CanExecute(CliptPluginContext context) =>
        WhereInSqlBuilder.HasMultipleLines(context.ClipboardText);

    public Task<CliptPluginResult> ExecuteAsync(CliptPluginContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool useHeader = context.OptionValues.TryGetValue(
            WhereInSqlBuilder.UseFirstLineAsColumnHeaderOptionKey,
            out bool value)
            && value;

        WhereInBuildResult result = WhereInSqlBuilder.Build(context.ClipboardText ?? string.Empty, useHeader);
        if (!result.Success || result.Sql is null)
            return Task.FromResult(CliptPluginResult.Fail(result.ErrorMessage ?? "Failed to build WHERE IN clause."));

        string message = result.SkippedCount > 0
            ? $"Wrote {result.GuidCount} GUID(s); skipped {result.SkippedCount} non-GUID line(s)."
            : $"Wrote {result.GuidCount} GUID(s).";

        return Task.FromResult(CliptPluginResult.Ok(result.Sql, message));
    }
}
