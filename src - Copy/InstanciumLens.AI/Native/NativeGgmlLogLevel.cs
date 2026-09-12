namespace InstanciumLens.AI.Native;

/// <summary>
/// Clean enumeration for GGML log levels, decoupled from native P/Invoke definitions.
/// </summary>
public enum NativeGgmlLogLevel
{
    None = 0,
    Info = 2,
    Warning = 3,
    Error = 4,
    Debug = 5
}

/// <summary>
/// Clean enumeration for LLaMA model split modes.
/// </summary>
public enum NativeLlamaSplitMode
{
    None = 0,
    Layer = 1,
    Row = 2
}

/// <summary>
/// Clean enumeration for Flash Attention types.
/// </summary>
public enum NativeLlamaFlashAttnType
{
    Disabled = 0,
    Enabled = 1
}

/// <summary>
/// Clean enumeration for GGML data types (used for KV cache quantization).
/// </summary>
public enum NativeGgmlType
{
    F32 = 0,
    F16 = 1,
    Q4_0 = 2,
    Q4_K = 12,
    Q5_K = 13,
    Q8_0 = 8
}

/// <summary>
/// Clean delegate for native log callbacks, decoupled from NativeMethods.
/// </summary>
public delegate void NativeLogCallback(NativeGgmlLogLevel level, string message);

/// <summary>
/// Clean structure for sampler chain parameters, decoupled from native definitions.
/// </summary>
public struct NativeSamplerChainParams
{
    public bool NoPerf;
}

/// <summary>
/// Clean enumeration for LLaMA pooling types, decoupled from native definitions.
/// </summary>
public enum NativeLlamaPoolingType
{
    Unspecified = -1,
    None = 0,
    Mean = 1,
    Cls = 2,
    Last = 3,
    Rank = 4
}

