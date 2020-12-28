using System;
using System.Threading.Tasks;
using Xunit.Internal;
using Xunit.v3;

namespace Xunit.Runner.Common
{
	/// <summary>
	/// An implementation of <see cref="_IMessageSink" /> that supports <see cref="TcpRunnerReporter" />.
	/// </summary>
	public class TcpRunnerReporterMessageHandler : _IMessageSink, IAsyncDisposable
	{
		readonly TcpRunnerClient client;

		/// <summary>
		/// Initializes a new instance of the <see cref="TcpRunnerReporterMessageHandler"/> class.
		/// </summary>
		/// <param name="client">The TCP client to send the v3 protocol messages to.</param>
		public TcpRunnerReporterMessageHandler(TcpRunnerClient client) =>
			this.client = Guard.ArgumentNotNull(nameof(client), client);

		/// <inheritdoc/>
		public ValueTask DisposeAsync() =>
			client.Stop();

		/// <inheritdoc/>
		public bool OnMessage(_MessageSinkMessage message) =>
			client.QueueMessage(message);
	}
}
