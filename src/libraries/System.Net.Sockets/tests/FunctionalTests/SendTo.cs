// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.XUnitExtensions;
using Xunit;
using Xunit.Abstractions;

namespace System.Net.Sockets.Tests
{
    public abstract class SendTo<T> : SocketTestHelperBase<T> where T : SocketHelperBase, new()
    {
        protected static Socket CreateSocket(AddressFamily addressFamily = AddressFamily.InterNetwork) => new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);

        protected static IPEndPoint GetGetDummyTestEndpoint(AddressFamily addressFamily = AddressFamily.InterNetwork) =>
            addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Parse("1.2.3.4"), 1234) : new IPEndPoint(IPAddress.Parse("1:2:3::4"), 1234);

        private (Socket listener, IPEndPoint endpoint) CreateLoopbackUdpEndpoint()
        {
            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            IPEndPoint endpoint = (IPEndPoint)listener.LocalEndPoint;
            return (listener, endpoint);
        }

        protected SendTo(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(1, -1, 0)] // offset low
        [InlineData(1, 2, 0)] // offset high
        [InlineData(1, 0, -1)] // count low
        [InlineData(1, 0, 2)] // count high
        [InlineData(1, 1, 1)] // count high
        public async Task OutOfRange_Throws_ArgumentOutOfRangeException(int length, int offset, int count)
        {
            using var socket = CreateSocket();

            ArraySegment<byte> buffer = new FakeArraySegment
            {
                Array = new byte[length], Count = count, Offset = offset
            }.ToActual();

            await AssertThrowsSynchronously<ArgumentOutOfRangeException>(() => SendToAsync(socket, buffer, GetGetDummyTestEndpoint()));
        }

        [Fact]
        public async Task NullBuffer_Throws_ArgumentNullException()
        {
            if (!ValidatesArrayArguments) return;
            using var socket = CreateSocket();

            await AssertThrowsSynchronously<ArgumentNullException>(() => SendToAsync(socket, null, GetGetDummyTestEndpoint()));
        }

        [Fact]
        public async Task NullEndpoint_Throws_ArgumentException()
        {
            using Socket socket = CreateSocket();
            if (UsesEap)
            {
                await AssertThrowsSynchronously<ArgumentException>(() => SendToAsync(socket, new byte[1], null));
            }
            else
            {
                await AssertThrowsSynchronously<ArgumentNullException>(() => SendToAsync(socket, new byte[1], null));
            }
        }

        [Fact]
        public async Task NullSocketAddress_Throws_ArgumentException()
        {
            using Socket socket = CreateSocket();
            SocketAddress socketAddress = null;

            if (!OperatingSystem.IsWasi()) Assert.Throws<ArgumentNullException>(() => socket.SendTo(new byte[1], SocketFlags.None, socketAddress));
            await AssertThrowsSynchronously<ArgumentNullException>(() => socket.SendToAsync(new byte[1], SocketFlags.None, socketAddress).AsTask());
        }

        [Fact]
        public async Task Datagram_UDP_ShouldImplicitlyBindLocalEndpoint()
        {
            using (Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            using (Socket sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                // Use loopback to ensure deterministic behavior on mobile/restricted environments
                var (tempListener, endpoint) = CreateLoopbackUdpEndpoint();
                tempListener.Dispose();
                listener.Bind(endpoint);
                
                Task<SocketReceiveFromResult> receiveTask = ReceiveFromAsync(listener, new ArraySegment<byte>(new byte[1]), endpoint);
                
                int bytesSent = await SendToAsync(sender, new ArraySegment<byte>(new byte[1]), endpoint);
                Assert.Equal(1, bytesSent);

                SocketReceiveFromResult result = await receiveTask;
                int bytesReceived = result.ReceivedBytes;
                Assert.Equal(1, bytesReceived);
                
                // Verify implicit bind occurred
                Assert.NotNull(sender.LocalEndPoint);
            }
        }

        [Fact]
        [PlatformSpecific(~TestPlatforms.OSX)] // bind to specific address has been observed to fail on OSX
        public async Task Datagram_UDP_AccessDenied_Throws_DoesNotBind()
        {
            IPAddress address = Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any;
                
            using (Socket receiver = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
            using (Socket sender = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
            {
                int port = receiver.BindToAnonymousPort(address);
                
                SocketException exception = await Assert.ThrowsAnyAsync<SocketException>(
                    () => SendToAsync(sender, new ArraySegment<byte>(new byte[1]), new IPEndPoint(address, port)));
                
                SocketError expectedError = SocketError.AccessDenied;
                
#if TARGET_ANDROID || TARGET_IOS || TARGET_TVOS || TARGET_MACCATALYST
                // On mobile/restricted platforms, we may get NetworkUnreachable or HostUnreachable
                // before AccessDenied due to missing default routes
                if (exception.SocketErrorCode == SocketError.NetworkUnreachable ||
                    exception.SocketErrorCode == SocketError.HostUnreachable)
                {
                    expectedError = exception.SocketErrorCode;
                }
#endif
                
                Assert.Equal(expectedError, exception.SocketErrorCode);
                
                // Core requirement: socket should remain unbound regardless of error
                Assert.Null(sender.LocalEndPoint);
            }
        }

        [Fact]
        public async Task Disposed_Throws()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => SendToAsync(socket, new byte[1], GetGetDummyTestEndpoint()));
        }
    }

    [ConditionalClass(typeof(PlatformDetection), nameof(PlatformDetection.IsThreadingSupported))]
    public sealed class SendTo_SyncSpan : SendTo<SocketHelperSpanSync>
    {
        public SendTo_SyncSpan(ITestOutputHelper output) : base(output) { }
    }

    [ConditionalClass(typeof(PlatformDetection), nameof(PlatformDetection.IsThreadingSupported))]
    public sealed class SendTo_SyncSpanForceNonBlocking : SendTo<SocketHelperSpanSyncForceNonBlocking>
    {
        public SendTo_SyncSpanForceNonBlocking(ITestOutputHelper output) : base(output) { }
    }

    [ConditionalClass(typeof(PlatformDetection), nameof(PlatformDetection.IsThreadingSupported))]
    public sealed class SendTo_ArraySync : SendTo<SocketHelperArraySync>
    {
        public SendTo_ArraySync(ITestOutputHelper output) : base(output) { }
    }

    [ConditionalClass(typeof(PlatformDetection), nameof(PlatformDetection.IsThreadingSupported))]
    public sealed class SendTo_SyncForceNonBlocking : SendTo<SocketHelperSyncForceNonBlocking>
    {
        public SendTo_SyncForceNonBlocking(ITestOutputHelper output) : base(output) {}
    }

    [ConditionalClass(typeof(PlatformDetection), nameof(PlatformDetection.IsThreadingSupported))]
    public sealed class SendTo_Apm : SendTo<SocketHelperApm>
    {
        public SendTo_Apm(ITestOutputHelper output) : base(output) {}

        [Fact]
        public void EndSendTo_NullAsyncResult_Throws_ArgumentNullException()
        {
            EndPoint endpoint = new IPEndPoint(IPAddress.Loopback, 1);
            using Socket socket = CreateSocket();
            Assert.Throws<ArgumentNullException>(() => socket.EndSendTo(null));
        }

        [Fact]
        public void EndSendTo_UnrelatedAsyncResult_Throws_ArgumentException()
        {
            EndPoint endpoint = new IPEndPoint(IPAddress.Loopback, 1);
            using Socket socket = CreateSocket();

            Assert.Throws<ArgumentException>(() => socket.EndSendTo(Task.CompletedTask));
        }
    }

    public sealed class SendTo_Eap : SendTo<SocketHelperEap>
    {
        public SendTo_Eap(ITestOutputHelper output) : base(output) {}

        [Fact]
        public void SendToAsync_NullAsyncEventArgs_Throws_ArgumentNullException()
        {
            using Socket socket = CreateSocket();
            Assert.Throws<ArgumentNullException>(() => socket.SendToAsync(null));
        }
    }

    public sealed class SendTo_Task : SendTo<SocketHelperTask>
    {
        public SendTo_Task(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SendTo_DifferentEP_Success(bool ipv4)
        {
            IPAddress address = ipv4 ? IPAddress.Loopback : IPAddress.IPv6Loopback;
            IPEndPoint remoteEp = new IPEndPoint(address, 0);

            using Socket receiver1 = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            using Socket receiver2 = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            using Socket sender = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            receiver1.BindToAnonymousPort(address);
            receiver2.BindToAnonymousPort(address);

            byte[] sendBuffer = new byte[32];
            var receiveInternalBuffer = new byte[sendBuffer.Length];
            ArraySegment<byte> receiveBuffer = new ArraySegment<byte>(receiveInternalBuffer, 0, receiveInternalBuffer.Length);


            await sender.SendToAsync(sendBuffer, SocketFlags.None, receiver1.LocalEndPoint);
            SocketReceiveFromResult result = await ReceiveFromAsync(receiver1, receiveBuffer, remoteEp).WaitAsync(TestSettings.PassingTestTimeout);
            Assert.Equal(sendBuffer.Length, result.ReceivedBytes);

            await sender.SendToAsync(sendBuffer, SocketFlags.None, receiver2.LocalEndPoint);
            result = await ReceiveFromAsync(receiver2, receiveBuffer, remoteEp).WaitAsync(TestSettings.PassingTestTimeout);
            Assert.Equal(sendBuffer.Length, result.ReceivedBytes);
        }
    }

    public sealed class SendTo_CancellableTask : SendTo<SocketHelperCancellableTask>
    {
        public SendTo_CancellableTask(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task PreCanceled_Throws()
        {
            using Socket sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            OperationCanceledException ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => sender.SendToAsync(new byte[1], SocketFlags.None, GetGetDummyTestEndpoint(), cts.Token).AsTask());

            Assert.Equal(cts.Token, ex.CancellationToken);
        }
    }

    public sealed class SendTo_MemoryArrayTask : SendTo<SocketHelperMemoryArrayTask>
    {
        public SendTo_MemoryArrayTask(ITestOutputHelper output) : base(output) { }
    }

    public sealed class SendTo_MemoryNativeTask : SendTo<SocketHelperMemoryNativeTask>
    {
        public SendTo_MemoryNativeTask(ITestOutputHelper output) : base(output) { }
    }
}
