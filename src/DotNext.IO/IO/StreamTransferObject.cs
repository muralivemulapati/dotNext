﻿using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace DotNext.IO
{
    using ByteBuffer = Buffers.ArrayRental<byte>;

    /// <summary>
    /// Represents object which content is represented by <see cref="Stream"/>.
    /// </summary>
    public class StreamTransferObject : Disposable, IDataTransferObject, IAsyncDisposable
    {
        private const int DefaultBufferSize = 1024;
        private readonly bool leaveOpen;
        private readonly Stream content;

        /// <summary>
        /// Initializes a new message.
        /// </summary>
        /// <param name="content">The message content.</param>
        /// <param name="leaveOpen"><see langword="true"/> to leave the stream open after <see cref="StreamTransferObject"/> object is disposed; otherwise, <see langword="false"/>.</param>
        public StreamTransferObject(Stream content, bool leaveOpen)
        {
            this.leaveOpen = leaveOpen;
            this.content = content;
        }

        /// <summary>
        /// Loads the content from another data transfer object.
        /// </summary>
        /// <param name="source">The content source.</param>
        /// <param name="token">The token that can be used to cancel the operation.</param>
        /// <returns>The task representing asynchronous state of content loading.</returns>
        /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
        /// <exception cref="NotSupportedException">The underlying stream does not support seeking.</exception>
        public async ValueTask LoadFromAsync(IDataTransferObject source, CancellationToken token = default)
        {
            if(content.CanSeek && content.CanWrite)
                try
                {
                    await source.TransformAsync(content, DefaultBufferSize, token).ConfigureAwait(false);
                }
                finally
                {
                    content.Seek(0, SeekOrigin.Begin);
                }
            else
                throw new NotSupportedException();
        }

        /// <summary>
        /// Indicates that the content of this message can be copied to the output stream or pipe multiple times.
        /// </summary>
        public virtual bool IsReusable => content.CanSeek;

        long? IDataTransferObject.Length => content.CanSeek ? content.Length : default(long?);

        async ValueTask IDataTransferObject.TransformAsync<TWriter>(TWriter writer, CancellationToken token)
        {
            try
            {
                await writer.CopyFromAsync(content, token).ConfigureAwait(false);
            }
            finally
            {
                if(content.CanSeek)
                    content.Seek(0, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// Parses the encapsulated stream. 
        /// </summary>
        /// <param name="parser">The parser instance.</param>
        /// <param name="token">The token that can be used to cancel the operation.</param>
        /// <typeparam name="TResult">The type of result.</typeparam>
        /// <typeparam name="TDecoder">The type of parser.</typeparam>
        /// <returns>The converted DTO content.</returns>
        /// <exception cref="OperationCanceledException">The operation has been canceled.</exception>
        public async ValueTask<TResult> TransformAsync<TResult, TDecoder>(TDecoder parser, CancellationToken token = default)
            where TDecoder : IDataTransferObject.ITransformation<TResult>
        {
            const int bufferSize = 1024;
            var buffer = new ByteBuffer(bufferSize);
            try
            {
                return await parser.TransformAsync(new AsyncStreamBinaryReader(content, buffer.Memory), token).ConfigureAwait(false);
            }
            finally
            {
                buffer.Dispose();
                if(content.CanSeek)
                    content.Seek(0L, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// Releases resources associated with this object.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> if called from <see cref="Disposable.Dispose()"/>; <see langword="false"/> if called from finalizer <see cref="Disposable.Finalize()"/>.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
                content.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>
        /// Asynchronously releases the resources associated with this object.
        /// </summary>
        /// <returns>A task that represents the asynchronous dispose operation.</returns>
        public virtual ValueTask DisposeAsync()
        {
            base.Dispose(false);
            return content.DisposeAsync();
        }
    }
}
