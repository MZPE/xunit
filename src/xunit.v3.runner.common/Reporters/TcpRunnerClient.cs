using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit.v3;

namespace Xunit.Runner.Common
{
	/// <summary>
	/// A client used by <see cref="TcpRunnerReporterMessageHandler"/> to handle TCP communication to the remote
	/// side of a v3 protocol TCP connection (for example, a meta-runner written using xunit.v3.runner.utility).
	/// </summary>
	public class TcpRunnerClient
	{
		readonly BufferedTcpClient bufferedClient;
		bool cancelRequested = false;
		readonly IRunnerLogger logger;
		readonly int port;
		readonly Socket socket;

		/// <summary>
		/// Initializes a new instance of the <see cref="TcpRunnerClient"/> class.
		/// </summary>
		/// <param name="logger">The logger used to log messages.</param>
		/// <param name="port">The TCP port to connect to (localhost is assumed).</param>
		public TcpRunnerClient(
			IRunnerLogger logger,
			int port)
		{
			this.logger = logger;
			this.port = port;

			socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
			bufferedClient = new BufferedTcpClient(socket, ProcessResponse);
		}

		void ProcessResponse(string command)
		{
			switch (command)
			{
				case "STOP":
					cancelRequested = true;
					break;

				default:
					logger.LogWarning($"Received unknown command: {command}");
					break;
			}
		}

		/// <summary>
		/// Queues a message for transmission to the remote runner.
		/// </summary>
		/// <param name="message">The message to be queued.</param>
		/// <returns>Returns <c>true</c> if the runner should continue to run tests; <c>false</c> if it should cancel the run.</returns>
		public bool QueueMessage(_MessageSinkMessage message)
		{
			bufferedClient.Send("MESG ");
			bufferedClient.Send(message.Serialize());
			bufferedClient.Send("\n");

			return !cancelRequested;
		}

		/// <summary/>
		public async ValueTask Start()
		{
			logger.LogMessage($"Connecting to tcp://localhost:{port}/");

			await socket.ConnectAsync(IPAddress.Loopback, port);
			bufferedClient.Start();
		}

		/// <summary/>
		public async ValueTask Stop()
		{
			logger.LogMessage("Disconnecting");

			await bufferedClient.DisposeAsync();

			socket.Shutdown(SocketShutdown.Receive);
			socket.Shutdown(SocketShutdown.Send);
			socket.Close();
			socket.Dispose();

			logger.LogMessage("Disconnected");
		}
	}
}
