using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;
using Xunit.Runner.Common;
using Xunit.Sdk;
using Xunit.v3;

public class TcpRunnerClientTests
{
	[Fact]
	public async ValueTask InvalidPortThrows()
	{
		var logger = new SpyRunnerLogger();
		var client = new TcpRunnerClient(logger, -1);

		var ex = await Record.ExceptionAsync(() => client.Start());

		Assert.NotNull(ex);
		var argEx = Assert.IsType<ArgumentOutOfRangeException>(ex);
		Assert.StartsWith("Specified argument was out of the range of valid values.", ex.Message);
		Assert.Equal("port", argEx.ParamName);
	}

	[Fact]
	public async ValueTask MessageSentIsReceived()
	{
		var logger = new SpyRunnerLogger();
		var server = new TcpServer();
		var port = server.Start();
		try
		{
			var client = new TcpRunnerClient(logger, port);

			await client.Start();
			client.QueueMessage(new _MessageSinkMessage());
			await client.Stop();
		}
		finally
		{
			await server.DisposeAsync();
		}

		var line = Assert.Single(server.ReadLines);
		Assert.Equal("MESG {\"$type\":\"_MessageSinkMessage\"}", line);
	}

	[Fact]
	public async ValueTask ContinueRunningIsTrueByDefault()
	{
		var logger = new SpyRunnerLogger();
		var server = new TcpServer();
		var port = server.Start();
		bool? result = null;
		try
		{
			var client = new TcpRunnerClient(logger, port);
			await client.Start();

			result = client.QueueMessage(new _MessageSinkMessage());

			await client.Stop();
		}
		finally
		{
			await server.DisposeAsync();
		}

		Assert.True(result);
	}

	[Fact]
	public async ValueTask ContinueRunningIsFalseAfterStopIsSent()
	{
		var logger = new SpyRunnerLogger();
		var server = new TcpServer();
		var port = server.Start();
		bool? result = null;
		try
		{
			var client = new TcpRunnerClient(logger, port);
			await client.Start();

			server.Send("STOP\n");

			// Loop for a few seconds sending messages, waiting for it to finally return false
			for (var count = 0; count < 30; ++count)
			{
				result = client.QueueMessage(new _MessageSinkMessage());
				if (result == false)
					break;
				await Task.Delay(100);
			}

			await client.Stop();
		}
		finally
		{
			await server.DisposeAsync();
		}

		Assert.False(result);
	}

	[Fact]
	public async ValueTask UnknownCommandIsLogged()
	{
		var logger = new SpyRunnerLogger();
		var server = new TcpServer();
		var port = server.Start();
		var found = false;
		try
		{
			var client = new TcpRunnerClient(logger, port);
			await client.Start();

			server.Send("UNKNOWN\n");

			// Loop for a few seconds sending messages, waiting for the message to show up
			for (var count = 0; count < 30; ++count)
			{
				if (logger.Messages.Contains("[Wrn] => Received unknown command: UNKNOWN"))
				{
					found = true;
					break;
				}

				await Task.Delay(100);
			}

			await client.Stop();
		}
		finally
		{
			await server.DisposeAsync();
		}

		Assert.True(found);
	}

	class TcpServer : IAsyncDisposable
	{
		readonly List<Action> cleanupTasks = new();
		readonly DisposalTracker disposalTracker = new();

		public readonly ConcurrentQueue<string> ReadLines = new();

		public async ValueTask DisposeAsync()
		{
			foreach (var cleanupTask in cleanupTasks)
				cleanupTask();

			await disposalTracker.DisposeAsync();
		}

		public void Send(string text)
		{
			// Super cheating :p
			foreach (var bufferedClient in disposalTracker.AsyncDisposables.OfType<BufferedTcpClient>())
				bufferedClient.Send(text);
		}

		public int Start()
		{
			var listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
			disposalTracker.Add(listenSocket);
			cleanupTasks.Add(() => listenSocket.Close());

			listenSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
			listenSocket.Listen(1);

			Task.Run(async () =>
			{
				var socket = await listenSocket.AcceptAsync();
				disposalTracker.Add(socket);
				cleanupTasks.Add(() => socket.Close());

				var bufferedClient = new BufferedTcpClient(socket, msg => ReadLines.Enqueue(msg));
				disposalTracker.Add(bufferedClient);
				bufferedClient.Start();
			});

			return ((IPEndPoint)listenSocket.LocalEndPoint!).Port;
		}
	}
}
