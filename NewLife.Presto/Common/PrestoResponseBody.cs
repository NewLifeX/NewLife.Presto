using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if NET6_0_OR_GREATER
using System.Buffers;
#endif

namespace NewLife.Presto;

/// <summary>HTTP 响应体</summary>
/// <remarks>
/// `net6.0+` 直接从响应流读取到池化字节缓冲，交给流式 JSON 解析，避免整页文本；
/// 低版本框架保持读取字符串。错误路径才解码为文本。
/// </remarks>
internal sealed class PrestoResponseBody : IDisposable
{
    #region 字段
    private String? _text;
#if NET6_0_OR_GREATER
    private Byte[]? _buffer;
    private Int32 _length;
    private Boolean _returned;
#endif
    #endregion

    #region 构造
    private PrestoResponseBody() { }

    /// <summary>读取响应体</summary>
    /// <param name="response">HTTP 响应</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应体</returns>
    public static async Task<PrestoResponseBody> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response?.Content == null) return new PrestoResponseBody();

#if NET6_0_OR_GREATER
        // 读取循环直接写在这里，少一层异步状态机（每页都省一次装箱）
        var content = response.Content;
        var length = content.Headers.ContentLength;
        var capacity = length is > 0 and <= Int32.MaxValue ? (Int32)length.Value : 4096;
        var buffer = ArrayPool<Byte>.Shared.Rent(capacity);
        var total = 0;

        try
        {
            using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var gzip = OpenDecompression(content, stream);
            var source = gzip != null ? gzip : stream;
            while (true)
            {
                if (total == buffer.Length)
                {
                    var larger = ArrayPool<Byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, larger, 0, total);
                    ArrayPool<Byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                var read = await source.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
                if (read <= 0) break;

                total += read;
            }

            return new PrestoResponseBody { _buffer = buffer, _length = total };
        }
        catch
        {
            ArrayPool<Byte>.Shared.Return(buffer);
            throw;
        }
#else
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        _ = cancellationToken;

        return new PrestoResponseBody { _text = text };
#endif
    }
    #endregion

    #region 解析
    /// <summary>解析单页查询结果</summary>
    /// <param name="structured">复杂类型是否返回结构化对象</param>
    /// <param name="knownId">上一页的查询编号，内容相同则复用</param>
    /// <param name="knownInfoUri">上一页的查询信息地址，内容相同则复用</param>
    /// <returns>查询结果</returns>
    public PrestoQueryResults ParseResults(Boolean structured, String? knownId = null, String? knownInfoUri = null)
    {
#if NET6_0_OR_GREATER
        if (_buffer == null) return PrestoJson.ParseResults((String?)null, structured);

        return PrestoJson.ParseResultsUtf8(new ReadOnlySpan<Byte>(_buffer, 0, _length), structured, knownId, knownInfoUri);
#else
        return PrestoJson.ParseResults(_text, structured);
#endif
    }

    /// <summary>同步读取响应体</summary>
    /// <param name="response">HTTP 响应</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应体</returns>
    public static PrestoResponseBody Read(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response?.Content == null) return new PrestoResponseBody();

#if NET6_0_OR_GREATER
        var (buffer, length) = ReadBytes(response.Content, cancellationToken);

        return new PrestoResponseBody { _buffer = buffer, _length = length };
#else
        return ReadAsync(response, cancellationToken).GetAwaiter().GetResult();
#endif
    }

    /// <summary>解析分段中的行数组</summary>
    /// <returns>行列表</returns>
    public IList<IList<Object?>> ParseRows()
    {
#if NET6_0_OR_GREATER
        if (_buffer == null) return PrestoJson.ParseRows((String?)null);

        return PrestoJson.ParseRowsUtf8(new ReadOnlySpan<Byte>(_buffer, 0, _length));
#else
        return PrestoJson.ParseRows(_text);
#endif
    }

    /// <summary>解码为文本，仅用于错误信息与诊断</summary>
    /// <returns>响应文本</returns>
    public String GetText()
    {
        if (_text != null) return _text;

#if NET6_0_OR_GREATER
        if (_buffer == null) return String.Empty;

        _text = Encoding.UTF8.GetString(_buffer, 0, _length);
        return _text;
#else
        return _text ?? String.Empty;
#endif
    }
    #endregion

    #region 读取
#if NET6_0_OR_GREATER
    private static (Byte[] Buffer, Int32 Length) ReadBytes(HttpContent content, CancellationToken cancellationToken)
    {
        var length = content.Headers.ContentLength;
        var capacity = length is > 0 and <= Int32.MaxValue ? (Int32)length.Value : 4096;
        var buffer = ArrayPool<Byte>.Shared.Rent(capacity);
        var total = 0;

        try
        {
            using var stream = content.ReadAsStream(cancellationToken);
            using var gzip = OpenDecompression(content, stream)!;
            var source = gzip != null ? gzip : stream;
            while (true)
            {
                if (total == buffer.Length)
                {
                    var larger = ArrayPool<Byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, larger, 0, total);
                    ArrayPool<Byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                var read = source.Read(buffer, total, buffer.Length - total);
                if (read <= 0) break;

                total += read;
            }

            return (buffer, total);
        }
        catch
        {
            ArrayPool<Byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>按 Content-Encoding 打开解压流；未压缩返回 null</summary>
    private static Stream? OpenDecompression(HttpContent content, Stream stream)
    {
        foreach (var item in content.Headers.ContentEncoding)
        {
            if (String.Equals(item, "gzip", StringComparison.OrdinalIgnoreCase))
                return new GZipStream(stream, CompressionMode.Decompress);
            if (String.Equals(item, "deflate", StringComparison.OrdinalIgnoreCase))
                return new DeflateStream(stream, CompressionMode.Decompress);
        }

        return null;
    }
#endif
    #endregion

    #region 释放
    /// <summary>归还池化缓冲</summary>
    public void Dispose()
    {
#if NET6_0_OR_GREATER
        if (_returned) return;

        _returned = true;
        if (_buffer != null)
        {
            ArrayPool<Byte>.Shared.Return(_buffer);
            _buffer = null;
        }
#endif
    }
    #endregion
}
