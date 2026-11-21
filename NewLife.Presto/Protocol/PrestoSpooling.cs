using System.Collections.Generic;
using System.Text;

namespace NewLife.Presto;

/// <summary>Spooling 协议辅助</summary>
/// <remarks>
/// 零第三方依赖约束下只支持未压缩的 <c>json</c> 编码；服务端不提供该编码时会自动回退直接协议，
/// 因此驱动只声明 <c>json</c> 不会影响既有集群。
/// </remarks>
internal static class PrestoSpooling
{
    #region 常量
    /// <summary>支持的编码列表，按优先级排列</summary>
    public const String SupportedEncodings = "json";

    /// <summary>内嵌分段类型</summary>
    public const String InlineType = "inline";

    /// <summary>对象存储分段类型</summary>
    public const String SpooledType = "spooled";
    #endregion

    #region 方法
    /// <summary>判断数据编码是否受支持</summary>
    /// <param name="encoding">服务端选择的编码</param>
    /// <returns>是否支持</returns>
    public static Boolean IsSupported(String? encoding) => !encoding.IsNullOrEmpty() && encoding.EqualIgnoreCase(SupportedEncodings);

    /// <summary>解码内嵌分段</summary>
    /// <param name="segment">分段</param>
    /// <returns>行列表</returns>
    public static IList<IList<Object?>> DecodeInline(PrestoQuerySegment segment)
    {
        if (segment.Data == null) throw InvalidSegment(segment.Type);

        Byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(segment.Data);
        }
        catch (FormatException ex)
        {
            throw new PrestoException(Localization.Translate("Failed to parse the server response: {0}", ex.Message), ex);
        }

#if NET6_0_OR_GREATER
        return PrestoJson.ParseRowsUtf8(bytes);
#else
        return PrestoJson.ParseRows(Encoding.UTF8.GetString(bytes));
#endif
    }

    /// <summary>构造不支持的分段异常</summary>
    /// <param name="type">分段类型</param>
    /// <returns>异常</returns>
    public static PrestoException InvalidSegment(String? type)
        => new(Localization.Translate("Unsupported spooled segment type: {0}.", type ?? String.Empty));
    #endregion
}
