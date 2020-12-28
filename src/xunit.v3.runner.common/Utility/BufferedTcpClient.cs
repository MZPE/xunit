using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Internal;
using Xunit.Sdk;

namespace Xunit.Runner.Common
{
	/// <summary>
	/// Provides a line-oriented read/write wrapper over top of a TCP socket. Intended to be used
	/// on both sides of the v3 TCP-based reporter system.
	/// </summary>
	public class BufferedTcpClient : IAsyncDisposable
	{
		readonly DisposalTracker disposalTracker = new();
		readonly TaskCompletionSource<int> finishedSource = new();
		readonly Action<string> receiveHandler;
		readonly Socket socket;
		readonly List<Task> tasks = new();
		readonly AutoResetEvent writeEvent = new(initialState: false);
		readonly ConcurrentQueue<string> writeQueue = new();

		/// <summary>
		/// Initializes a new instance of the <see cref="BufferedTcpClient"/> class.
		/// </summary>
		/// <param name="socket">The TCP socket that is read from/written to.</param>
		/// <param name="receiveHandler">The handler that is called for each received line of text.</param>
		public BufferedTcpClient(
			Socket socket,
			Action<string> receiveHandler)
		{
			this.socket = Guard.ArgumentNotNull(nameof(socket), socket);
			this.receiveHandler = Guard.ArgumentNotNull(nameof(receiveHandler), receiveHandler);
		}

		/// <summary>
		/// Gets a flag to indicate whether the client has completed processing (either because
		/// the other side gracefully closed, or because of a fault during processing).
		/// </summary>
		public bool Completed { get; private set; }

		/// <summary>
		/// Gets the fault that happened while processing input; will return <c>null</c> if there
		/// was no fault.
		/// </summary>
		public Exception? Fault { get; private set; }

		/// <inheritdoc/>
		public async ValueTask DisposeAsync()
		{
			MarkComplete();

			await Task.WhenAll(tasks);
			await disposalTracker.DisposeAsync();
		}

		void MarkComplete()
		{
			Completed = true;

			finishedSource.TrySetResult(0);
			writeEvent.Set();
		}

		/// <summary>
		/// Sends text to the other side of the connection. Does not add the required \n to the string,
		/// so ensure that you have properly formatted the message (or you send the message in multiple
		/// pieces).
		/// </summary>
		/// <param name="text">The text to send to the other side of the connection.</param>
		public void Send(string text)
		{
			writeQueue.Enqueue(text);
			writeEvent.Set();
		}

		/// <summary>
		/// Starts the read/write background workers.
		/// </summary>
		public void Start()
		{
			tasks.Add(Task.Run(StartSocketPipeReader));
			tasks.Add(Task.Run(StartSocketPipeWriter));
		}

		async Task StartSocketPipeReader()
		{
			var stream = new NetworkStream(socket);
			disposalTracker.Add(stream);

			var reader = PipeReader.Create(stream);

			while (true)
			{
				try
				{
					var readTask = reader.ReadAsync().AsTask();
					var completedTask = await Task.WhenAny(readTask, finishedSource.Task);
					if (completedTask == finishedSource.Task)
					{
						reader.CancelPendingRead();
						break;
					}

					var result = await readTask;
					var buffer = result.Buffer;

					while (TryReadLine(ref buffer, out var line))
					{
						var sb = new StringBuilder();

						foreach (var lineSegment in line)
						{
							if (!MemoryMarshal.TryGetArray(lineSegment, out var arraySegment))
								throw new InvalidOperationException("Buffer backed by array was expected");
							if (arraySegment.Array == null)
								throw new InvalidOperationException("ArraySegment<byte> returned a null array");

							sb.Append(Encoding.UTF8.GetString(arraySegment.Array, arraySegment.Offset, arraySegment.Count));
						}

						try
						{
							receiveHandler(sb.ToString());
						}
						catch { }  // Ignore the handler throwing; that's their problem, not ours.
					}

					reader.AdvanceTo(buffer.Start, buffer.End);

					if (result.IsCompleted)
						break;
				}
				catch (Exception ex)
				{
					Fault = ex;
					break;
				}
			}

			await reader.CompleteAsync();

			MarkComplete();
		}

		async Task StartSocketPipeWriter()
		{
			var stream = new NetworkStream(socket);
			disposalTracker.Add(stream);

			var writer = PipeWriter.Create(stream);

			while (true)
			{
				try
				{
					writeEvent.WaitOne();

					while (writeQueue.TryDequeue(out var message))
						await writer.WriteAsync(Encoding.UTF8.GetBytes(message));

					await writer.FlushAsync();

					if (finishedSource.Task.IsCompleted)
						break;
				}
				catch (Exception ex)
				{
					Fault = ex;
					break;
				}
			}

			await writer.CompleteAsync();

			MarkComplete();
		}

		bool TryReadLine(
			ref ReadOnlySequence<byte> buffer,
			out ReadOnlySequence<byte> line)
		{
			var position = buffer.PositionOf((byte)'\n');

			if (position == null)
			{
				line = default;
				return false;
			}

			line = buffer.Slice(0, position.Value);
			buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
			return true;
		}
	}
}
