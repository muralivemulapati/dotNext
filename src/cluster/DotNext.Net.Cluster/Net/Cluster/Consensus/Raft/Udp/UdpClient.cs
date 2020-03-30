using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DotNext.Net.Cluster.Consensus.Raft.Udp
{
    using static Threading.AtomicInt64;

    internal sealed class UdpClient : UdpSocket
    {
        internal abstract class Exchange<T> : TaskCompletionSource<T>, IExchange
        {
            private protected readonly long CurrentTerm;

            private protected Exchange(long term)
                : base(TaskCreationOptions.RunContinuationsAsynchronously)
                => CurrentTerm = term;

            public abstract ValueTask<bool> ProcessInbountMessageAsync(PacketHeaders headers, ReadOnlyMemory<byte> payload, EndPoint endpoint, CancellationToken token);

            public abstract ValueTask<(PacketHeaders Headers, int BytesWritten, bool)> CreateOutboundMessageAsync(Memory<byte> payload, CancellationToken token);

            private protected virtual void OnException(Exception e) { }

            void IExchange.OnException(Exception e)
            {
                if(e is OperationCanceledException cancellation ? TrySetCanceled(cancellation.CancellationToken) : TrySetException(e))
                    OnException(e);
            }

            private protected virtual void OnCanceled(CancellationToken token) { }

            void IExchange.OnCanceled(CancellationToken token)
            {
                if(TrySetCanceled(token))
                    OnCanceled(token);
            }
        }

        [StructLayout(LayoutKind.Auto)]
        private readonly struct Channel : IChannel
        {
            private readonly IExchange exchange;
            internal readonly CancellationToken Token;
            private readonly CancellationTokenRegistration cancellation;

            internal Channel(IExchange exchange, Action<object> cancellationCallback, CorrelationId id, CancellationToken token)
            {
                this.exchange = exchange;
                this.Token = token;
                cancellation = token.CanBeCanceled ? token.Register(cancellationCallback, id) : default;
            }

            CancellationToken IChannel.Token => Token;
            IExchange IChannel.Exchange => exchange;

            internal void Complete(Exception e) => exchange.OnException(e);

            public void Dispose() => cancellation.Dispose();
        }
        
        private readonly Action<object> cancellationHandler;

        //I/O management
        private readonly long applicationId;
        private long streamNumber;
        private readonly ChannelPool<Channel> channels;

        internal UdpClient(IPEndPoint address, int backlog, int datagramSize, ArrayPool<byte> bufferPool, ILoggerFactory loggerFactory)
            : base(address, backlog, datagramSize, bufferPool, loggerFactory)
        {
            channels = new ChannelPool<Channel>(backlog);
            cancellationHandler = channels.CancellationRequested;
           
            applicationId = new Random().Next<long>();
            streamNumber = long.MinValue;
        }

        private protected override void ReportError(SocketError error)
            => channels.ReportError(error);

        private protected override void EndReceive(object sender, SocketAsyncEventArgs args)
        {
            ReadOnlyMemory<byte> datagram = args.MemoryBuffer.Slice(0, args.BytesTransferred);
            //dispatch datagram to appropriate exchange
            var correlationId = new CorrelationId(ref datagram);
            if(channels.TryGetValue(correlationId, out var channel))
                ProcessDatagram(channels, channel, correlationId, new PacketHeaders(ref datagram), datagram, args);
            else
                logger.PacketDropped(correlationId, args.RemoteEndPoint);
        }

        internal void Start()
        {
            Connect(Address);
            base.Start();
        }

        internal void Stop() => Disconnect(false);

        internal async void Enqueue<TResponse>(Exchange<TResponse> exchange, CancellationToken token)
        {
            var id = new CorrelationId(applicationId, streamNumber.IncrementAndGet());
            var channel = new Channel(exchange, cancellationHandler, id, token);
            if(channels.TryAdd(id, channel))
                try
                {
                    if(!await SendAsync(id, channel, RemoteEndPoint).ConfigureAwait(false))
                        throw new NotSupportedException(ExceptionMessages.UnexpectedUdpSenderBehavior);
                }
                catch(Exception e)
                {
                    if(channels.TryRemove(id, out channel))
                        using(channel)
                            channel.Complete(e);
                }
            else 
                using(channel)
                    channel.Complete(new InvalidOperationException(ExceptionMessages.DuplicateCorrelationId));
        }

        private void Cleanup(bool disposing)
        {
            if(disposing)
            {
                channels.ClearAndDestroyChannels();
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                Cleanup(disposing);
            }
        }
    }
}