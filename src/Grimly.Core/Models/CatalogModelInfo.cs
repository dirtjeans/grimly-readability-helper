namespace Grimly.Models;

/// <summary>
/// One entry from the Foundry Local model catalog. Populated from parsing
/// <c>foundry model list --available</c>. Fields are what the CLI exposes
/// as columns; anything the CLI doesn't return is null.
/// </summary>
/// <param name="Id">Full model alias / ID that <c>foundry model download</c>
/// accepts (e.g., <c>qwen2.5-7b-instruct-qnn-npu</c>).</param>
/// <param name="Device">Runtime the model was compiled for — CPU, CUDA, NPU,
/// GPU. Grimly filters on this for the "NPU only" toggle.</param>
/// <param name="SizeBytes">Model download size. Nullable because the CLI
/// occasionally omits it. Displayed as GB in the UI.</param>
/// <param name="IsCached">True if this model is already downloaded and
/// doesn't need a network fetch — grays the "Download" button.</param>
public sealed record CatalogModelInfo(
    string Id,
    string Device,
    long? SizeBytes,
    bool IsCached)
{
    public bool IsNpu => Device.Contains("npu", System.StringComparison.OrdinalIgnoreCase)
                      || Device.Contains("qnn", System.StringComparison.OrdinalIgnoreCase);

    public bool IsGpu => Device.Contains("gpu", System.StringComparison.OrdinalIgnoreCase)
                      || Device.Contains("cuda", System.StringComparison.OrdinalIgnoreCase)
                      || Device.Contains("directml", System.StringComparison.OrdinalIgnoreCase)
                      || Device.Contains("rocm", System.StringComparison.OrdinalIgnoreCase)
                      || Device.Contains("dml", System.StringComparison.OrdinalIgnoreCase);

    public string SizeDisplay => SizeBytes is long b && b > 0
        ? $"{b / (1024.0 * 1024 * 1024):0.0} GB"
        : "";
}
