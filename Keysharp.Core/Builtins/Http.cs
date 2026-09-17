namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>
		/// HTTP requests. Scripts reach it through the KS module: <c>#Import "Ks" { Http }</c>, then either the
		/// static shortcuts — <c>Http.Get(url)</c> — or a session, <c>Http(Options)</c>, which carries default
		/// headers, credentials and a cookie jar across its own requests.
		/// <para>A status outside 2xx is an answer rather than an error: the server replied, and its body usually
		/// says why. Only a transport failure, a timeout and unusable input raise.</para>
		/// </summary>
		public class Http : KeysharpObject, IDisposable
		{
			private const double DefaultTimeoutSeconds = 30;
			private const int ReadBufferSize = 64 * 1024;

			/// <summary>
			/// How long received bytes accumulate before <c>OnData</c> is called with them. Each delivery is a
			/// pseudo-thread launch, and a 64 KB chunk at 10 MB/s would otherwise be 160 of them a second.
			/// </summary>
			private const int DataFlushIntervalMs = 100;

			/// <summary>The largest body pre-sized from Content-Length, so a hostile header cannot ask for more.</summary>
			private const long MaxPreSizedBody = 64L * 1024 * 1024;

			/// <summary>
			/// The largest chunk OnData is handed. Without it a fast link grows the chunk with its own speed, since
			/// the flush is otherwise timed, and every delivery allocates on the large object heap.
			/// </summary>
			private const int MaxChunkBytes = 1024 * 1024;

			/// <summary>Read once: the assembly attribute behind it costs microseconds and never changes.</summary>
			private static readonly string userAgent = $"Keysharp/{Ks.A_KsVersion}";

			/// <summary>The characters RFC 9110 allows in a method token.</summary>
			private static readonly SearchValues<char> MethodChars =
				SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!#$%&'*+-.^_`|~");

			/// <summary>
			/// The stateless client behind the static shortcuts. It holds no cookies, so two unrelated libraries in
			/// one script calling the same host cannot see each other's state. It is a bare
			/// <see cref="HttpClient"/> rather than an <see cref="Http"/> so no script object outlives its script.
			/// </summary>
			private static HttpClient sharedClient;
			private static Script sharedOwner;
			private static readonly Lock sharedGate = new();

			private HttpClient client;
			private RequestOptions session = new();
			private string unusable;

			public Http(params object[] args) : base(args) { }

			/// <summary>
			/// Creates a session: its own connection pool, cookie jar and default options.
			/// </summary>
			/// <param name="options">A <see cref="Map"/> or object supplying any of the request options —
			/// <c>Headers</c>, <c>Body</c>, <c>Json</c>, <c>Timeout</c>, <c>OnData</c> — as defaults for every
			/// request made through this session, plus the session-only <c>BaseUrl</c>, <c>Auth</c>, <c>Proxy</c>,
			/// <c>IgnoreCertificateErrors</c> and <c>Handler</c>.</param>
			/// <exception cref="ValueError">Thrown for an unknown option key or an unusable option value.</exception>
			public object __New(object options = null)
			{
				if (RequestOptions.Parse(options, isSession: true) is not { } parsed)
				{
					// The options were reported as they were read. This session keeps a usable shape so a later
					// member access names the real cause rather than failing on a half-built object.
					unusable = "This Http session was not created: its options were rejected.";
					return DefaultObject;
				}

				session = parsed;
				session.Headers = RequestOptions.CaseInsensitive(parsed.Headers) ?? new Map(eCaseSense.Off);
				client = parsed.NewClient();
				return DefaultObject;
			}

			// ---- session state -------------------------------------------------------------------------------

			/// <summary>
			/// The headers sent with every request through this session, as a live case-insensitive
			/// <see cref="Map"/>. A request's own <c>Headers</c> merge over these, and a request value of
			/// <c>""</c> removes one for that request. Read when a request is sent, so changing it leaves a
			/// request already in flight alone.
			/// </summary>
			public object Headers
			{
				get => session.Headers;

				set
				{
					if (value is Map map)
						session.Headers = RequestOptions.CaseInsensitive(map);
					else
						_ = Errors.TypeErrorOccurred(value, typeof(Map));
				}
			}

			/// <summary>
			/// Seconds to wait for the response headers, and then for each further piece of the body — an idle
			/// timeout rather than a total one. <c>-1</c> waits indefinitely. Default 30.
			/// </summary>
			public object Timeout
			{
				get => session.Timeout ?? DefaultTimeoutSeconds;
				set => session.Timeout = RequestOptions.ParseTimeout(value) ?? session.Timeout;
			}

			/// <summary>
			/// Resolved against a request URL which is not already absolute, as a browser resolves a link: a
			/// <c>BaseUrl</c> ending in <c>/</c> keeps its whole path, and one that does not loses its last segment.
			/// </summary>
			public object BaseUrl
			{
				get => session.BaseUrl ?? "";
				set => session.BaseUrl = value.As();
			}

			/// <summary>
			/// The callback every request through this session streams its body to, unless the request names its
			/// own. Reads back as <c>""</c> when there is none. A download ignores it: the file is the body's sink.
			/// </summary>
			public object OnData
			{
				get => session.OnData ?? "";

				set
				{
					// Called with a prefix of (Chunk, Received, Total), so only that it is callable is checked here.
					if (value is null || (value is string s && s.Length == 0))
						session.OnData = null;
					else if (Functions.CheckedCallback(value, -1) is { } callback)
						session.OnData = callback;
				}
			}

			/// <summary>The underlying <see cref="HttpClient"/>, for settings this class does not surface.</summary>
			public object ToClr() => ManagedInvoke.WrapManaged(client);

			/// <summary>
			/// Releases the session's connections. A session left to the garbage collector releases them anyway,
			/// so this is for a script that opens many sessions and wants their sockets back promptly.
			/// </summary>
			public object Close()
			{
				unusable ??= "This Http session has been closed.";
				client?.Dispose();
				client = null;
				HasFinalizer = false;
				return DefaultObject;
			}

			[PublicHiddenFromUser]
			public void Dispose() => _ = Close();

			// ---- session requests ----------------------------------------------------------------------------

			/// <summary>Sends a GET and waits for the response.</summary>
			/// <param name="url">Absolute, or relative to <see cref="BaseUrl"/>.</param>
			/// <param name="options">Per-request options, merged over the session's.</param>
			/// <returns>An <see cref="Response"/>, whatever the status.</returns>
			/// <exception cref="OSError">The request never reached a reply.</exception>
			/// <exception cref="TimeoutError">Nothing arrived within <see cref="Timeout"/>.</exception>
			/// <exception cref="ValueError">The URL, an option or a header is unusable.</exception>
			public object Get(object url, object options = null) => Run("GET", url, null, options, async: false);

			/// <summary>Sends a POST and waits for the response.</summary>
			/// <param name="url">Absolute, or relative to <see cref="BaseUrl"/>.</param>
			/// <param name="body">The request body: a String sent as UTF-8 text, or a <see cref="Buffer"/> sent as
			/// bytes. Positional sugar for the <c>Body</c> option.</param>
			/// <param name="options">Per-request options, merged over the session's.</param>
			/// <inheritdoc cref="Get"/>
			public object Post(object url, object body = null, object options = null) => Run("POST", url, body, options, async: false);

			/// <summary>Sends any method and waits for the response.</summary>
			/// <param name="@method">The HTTP method, uppercased. <c>PUT</c>, <c>PATCH</c>, <c>DELETE</c> and
			/// <c>HEAD</c> have no shortcut of their own and are spelled here.</param>
			/// <param name="url">Absolute, or relative to <see cref="BaseUrl"/>.</param>
			/// <param name="body">As for <see cref="Post"/>.</param>
			/// <param name="options">Per-request options, merged over the session's.</param>
			/// <inheritdoc cref="Get"/>
			public object Request(object @method, object url, object body = null, object options = null)
				=> Run(@method.As(), url, body, options, async: false);

			/// <summary>The same as <see cref="Get"/>, but returns a <c>Task</c> rather than waiting.</summary>
			public object GetAsync(object url, object options = null) => Run("GET", url, null, options, async: true);

			/// <summary>The same as <see cref="Post"/>, but returns a <c>Task</c> rather than waiting.</summary>
			public object PostAsync(object url, object body = null, object options = null) => Run("POST", url, body, options, async: true);

			/// <summary>The same as <see cref="Request"/>, but returns a <c>Task</c> rather than waiting.</summary>
			public object RequestAsync(object @method, object url, object body = null, object options = null)
				=> Run(@method.As(), url, body, options, async: true);

			/// <summary>Fetches a URL straight to a file, without it ever being a script value.</summary>
			/// <param name="url">Absolute, or relative to <see cref="BaseUrl"/>.</param>
			/// <param name="path">The file to create, overwriting any existing one. It is opened once the response
			/// headers have arrived, so a request that never reaches a reply leaves an existing file alone.</param>
			/// <param name="options">Per-request options, merged over the session's. <c>OnData</c> is not one of
			/// them: the body goes to the file.</param>
			/// <returns>The <see cref="Response"/>, whose <c>Body</c> is empty because the file took it.</returns>
			/// <inheritdoc cref="Get"/>
			public object Download(object url, object path, object options = null)
				=> Run("GET", url, null, options, async: false, path: path.As());

			/// <summary>The same as <see cref="Download"/>, but returns a <c>Task</c> rather than waiting.</summary>
			public object DownloadAsync(object url, object path, object options = null)
				=> Run("GET", url, null, options, async: true, path: path.As());

			// ---- stateless shortcuts -------------------------------------------------------------------------

			/// <summary>Sends a GET on the shared stateless client and waits for the response.</summary>
			/// <inheritdoc cref="Get"/>
			public static object staticGet(object @this, object url, object options = null)
				=> RunShared("GET", url, null, options, async: false);

			/// <summary>Sends a POST on the shared stateless client and waits for the response.</summary>
			/// <inheritdoc cref="Post"/>
			public static object staticPost(object @this, object url, object body = null, object options = null)
				=> RunShared("POST", url, body, options, async: false);

			/// <summary>Sends any method on the shared stateless client and waits for the response.</summary>
			/// <inheritdoc cref="Request"/>
			public static object staticRequest(object @this, object @method, object url, object body = null, object options = null)
				=> RunShared(@method.As(), url, body, options, async: false);

			/// <summary>The same as <c>Http.Get</c>, but returns a <c>Task</c> rather than waiting.</summary>
			public static object staticGetAsync(object @this, object url, object options = null)
				=> RunShared("GET", url, null, options, async: true);

			/// <summary>The same as <c>Http.Post</c>, but returns a <c>Task</c> rather than waiting.</summary>
			public static object staticPostAsync(object @this, object url, object body = null, object options = null)
				=> RunShared("POST", url, body, options, async: true);

			/// <summary>The same as <c>Http.Request</c>, but returns a <c>Task</c> rather than waiting.</summary>
			public static object staticRequestAsync(object @this, object @method, object url, object body = null, object options = null)
				=> RunShared(@method.As(), url, body, options, async: true);

			/// <summary>Fetches a URL straight to a file on the shared stateless client.</summary>
			/// <inheritdoc cref="Download"/>
			public static object staticDownload(object @this, object url, object path, object options = null)
				=> RunShared("GET", url, null, options, async: false, path: path.As());

			/// <summary>The same as <c>Http.Download</c>, but returns a <c>Task</c> rather than waiting.</summary>
			public static object staticDownloadAsync(object @this, object url, object path, object options = null)
				=> RunShared("GET", url, null, options, async: true, path: path.As());

			// ---- dispatch ------------------------------------------------------------------------------------

			private object Run(string method, object url, object body, object options, bool async, string path = null)
				=> unusable != null
				   ? Errors.ErrorOccurred(unusable)
				   : Send(client, session, method, url, body, options, async, path);

			private static object RunShared(string method, object url, object body, object options, bool async,
											string path = null)
				=> Send(SharedClient(), null, method, url, body, options, async, path);

			private static object Send(HttpClient client, RequestOptions session, string method, object url,
									   object body, object options, bool async, string path)
			{
				if (RequestOptions.Parse(options, isSession: false) is not { } request)
					return DefaultObject;

				if (body != null)
				{
					if (request.HasBody || request.HasJson)
						return Errors.ValueErrorOccurred("A body was given both positionally and as an option.");

					request.Body = body;
					request.HasBody = true;
				}

				// A download's body goes to the file, so a request that also asks for OnData is asking for the
				// same bytes twice. A session-level default is left alone: it belongs to that session's other calls.
				if (path != null && request.OnData != null)
					return Errors.ValueErrorOccurred(
						"OnData and a download both take the body, so only one of them may be given.");

				var merged = request.MergeOver(session);
				Func<Stream> sink = null;

				if (path != null)
				{
					// The file is the body's sink, so a session's OnData default has nothing to do here.
					merged.OnData = null;
					sink = () => new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
												ReadBufferSize, FileOptions.Asynchronous);
				}

				if (!TryBuildRequest(merged, method, url.As(), out var message, out var error))
					return error.Length == 0 ? DefaultObject : Errors.ValueErrorOccurred(error);

				// Captured on the calling script thread: OnData runs there, at the priority of the thread that
				// asked, so a request made from a raised-priority thread is not refused by its own priority.
				var script = Script.TheScript;
				var scheduler = merged.OnData == null ? null : script?.CurrentSchedulerIfCreated ?? script?.EventScheduler;

				if (merged.OnData != null && scheduler == null)
				{
					message.Dispose();
					return Errors.ErrorOccurred("OnData needs a script thread to run on, and this call has none.");
				}

				var work = SendAsync(client, message, merged, scheduler,
									 script?.Threads.CurrentThread.priority ?? 0, sink);

				// A streaming transfer roots the script for as long as it runs, the way a pending Task.Then does:
				// its callback is still to come, so an otherwise idle script must not exit out from under it.
				if (scheduler != null)
					Root(scheduler, work);

				return async ? KeysharpTask.Wrap(work) : Await(KeysharpTask.Wrap(work));
			}

			/// <summary>
			/// Keeps <paramref name="scheduler"/> alive until <paramref name="work"/> settles, and fails the
			/// transfer rather than leaving it hanging if the scheduler is torn down first.
			/// </summary>
			private static void Root(ScriptEventScheduler scheduler, Task work)
			{
				// Nothing to undo on teardown: the delivery in flight registers its own callback and fails there,
				// and this one exists only to hold the root while the transfer is between chunks.
				static void Invalidated() { }

				if (!scheduler.RegisterPendingCallback(Invalidated))
					return;

				_ = work.ContinueWith(static (_, state) =>
				{
					var (owner, release) = ((ScriptEventScheduler, Action))state;
					owner.ReleasePendingCallback(release);
				}, (scheduler, (Action)Invalidated), CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
			}

			/// <summary>
			/// Turns the merged options into a request message, or reports the first thing about them that cannot
			/// be sent.
			/// </summary>
			private static bool TryBuildRequest(RequestOptions options, string method, string url,
												out HttpRequestMessage message, out string error)
			{
				message = null;

				if (!TryResolveUrl(options.BaseUrl, url, out var uri, out error))
					return false;

				// HttpMethod's own validation throws a bare FormatException, which is not one of this class's
				// documented errors.
				if (method.Length == 0 || method.AsSpan().ContainsAnyExcept(MethodChars))
				{
					error = $"\"{method}\" is not a usable HTTP method.";
					return false;
				}

				HttpContent content = null;

				if (options.HasJson)
				{
					if (Json.Encode(null, options.Json) is not string encoded)
					{
						// Json.Encode has already reported why; an empty reason says so.
						error = "";
						return false;
					}

					content = new StringContent(encoded, Encoding.UTF8, "application/json");
				}
				else if (options.Body is Any and not Buffer)
				{
					// Anything else is stringified into the request, so an options map handed to the Body slot
					// would be sent as its text and answered with a cheerful 200.
					_ = Errors.TypeErrorOccurred(options.Body, typeof(Buffer));
					error = "";
					return false;
				}
				else if (options.Body is Buffer buf)
					content = new ByteArrayContent(buf.Size.Al() == 0 ? [] : buf.ToByteArray())
				{
					Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") }
				};
				else if (options.Body != null)
					content = new StringContent(options.Body.As(), Encoding.UTF8, "text/plain");

				message = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), uri) { Content = content };
				error = ApplyHeaders(message, options.Headers);

				if (error != null)
				{
					message.Dispose();
					message = null;
					return false;
				}

				return true;
			}

			/// <summary>
			/// Sends the request and consumes the body, either into a <see cref="Response"/>, into the stream
			/// <paramref name="destination"/> opens, or into the script's <c>OnData</c>. The three share one read
			/// loop, so all three get the same idle timeout and the same streaming.
			/// </summary>
			/// <param name="destination">Opened once the response headers have arrived, so a request that never
			/// reaches a reply leaves an existing file alone.</param>
			private static async Task<object> SendAsync(HttpClient client, HttpRequestMessage request,
														RequestOptions options, ScriptEventScheduler scheduler,
														long priority, Func<Stream> destination)
			{
				var timeoutMs = options.TimeoutMs(DefaultTimeoutSeconds);
				var onData = options.OnData;
				var arity = onData == null ? 0 : Script.CallbackArgCount(onData, 3);
				var aborted = false;
				var kept = false;
				HttpResponseMessage message = null;
				byte[] buffer = null;
				using var cts = new CancellationTokenSource();

				void ResetIdle()
				{
					if (timeoutMs >= 0)
						cts.CancelAfter(timeoutMs);
				}

				try
				{
					ResetIdle();
					message = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
							  .ConfigureAwait(false);
					ResetIdle();
					var total = message.Content.Headers.ContentLength ?? -1L;
					using var stream = await message.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
					using var sink = destination?.Invoke();
					var body = onData != null || sink != null
							   ? null
							   : new MemoryStream(total > 0 && total <= MaxPreSizedBody ? (int)total : 0);
					var pending = onData == null ? null : new MemoryStream();
					var lastFlush = Environment.TickCount64;
					long received = 0;
					buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);

					// Hands one accumulated chunk to the script and unwinds the transfer if it asked to stop. The
					// idle timer measures the connection, so it does not run while the script's own callback does.
					async Task Flush(Buffer chunk)
					{
						cts.CancelAfter(System.Threading.Timeout.Infinite);

						if (await DeliverAsync(onData, arity, scheduler, priority, chunk, received, total).ConfigureAwait(false))
						{
							aborted = true;
							throw new OperationCanceledException();
						}

						lastFlush = Environment.TickCount64;
						ResetIdle();
					}

					async Task FlushPending()
					{
						await Flush(Drain(pending)).ConfigureAwait(false);
						pending.SetLength(0);
					}

					while (true)
					{
						var read = await stream.ReadAsync(buffer.AsMemory(0, ReadBufferSize), cts.Token).ConfigureAwait(false);

						if (read == 0)
							break;

						ResetIdle();

						if (sink != null)
						{
							await sink.WriteAsync(buffer.AsMemory(0, read), cts.Token).ConfigureAwait(false);
							continue;
						}

						if (onData == null)
						{
							body.Write(buffer, 0, read);
							continue;
						}

						// Held bytes go out before the read that would push them past the cap, and before Received
						// counts that read, so Received always names exactly what the script has been handed.
						if (pending.Length > 0 && pending.Length + read > MaxChunkBytes)
							await FlushPending().ConfigureAwait(false);

						received += read;

						// Delivery is due on time so a slow trickle still reports progress, and on size so a fast
						// link does not hand the script an ever larger chunk -- on time alone, a gigabit would.
						if (Environment.TickCount64 - lastFlush < DataFlushIntervalMs
								&& pending.Length + read < MaxChunkBytes)
						{
							pending.Write(buffer, 0, read);
							continue;
						}

						if (pending.Length == 0)
						{
							await Flush(Chunk(buffer, read)).ConfigureAwait(false);
							continue;
						}

						pending.Write(buffer, 0, read);
						await FlushPending().ConfigureAwait(false);
					}

					if (onData != null && pending.Length > 0)
						await FlushPending().ConfigureAwait(false);

					// A streamed request still answers with its status and headers; the body is empty because
					// OnData or the file took it.
					kept = true;
					return Response.From(message, body == null ? [] : Exact(body));
				}
				// Only a fired idle timer is a timeout. An abort, and an Exit inside the callback, are cancellations
				// and travel as themselves.
				catch (OperationCanceledException) when (!aborted && cts.IsCancellationRequested)
				{
					throw (Exception)new TimeoutError(
						$"The HTTP request to {request.RequestUri} timed out after {timeoutMs / 1000.0} s without progress.");
				}
				catch (Exception ex) when (ex is HttpRequestException or IOException)
				{
					throw (Exception)new OSError(ex, "Http");
				}
				finally
				{
					// Cleared on return: a pooled buffer holds the response body, and the next renter is unrelated.
					if (buffer != null)
						ArrayPool<byte>.Shared.Return(buffer, clearArray: true);

					// A Response is the only thing that keeps the message, for its headers and its Clr.Wrap view.
					if (!kept)
						message?.Dispose();

					request.Dispose();
				}
			}

			/// <summary>
			/// One chunk, copied straight into the Buffer's own memory. Nothing managed is allocated for it, which
			/// keeps a fast transfer's chunks -- up to <see cref="MaxChunkBytes"/> -- off the large object heap.
			/// </summary>
			private static Buffer Chunk(byte[] source, int count)
			{
				// Sized rather than constructed from the array: the Size setter allocates without filling, and the
				// copy below writes every byte of it.
				var chunk = new Buffer();
				chunk.Size = (long)count;

				if (count > 0)
					Marshal.Copy(source, 0, (nint)chunk.Ptr, count);

				return chunk;
			}

			/// <summary>The accumulated chunk. The stream keeps its capacity, so it is allocated once per transfer.</summary>
			private static Buffer Drain(MemoryStream pending)
			{
				_ = pending.TryGetBuffer(out var segment);//Always succeeds: the stream is one of ours.
				return Chunk(segment.Array, (int)pending.Length);
			}

			/// <summary>The stream's own array when it is exactly full, so a sized read is not copied again.</summary>
			private static byte[] Exact(MemoryStream body)
				=> body.Length == body.Capacity && body.TryGetBuffer(out var segment)
				   ? segment.Array
				   : body.ToArray();

			/// <summary>
			/// Hands one chunk to <c>OnData</c> on the script thread that asked for the request, and reports whether
			/// it asked to stop. The transfer awaits the answer rather than occupying a thread with it.
			/// <para>It is queued as dispatch, so the pump serves it in arrival order even while thread launches
			/// are parked, and the pseudo-thread it starts is admitted unconditionally. Registering it as a pending
			/// callback settles the wait if the scheduler is torn down while a chunk is in the air.</para>
			/// </summary>
			private static async Task<bool> DeliverAsync(object onData, int arity, ScriptEventScheduler scheduler,
														 long priority, Buffer chunk, long received, long total)
			{
				var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
				object[] args = arity switch
				{
					0 => [],
					1 => [chunk],
					2 => [chunk, received],
					_ => [chunk, received, total],
				};
				void Invalidated() => completion.TrySetException(
					(Exception)new Error("The Http transfer stopped because its script thread went away."));

				if (!scheduler.RegisterPendingCallback(Invalidated))
					throw (Exception)new Error("The Http transfer stopped because its script thread is gone.");

				// Queued as dispatch rather than as a launch, so the pump keeps serving it while launches are
				// parked, and served in arrival order as a sent message is.
				var queued = scheduler.EnqueueCallback(() =>
				{
					try
					{
						// Admitted unconditionally: this is data that has already arrived and that the transfer is
						// already waiting on, so refusing it for Critical, #MaxThreads or a higher-priority thread
						// would stall a synchronous request against the very thread that has to serve it.
						// AutoHotkey serves a sent message on the same terms.
						var status = scheduler.TryInvokePseudoThread(priority, skipUninterruptible: true, isCritical: false,
									 _ =>
									 {
										 // The transfer fails with what the callback raises.
										 using var tryScope = Keysharp.Runtime.Flow.EnterTry();
										 return Script.InvokeOrNull(onData, null, args);
									 }, out var value, allowEmergencyOverflow: true);
						_ = status == ScriptEventExecutionResult.Executed
							? completion.TrySetResult(value)
							: completion.TrySetException(
								(Exception)new Error("The Http OnData callback could not run, so the transfer stopped."));
					}
					catch (Exception ex)
					{
						// An Exit or ExitApp inside the callback ends that thread rather than becoming the
						// transfer's failure, so the wait is canceled and the exception left to unwind the pump.
						if (Keysharp.Internals.Flow.TryGetException(ex, out Keysharp.Builtins.Flow.UserRequestedExitException _))
						{
							_ = completion.TrySetCanceled();
							throw;
						}

						_ = completion.TrySetException(ex);
					}
					finally
					{
						scheduler.ReleasePendingCallback(Invalidated);
					}
				}, ScriptEventQueue.Interactive, priority);

				if (!queued)
				{
					scheduler.ReleasePendingCallback(Invalidated);
					throw (Exception)new Error("The Http OnData callback could not be queued, so the transfer stopped.");
				}

				// Only a non-zero Integer stops the transfer, as with OnExit. Nothing returned, or a value that is
				// not an Integer, keeps it going.
				return await completion.Task.ConfigureAwait(false) is long stop && stop != 0;
			}

			// ---- Download ------------------------------------------------------------------------------------

			/// <summary>
			/// What the global <c>Download</c> is over http and https: the ordinary send path with a file as its
			/// sink, so it shares the headers, the idle timeout and the streaming.
			/// </summary>
			/// <param name="uri">The resource to fetch, already known to be absolute http or https.</param>
			/// <param name="path">The file to create, overwriting any existing one.</param>
			/// <param name="noCache">True to ask every cache along the way for a fresh copy.</param>
			internal static object DownloadTo(Uri uri, string path, bool noCache)
			{
				var options = noCache
							  ? new Map(eCaseSense.Off) { ["Cache-Control"] = "no-cache" }
							  : null;
				_ = Send(SharedClient(), null, "GET", uri.ToString(), null,
						 options == null ? null : new Map(eCaseSense.Off) { ["Headers"] = options },
						 async: false, path: path);
				return DefaultObject;
			}

			// ---- plumbing ------------------------------------------------------------------------------------

			/// <summary>
			/// The stateless client, rebuilt when a different script is running so a reload does not inherit the
			/// previous one's connections.
			/// </summary>
			private static HttpClient SharedClient()
			{
				var script = Script.TheScript;

				lock (sharedGate)
				{
					if (sharedClient != null && ReferenceEquals(sharedOwner, script))
						return sharedClient;

					sharedClient?.Dispose();
					sharedOwner = script;
					return sharedClient = new RequestOptions { NoCookies = true }.NewClient();
				}
			}

			/// <summary>
			/// The credentials a URL carries in its userinfo, as <c>scheme://user:pass@host</c> spells them, or null
			/// when it carries none. Shared with the ftp side of <c>Download</c>, which reads them the same way.
			/// </summary>
			internal static NetworkCredential UserInfoCredentials(Uri uri)
			{
				if (uri.UserInfo.Length == 0)
					return null;

				var split = uri.UserInfo.Split(':', 2);
				return new NetworkCredential(Uri.UnescapeDataString(split[0]),
											 split.Length > 1 ? Uri.UnescapeDataString(split[1]) : "");
			}
			private static bool TryResolveUrl(string baseUrl, string url, out Uri uri, out string error)
			{
				uri = null;
				error = null;

				if (url.Length == 0)
				{
					error = "The URL cannot be empty.";
					return false;
				}

				if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
				{
					if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var root)
							|| !Uri.TryCreate(root, url, out uri))
					{
						error = $"\"{url}\" is not an absolute URL, and no BaseUrl makes it one.";
						uri = null;
						return false;
					}
				}

				// Checked here rather than left to the handler, which reports an unusable scheme as a bare CLR
				// exception carrying no script error type.
				if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
				{
					error = $"\"{uri.Scheme}\" is not an HTTP scheme. Only http and https are supported.";
					uri = null;
					return false;
				}

				return true;
			}

			/// <summary>Headers .NET keeps on the content although their names do not say so.</summary>
			private static readonly FrozenSet<string> contentOnlyNames =
				new[] { "Expires", "Last-Modified", "Allow" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

			/// <summary>
			/// Applies header entries, returning a message for the first value that cannot be sent. A value of
			/// <c>""</c> removes a header a session had set. Content headers belong to the content, which is also
			/// why a response merges the two collections back together.
			/// </summary>
			private static string ApplyHeaders(HttpRequestMessage request, Map headers)
			{
				var agent = false;

				if (headers != null)
				{
					foreach (var (key, val) in (IEnumerable<(object, object)>)headers)
					{
						var name = key.As();
						var value = val.As();

						if (name.Length == 0)
							return "A header name cannot be empty.";

						agent |= name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase);

						if (value.Length == 0)
							continue;

						var onContent = name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase);

						// These describe a body, so a request without one has nothing to put them on. .NET files a
						// few names that do not begin with Content- under the content too, hence the second set.
						if (request.Content == null && (onContent || contentOnlyNames.Contains(name)))
							continue;

						if (onContent && name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
						{
							if (!MediaTypeHeaderValue.TryParse(value, out var mediaType))
								return $"\"{value}\" is not a usable Content-Type.";

							request.Content.Headers.ContentType = mediaType;
							continue;
						}

						// Without validation, so a header name .NET's strict parser dislikes still reaches the server.
						// .NET files a handful of ordinary request headers -- Expires, Last-Modified, Allow -- under
						// the content, so a name the request collection refuses is offered to the content before it
						// is called unusable.
						var added = onContent
									? request.Content.Headers.TryAddWithoutValidation(name, value)
									: request.Headers.TryAddWithoutValidation(name, value)
									  || (request.Content?.Headers.TryAddWithoutValidation(name, value) ?? false);

						if (!added)
							return $"\"{name}\" is not a usable header name.";
					}
				}

				// Set per request rather than on the client, so `Headers["User-Agent"] := ""` removes it as the
				// documented "" rule says. HttpClient sends no User-Agent at all, which several APIs answer with 403.
				if (!agent)
					_ = request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

				return null;
			}

			// ---- options -------------------------------------------------------------------------------------

			/// <summary>
			/// One request's options, and — held by a session — that session's defaults. The two are the same
			/// shape so merging one over the other is the whole of the precedence rule.
			/// </summary>
			private sealed class RequestOptions
			{
				internal Map Headers;
				internal object Body;
				internal bool HasBody;
				internal object Json;
				internal bool HasJson;
				internal double? Timeout;
				internal object OnData;

				internal string BaseUrl;
				internal NetworkCredential Credentials;
				internal WebProxy Proxy;
				internal bool NoProxy;
				internal bool NoCookies;
				internal bool IgnoreCertificateErrors;
				internal HttpMessageHandler Handler;

				internal int TimeoutMs(double fallback)
				{
					var seconds = Timeout ?? fallback;
					return seconds < 0 ? -1 : (int)Math.Min(int.MaxValue, seconds * 1000);
				}

				/// <summary>This request's options over a session's defaults.</summary>
				internal RequestOptions MergeOver(RequestOptions defaults)
					=> defaults == null
					   ? this
					   : new RequestOptions
				{
					BaseUrl = BaseUrl ?? defaults.BaseUrl,
					Timeout = Timeout ?? defaults.Timeout,
					OnData = OnData ?? defaults.OnData,
					Headers = Merge(defaults.Headers, Headers),
					Body = Body,
					HasBody = HasBody,
					Json = Json,
					HasJson = HasJson,
				};

				private static Map Merge(Map session, Map request)
				{
					if (request == null)
						return session;

					if (session == null)
						return request;

					var merged = CaseInsensitive(session);

					foreach (var (key, val) in (IEnumerable<(object, object)>)request)
						merged[key] = val;

					return merged;
				}

				/// <summary>
				/// A copy whose keys compare case-insensitively, as HTTP header names do. A script's own Map is
				/// case-sensitive unless it says otherwise, and its CaseSense cannot be changed once it holds
				/// entries.
				/// </summary>
				internal static Map CaseInsensitive(Map source)
				{
					if (source == null)
						return null;

					var copy = new Map(eCaseSense.Off);

					foreach (var (key, val) in (IEnumerable<(object, object)>)source)
						copy[key] = val;

					return copy;
				}

				internal static double? ParseTimeout(object value)
				{
					var seconds = value.Ad(double.NaN);

					if (double.IsNaN(seconds) || (seconds <= 0 && seconds != -1))
					{
						_ = Errors.ValueErrorOccurred("Timeout must be a positive number of seconds, or -1 to wait indefinitely.", value);
						return null;
					}

					return seconds;
				}

				/// <summary>
				/// The client a session's connection options describe. They configure the message handler, which
				/// .NET freezes once a request has been sent, which is why they are construction-only.
				/// </summary>
				internal HttpClient NewClient()
				{
					var handler = Handler;

					if (handler == null)
					{
						var sockets = new SocketsHttpHandler
						{
							UseCookies = !NoCookies,
							AutomaticDecompression = DecompressionMethods.All,
							// A pooled connection outlives a DNS change without this, so a long-lived session keeps
							// talking to an address that has moved.
							PooledConnectionLifetime = TimeSpan.FromMinutes(2)
						};

						if (!NoCookies)
							sockets.CookieContainer = new CookieContainer();

						if (Credentials != null)
						{
							sockets.Credentials = Credentials;
							sockets.PreAuthenticate = true;
						}

						if (NoProxy)
							sockets.UseProxy = false;
						else if (Proxy != null)
							sockets.Proxy = Proxy;

						if (IgnoreCertificateErrors)
							sockets.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;

						handler = sockets;
					}

					// The timeout lives in a per-request token instead: HttpClient.Timeout is frozen after the
					// first request and measures the whole transfer rather than idle time.
					// A handler the script built is shared with whatever else it was given to, so only one this
					// class made is disposed with the client.
					return new HttpClient(handler, Handler == null) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
				}

				/// <summary>The options that configure the connection, and so belong only to a session.</summary>
				private static readonly FrozenSet<string> connectionKeys =
					new[] { "auth", "proxy", "ignorecertificateerrors", "handler" }.ToFrozenSet();

				/// <summary>The options that describe one request, and so cannot be a session's default.</summary>
				private static readonly FrozenSet<string> requestOnlyKeys = new[] { "body", "json" }.ToFrozenSet();

				/// <summary>
				/// Reads an options <see cref="Map"/> or object, or returns null once anything in it is unusable.
				/// An unknown key raises, so a typo is reported where it is written.
				/// </summary>
				/// <param name="isSession">Whether the connection options are allowed here.</param>
				internal static RequestOptions Parse(object options, bool isSession)
				{
					var parsed = new RequestOptions();

					if (options == null)
						return parsed;

					foreach (var (key, value) in Entries(options))
					{
						var name = key.ToLowerInvariant();

						// Each message names the reason rather than only the rule, which looks arbitrary alone.
						if (!isSession && connectionKeys.Contains(name))
						{
							_ = Errors.ValueErrorOccurred(
								$"{key} configures the connection, so it belongs to Http(Options) rather than to one request.");
							return null;
						}

						if (isSession && requestOnlyKeys.Contains(name))
						{
							_ = Errors.ValueErrorOccurred(
								$"{key} is one request's body, so it belongs to that request rather than to the session.");
							return null;
						}

						switch (name)
						{
							case "headers":
								if (value is not Map map)
								{
									_ = Errors.TypeErrorOccurred(value, typeof(Map));
									return null;
								}

								parsed.Headers = map;
								break;

							case "body":
								parsed.Body = value;
								parsed.HasBody = true;
								break;

							case "json":
								parsed.Json = value;
								parsed.HasJson = true;
								break;

							case "timeout":
								if (ParseTimeout(value) is not { } seconds)
									return null;

								parsed.Timeout = seconds;
								break;

							case "ondata":
								if (Functions.CheckedCallback(value, -1) is not { } callback)
									return null;

								parsed.OnData = callback;
								break;

							case "baseurl":
								parsed.BaseUrl = value.As();
								break;

							case "auth":
								if (!ParseAuth(parsed, value))
									return null;

								break;

							case "proxy":
								if (!ParseProxy(parsed, value.As()))
									return null;

								break;

							case "ignorecertificateerrors":
								parsed.IgnoreCertificateErrors = value.Ab();
								break;

							case "handler":
								if ((value as Clr.ManagedInstance)?.Native is not HttpMessageHandler handler)
								{
									_ = Errors.TypeErrorOccurred(value, typeof(HttpMessageHandler));
									return null;
								}

								parsed.Handler = handler;
								break;


							default:
								_ = Errors.ValueErrorOccurred($"\"{key}\" is not an Http option.", key);
								return null;
						}
					}

					// A supplied handler IS the connection, so the options that would have configured one are
					// refused rather than silently doing nothing.
					if (parsed.Handler != null
							&& (parsed.Credentials != null || parsed.Proxy != null || parsed.NoProxy
								|| parsed.IgnoreCertificateErrors))
					{
						_ = Errors.ValueErrorOccurred(
							"Handler is the connection, so Auth, Proxy and IgnoreCertificateErrors cannot be given with it.");
						return null;
					}

					return parsed;
				}

				private static bool ParseAuth(RequestOptions parsed, object value)
				{
					if (value is Array pair && pair.Count == 2)
					{
						parsed.Credentials = new NetworkCredential(pair[1L].As(), pair[2L].As());
						return true;
					}

					if (value.As().Equals("Default", StringComparison.OrdinalIgnoreCase))
					{
						parsed.Credentials = CredentialCache.DefaultNetworkCredentials;
						return true;
					}

					_ = Errors.ValueErrorOccurred("Auth must be [User, Password] or \"Default\".", value);
					return false;
				}

				private static bool ParseProxy(RequestOptions parsed, string value)
				{
					if (value.Length == 0)
					{
						parsed.NoProxy = true;
						return true;
					}

					if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
					{
						_ = Errors.ValueErrorOccurred($"\"{value}\" is not a usable proxy URL.", value);
						return false;
					}

					parsed.Proxy = new WebProxy(uri) { Credentials = UserInfoCredentials(uri) };
					return true;
				}

				private static IEnumerable<(string, object)> Entries(object options)
				{
					if (options is Map map)
					{
						foreach (var (key, value) in (IEnumerable<(object, object)>)map)
							yield return (key.As(), value);

						yield break;
					}

					// An object literal is the other natural spelling, and only its own value properties are read:
					// invoking a dynamic property to find an option would run script at an unexpected moment.
					if (options is KeysharpObject kso && kso.op != null)
						foreach (var (name, desc) in kso.op)
							if (desc.Value != null)
								yield return (name, desc.Value);
				}
			}

			/// <summary>
			/// What a server answered. A status outside 2xx arrives here rather than as an error, since the body of
			/// a failed request is usually where the reason is.
			/// </summary>
			public class Response : KeysharpObject
			{
				private HttpResponseMessage message;
				private byte[] bytes;
				private Map headers;
				private string text;
				private Buffer body;

				internal Response() : base(null) { }

				internal static Response From(HttpResponseMessage message, byte[] bytes) =>
					new() { message = message, bytes = bytes };

				/// <summary>The HTTP status code, such as 200 or 404.</summary>
				public object Status => (long)(int)message.StatusCode;

				/// <summary>The status code's reason phrase, such as "OK".</summary>
				public object StatusText => message.ReasonPhrase ?? "";

				/// <summary>Whether the status is in the 2xx range.</summary>
				public object IsSuccess => message.IsSuccessStatusCode;

				/// <summary>The URL the response came from, which differs from the request's after a redirect.</summary>
				public object Url => message.RequestMessage?.RequestUri?.ToString() ?? "";

				/// <summary>
				/// The response and content headers together, in one case-insensitive <see cref="Map"/>. A header
				/// sent more than once is joined with ", ", as HTTP itself defines.
				/// </summary>
				public object Headers
				{
					get
					{
						if (headers != null)
							return headers;

						headers = new Map(eCaseSense.Off);

						foreach (var header in message.Headers)
							headers[header.Key] = string.Join(", ", header.Value);

						foreach (var header in message.Content.Headers)
							headers[header.Key] = string.Join(", ", header.Value);

						return headers;
					}
				}

				/// <summary>
				/// The body decoded as text, using the charset the response names and UTF-8 when it names none or
				/// names one this platform does not know. Empty when <c>OnData</c> took the bytes instead.
				/// </summary>
				public object Text => text ??= Decode();

				/// <summary>The body's raw bytes. Empty when <c>OnData</c> took them instead.</summary>
				public object Body => body ??= new Buffer(bytes);

				/// <summary>
				/// The body decoded as JSON, the same as <c>Json.Decode(response.Text)</c>. Call
				/// <c>Json.Decode</c> directly to pass its <c>caseSense</c> or <c>nullValue</c> options.
				/// </summary>
				/// <exception cref="ValueError">Thrown if the body is not well-formed JSON.</exception>
				public object Json() => Ks.Json.Decode(null, Text);

				/// <summary>The underlying <see cref="HttpResponseMessage"/>, whose body has already been read.</summary>
				public object ToClr() => ManagedInvoke.WrapManaged(message);

				private string Decode()
				{
					var name = message.Content.Headers.ContentType?.CharSet;
					var encoding = Encoding.UTF8;

					if (!string.IsNullOrEmpty(name))
					{
						try
						{
							encoding = Encoding.GetEncoding(name.Trim('"'));
						}
						catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
					}

					var decoded = encoding.GetString(bytes);
					// A byte-order mark is framing rather than content, and left in place it breaks the first thing
					// most scripts do with the text -- Json.Decode raises on it.
					return decoded.Length > 0 && decoded[0] == '\uFEFF' ? decoded.Substring(1) : decoded;
				}
			}
		}
	}
}
