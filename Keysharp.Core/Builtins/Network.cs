namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for network-related functions.
	/// </summary>
	public partial class Ks
	{
		/// <summary>
		/// Sends an email through an SMTP server.
		/// </summary>
		/// <param name="recipients">One address as a String, or several as an <see cref="Array"/> of Strings.</param>
		/// <param name="subject">Subject of the message.</param>
		/// <param name="message">Message body.</param>
		/// <param name="options">A <see cref="Map"/> carrying the settings below. <c>host</c> and <c>from</c> are
		/// required, because SMTP has no sensible default for either. Every other key is optional, and an
		/// unrecognized key raises rather than being sent as a header.<br/>
		/// host: The SMTP server as "hostname" or "hostname:port". Port 25 if none is given.<br/>
		/// from: The sender's address.<br/>
		/// cc / bcc: A String or <see cref="Array"/> of Strings of further recipients.<br/>
		/// replyto: The address replies should go to, when it differs from <c>from</c>.<br/>
		/// attachments: A String or <see cref="Array"/> of Strings of file paths.<br/>
		/// headers: A <see cref="Map"/> of additional SMTP header names and values.
		/// </param>
		/// <exception cref="ValueError">Thrown if a required option is missing, an option's value has the wrong
		/// shape, an address or port cannot be parsed, or an unrecognized key is present.</exception>
		/// <exception cref="TypeError">Thrown if <paramref name="recipients"/> is neither a String nor an Array of them.</exception>
		/// <exception cref="OSError">Thrown if an attachment cannot be read, or the server rejects the message or cannot be reached.</exception>
		public static object Mail(object recipients, string subject, string message, Map options = null)
		{
			var to = new List<string>();

			if (recipients is string s)
			{
				if (s.Length != 0)
					to.Add(s);
			}
			else if (recipients is Array arr)
			{
				for (var i = 0; i < arr.Count; i++)
					if (arr.array[i] is not string entry)
						return Errors.TypeErrorOccurred($"Recipient {i + 1} is {Errors.Describe(arr.array[i])}; every recipient must be a String.");
					else if (entry.Length == 0)
						return Errors.ValueErrorOccurred($"Recipient {i + 1} is empty.", recipients);
					else
						to.Add(entry);
			}
			else
				return Errors.TypeErrorOccurred(recipients, typeof(string));

			if (to.Count == 0)
				return Errors.ValueErrorOccurred("At least one recipient is required.", recipients);

			string host = null, from = null, replyTo = null;
			var port = 25;
			List<string> cc = null, bcc = null, attachments = null;
			Map headers = null;

			if (options != null)
			{
				foreach (var (key, val) in options)
				{
					var item = key as string;

					if (string.IsNullOrEmpty(item))
						continue;

					switch (item)
					{
						case var x when x.Equals(Keyword_Host, StringComparison.OrdinalIgnoreCase):
						{
							if (Single(val) is not string h)
								return Errors.ValueErrorOccurred($"The \"{item}\" option must be a String.", val);

							// Rightmost colon only, and never one inside a bracketed IPv6 literal.
							var z = h.LastIndexOf(Keyword_Port);

							if (z != -1 && h.LastIndexOf(']') < z)
							{
								if (!int.TryParse(h.AsSpan(z + 1), out port) || port < 1 || port > 65535)
									return Errors.ValueErrorOccurred($"\"{h[(z + 1)..]}\" is not a port between 1 and 65535.", val);

								h = h[..z];
							}

							if (h.Length == 0)
								return Errors.ValueErrorOccurred($"The \"{item}\" option names no server.", val);

							host = h;
							break;
						}

						case var x when x.Equals(Keyword_From, StringComparison.OrdinalIgnoreCase):
							if ((from = Single(val) as string) == null)
								return Errors.ValueErrorOccurred($"The \"{item}\" option must be a String.", val);

							break;

						case var x when x.Equals(Keyword_ReplyTo, StringComparison.OrdinalIgnoreCase):
							if ((replyTo = Single(val) as string) == null)
								return Errors.ValueErrorOccurred($"The \"{item}\" option must be a String.", val);

							break;

						case var x when x.Equals(Keyword_CC, StringComparison.OrdinalIgnoreCase):
							if ((cc = Many(val)) == null)
								return Errors.ValueErrorOccurred($"The \"{item}\" option must be a String or an Array of them.", val);

							break;

						case var x when x.Equals(Keyword_Bcc, StringComparison.OrdinalIgnoreCase):
							if ((bcc = Many(val)) == null)
								return Errors.ValueErrorOccurred($"The \"{item}\" option must be a String or an Array of them.", val);

							break;

						case var x when x.Equals(Keyword_Attachments, StringComparison.OrdinalIgnoreCase):
							if ((attachments = Many(val)) == null)
								return Errors.ValueErrorOccurred($"The \"{item}\" option must be a String or an Array of them.", val);

							break;

						case var x when x.Equals(Keyword_Headers, StringComparison.OrdinalIgnoreCase):
							if ((headers = val as Map) == null)
								return Errors.ValueErrorOccurred($"The \"{item}\" option must be a Map of header names and values.", val);

							break;

						default:
							return Errors.ValueErrorOccurred($"Unknown option \"{item}\". Accepted keys are host, from, cc, bcc, replyto, attachments and headers.", key);
					}
				}
			}

			if (host == null)
				return Errors.ValueErrorOccurred("The \"host\" option is required: there is no default SMTP server.");

			if (from == null)
				return Errors.ValueErrorOccurred("The \"from\" option is required: SMTP has no sender to fall back on.");

			// Every address and attachment below is script-supplied and throws out of the BCL when it cannot be
			// parsed or read, so the whole build-and-send is guarded; the message and client are disposed either
			// way, because an Attachment holds its file open until the message is.
			MailMessage msg = null;
			SmtpClient client = null;

			try
			{
				msg = new MailMessage { Subject = subject, Body = message, From = new MailAddress(from) };

				foreach (var entry in to)
					msg.To.Add(new MailAddress(entry));

				if (cc != null)
					foreach (var entry in cc)
						msg.CC.Add(new MailAddress(entry));

				if (bcc != null)
					foreach (var entry in bcc)
						msg.Bcc.Add(new MailAddress(entry));

				if (replyTo != null)
					msg.ReplyToList.Add(new MailAddress(replyTo));

				if (attachments != null)
					foreach (var entry in attachments)
						msg.Attachments.Add(new Attachment(entry));

				if (headers != null)
					foreach (var (name, value) in headers)
						if (name is string header && header.Length != 0)
							msg.Headers.Add(header, value?.ToString() ?? "");

				client = new SmtpClient(host, port);
				client.Send(msg);
				return DefaultObject;
			}
			catch (FormatException ex)
			{
				return Errors.ValueErrorOccurred(ex.Message);
			}
			catch (ArgumentException ex)
			{
				return Errors.ValueErrorOccurred(ex.Message);
			}
			catch (Exception ex)
			{
				// SmtpException says only "Failure sending mail."; the reason is always one level down.
				return Errors.OSErrorOccurredWithMessage(ex.InnerException?.Message is string inner && inner.Length != 0
						? $"{ex.Message} {inner}"
						: ex.Message);
			}
			finally
			{
				msg?.Dispose();
				client?.Dispose();
			}

			// An option taking exactly one value accepts a bare String or a one-element Array, so a script
			// assembling options can use one shape throughout. Null means it was neither.
			static object Single(object val) =>
			val is string one ? one
			: val is Array a && a.Count == 1 && a.array[0] is string only ? only
			: null;

			// An option taking any number of values. Null means the value was neither, or held a non-String.
			static List<string> Many(object val)
			{
				if (val is string one)
					return one.Length != 0 ? [one] : [];

				if (val is not Array a)
					return null;

				var list = new List<string>(a.Count);

				for (var i = 0; i < a.Count; i++)
					if (a.array[i] is string entry && entry.Length != 0)
						list.Add(entry);
					else
						return null;

				return list;
			}
		}

	}

	/// <summary>
	/// Public interface for network-related functions.
	/// </summary>
	public static class Network
	{
		/// <summary>
		/// Downloads a resource from the internet.
		/// AHK difference: does not allow specifying flags other than 0.
		/// </summary>
		/// <param name="url">URL of the file to download, over http, https or ftp.<br/>
		/// For example, "https://someorg.org" might retrieve the welcome page for that organization.<br/>
		/// An ftp URL may carry credentials as "ftp://user:pass@host/path"; without them the login is anonymous.
		/// </param>
		/// <param name="filename">Specify the name of the file to be created locally, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// Any existing file will be overwritten by the new file.<br/>
		/// </param>
		/// <exception cref="OSError">The transfer failed, or the server refused the request.</exception>
		/// <exception cref="ValueError">The URL is not absolute, its scheme is not one of http, https and ftp, or a
		/// cache flag other than <c>*0</c> was given.</exception>
		public static object Download([UserDeclaredName("URL")] object url, object filename)
		{
			var address = url.As();
			var file = filename.As();
			var noCache = true;

			if (address.StartsWith('*'))
			{
				var splits = address.Split(SpaceTab, 2, StringSplitOptions.RemoveEmptyEntries);

				if (splits.Length == 2)
				{
					// Read as text rather than through a numeric conversion, which answers 0 for anything it
					// cannot make sense of and would let "*abc" pass for "*0".
					if (splits[0] != "*0")
						return Errors.ValueErrorOccurred("Download supports only the *0 cache flag.", splits[0]);

					noCache = false;
					address = splits[1];
				}
			}

			if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
				return Errors.ValueErrorOccurred($"\"{address}\" is not an absolute URL.");

			// Both paths stream to the file rather than buffering, so a download's size does not become the
			// script's memory, and both wait the way AutoHotkey waits: pumping, so timers and the GUI stay alive.
			if (uri.Scheme == Uri.UriSchemeFtp)
				return Ks.Await(Ks.KeysharpTask.Wrap(FtpToFileAsync(uri, file)));

			if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
				return Ks.Http.DownloadTo(uri, file, noCache);

			return Errors.ValueErrorOccurred($"Unsupported URL scheme \"{uri.Scheme}\". Expected http, https or ftp.");
		}

		/// <summary>How long an FTP transfer may stall before it is abandoned, matching Http's default.</summary>
		private const int FtpTimeoutMs = 30_000;

		/// <summary>
		/// Fetches an ftp URL to a file. A path the server refuses as a plain file is listed instead, which is
		/// what AutoHotkey's WinInet download does for a directory URL.
		/// </summary>
		private static async Task<object> FtpToFileAsync(Uri uri, string path)
		{
			try
			{
				try
				{
					await FtpTransferAsync(uri, WebRequestMethods.Ftp.DownloadFile, source =>
					{
						// The response arrives before the file is opened, so a refused request leaves any
						// existing file alone.
						using var destination = new FileStream(path, FileMode.Create);
						source.CopyTo(destination);
						return null;
					}).ConfigureAwait(false);
				}
				catch (WebException ex) when ((ex.Response as FtpWebResponse)?.StatusCode
											  == FtpStatusCode.ActionNotTakenFileUnavailable)
				{
					// A listing is small, so it is read whole before the file is touched: an empty one means the
					// path is neither a file nor a directory, and reporting the server's refusal beats leaving a
					// zero-byte file behind and calling it a success.
					var listing = await FtpTransferAsync(uri, WebRequestMethods.Ftp.ListDirectoryDetails, source =>
									  {
										  using var buffer = new MemoryStream();
										  source.CopyTo(buffer);
										  return buffer.ToArray();
									  }).ConfigureAwait(false);

					if (listing.Length == 0)
						throw;

					await File.WriteAllBytesAsync(path, listing).ConfigureAwait(false);
				}

				return DefaultObject;
			}
			catch (Exception ex) when (ex is WebException or IOException)
			{
				throw (Exception)new OSError(ex, "Download");
			}
		}


		/// <summary>
		/// Runs the whole exchange on a pool thread with the synchronous calls, which are the only ones
		/// <see cref="FtpWebRequest.Timeout"/> and <see cref="FtpWebRequest.ReadWriteTimeout"/> apply to: on the
		/// async pair they have no effect, and a server that stalls after answering would hang the transfer for
		/// good.
		/// </summary>
		private static Task<byte[]> FtpTransferAsync(Uri uri, string method, Func<Stream, byte[]> consume)
			=> Task.Run(() =>
		{
			using var response = NewFtpRequest(uri, method).GetResponse();
			using var source = response.GetResponseStream();
			return consume(source);
		});

		private static FtpWebRequest NewFtpRequest(Uri uri, string method)
		{
			// FtpWebRequest is the only FTP client in the shared framework. It is obsolete rather than removed, and
			// it is what keeps Download's inherited ftp:// URLs working without taking on a dependency.
#pragma warning disable SYSLIB0014
			var request = (FtpWebRequest)WebRequest.Create(new UriBuilder(uri) { UserName = "", Password = "" }.Uri);
#pragma warning restore SYSLIB0014
			request.Method = method;
			request.UseBinary = true;
			request.KeepAlive = false;
			request.Timeout = FtpTimeoutMs;
			request.ReadWriteTimeout = FtpTimeoutMs;

			if (Ks.Http.UserInfoCredentials(uri) is { } credentials)
				request.Credentials = credentials;

			return request;
		}

		/// <summary>
		/// Returns an <see cref="Array"/> of the system's IPv4 addresses.
		/// </summary>
		/// <returns>An <see cref="Array"/> where each element is an IPv4 address string such as "192.168.0.1".</returns>
		public static Array SysGetIPAddresses()
		{
			var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
			var ips = new Array();

			foreach (var address in addresses)
				if (address.AddressFamily == AddressFamily.InterNetwork)
					_ = ips.Push(address.ToString());

			return ips;
		}

		/// <summary>
		/// Internal helper which resolves a host name or IP address.
		/// </summary>
		/// <param name="name">The host name to resolve.</param>
		/// <returns>A <see cref="Dictionary{string, object}"/> with the following key/value pairs:<br/>
		///     Host: The host name.<br/>
		///     Addresses: The list of IP addresses.
		/// </returns>
		internal static Dictionary<string, object> GetHostEntry(string name)
		{
			var entry = Dns.GetHostEntry(name);
			var ips = new string[entry.AddressList.Length];

			for (var i = 0; i < ips.Length; i++)
				ips[i] = entry.AddressList[0].ToString();

			var info = new Dictionary<string, object>
			{
				{ "Host", entry.HostName },
				{ "Addresses", ips }
			};
			return info;
		}
	}
}
