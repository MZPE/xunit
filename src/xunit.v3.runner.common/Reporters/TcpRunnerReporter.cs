using System.Threading.Tasks;
using Xunit.Sdk;
using Xunit.v3;

namespace Xunit.Runner.Common
{
	/// <summary>
	/// An implementation of <see cref="IRunnerReporter"/> which sends v3 protocol messages across
	/// a TCP connection. Used by meta-runners (via xunit.v3.runner.utility) when launching v3
	/// test projects.
	/// </summary>
	[HiddenRunnerReporter]
	public class TcpRunnerReporter : IRunnerReporter
	{
		readonly DisposalTracker disposalTracker = new DisposalTracker();
		readonly int tcpPort;

		/// <summary>
		/// Initializes a new instance of the <see cref="TcpRunnerReporter"/> class.
		/// </summary>
		/// <param name="tcpPort"></param>
		public TcpRunnerReporter(int tcpPort)
		{
			this.tcpPort = tcpPort;
		}

		/// <inheritdoc/>
		public string Description => string.Empty;

		/// <inheritdoc/>
		public bool IsEnvironmentallyEnabled => false;

		/// <inheritdoc/>
		public string? RunnerSwitch => null;

		/// <inheritdoc/>
		public async ValueTask<_IMessageSink> CreateMessageHandler(IRunnerLogger logger)
		{
			var client = new TcpRunnerClient(logger, tcpPort);

			await client.Start();

			var handler = new TcpRunnerReporterMessageHandler(client);
			disposalTracker.Add(handler);

			return handler;
		}

		/// <inheritdoc/>
		public ValueTask DisposeAsync() =>
			disposalTracker.DisposeAsync();
	}
}
