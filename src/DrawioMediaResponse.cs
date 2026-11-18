using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Drawio;

/// <summary>Formats the response produced by the site's regular media pipeline, including its access checks.</summary>
internal static class DrawioMediaResponse
{
    internal static async Task Serve(HttpContext context, RequestDelegate next, bool base64)
    {
        var response = context.Response;
        var destination = response.Body;
        var cancel = context.RequestAborted;
        // Request the full identity representation. Ranges and validators for the original file do not describe Base64.
        foreach (var header in new[] { "Range", "If-Range", "If-None-Match", "If-Modified-Since", "Accept-Encoding" })
            context.Request.Headers.Remove(header);
        await using var buffer = new FileBufferingWriteStream();
        using var limited = new LimitedWriter(buffer, response);
        response.Body = limited;
        try
        {
            await next(context);
        }
        catch (DiagramTooLargeException)
        {
            response.Clear();
            response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        finally
        {
            response.Body = destination;
        }

        if (response.StatusCode != StatusCodes.Status200OK)
        {
            // Preserve challenges, redirects and errors from the canonical media URL.
            await buffer.DrainBufferAsync(destination, cancel);
            return;
        }
        var head = HttpMethods.IsHead(context.Request.Method);
        var size = head ? response.ContentLength : buffer.Length;
        if (size > DrawioPaths.MaxBase64Bytes)
        {
            response.Clear();
            response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        response.Headers.Remove("Accept-Ranges");
        response.Headers.Remove("ETag");
        response.Headers.Remove("Last-Modified");
        response.Headers.CacheControl = "private, no-store";
        response.Headers.XContentTypeOptions = "nosniff";
        response.ContentType = base64 ? "text/plain" : "application/octet-stream";
        response.ContentLength = base64 ? 4 * ((size + 2) / 3) : size;
        if (head) return;
        if (base64)
        {
            using var transform = new ToBase64Transform();
            await using var output = new CryptoStream(destination, transform, CryptoStreamMode.Write, leaveOpen: true);
            await buffer.DrainBufferAsync(output, cancel);
            await output.FlushFinalBlockAsync(cancel);
        }
        else await buffer.DrainBufferAsync(destination, cancel);
    }

    private sealed class DiagramTooLargeException : Exception;

    // FileBufferingWriteStream spills larger responses to disk. Check before each write, including chunked responses.
    private sealed class LimitedWriter(Stream buffer, HttpResponse response) : Stream
    {
        private void Check(int count)
        {
            if (buffer.Length + count > DrawioPaths.MaxBase64Bytes || response.ContentLength > DrawioPaths.MaxBase64Bytes)
                throw new DiagramTooLargeException();
        }
        public override void Write(byte[] bytes, int offset, int count)
        {
            Check(count);
            buffer.Write(bytes, offset, count);
        }
        public override Task WriteAsync(byte[] bytes, int offset, int count, CancellationToken cancel) =>
            WriteAsync(bytes.AsMemory(offset, count), cancel).AsTask();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancel = default)
        {
            Check(bytes.Length);
            return buffer.WriteAsync(bytes, cancel);
        }
        public override void Flush() => buffer.Flush();
        public override Task FlushAsync(CancellationToken cancel) => buffer.FlushAsync(cancel);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => buffer.Length;
        public override long Position { get => Length; set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
    }
}
