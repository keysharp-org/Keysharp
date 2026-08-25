namespace Keysharp.Builtins
{
	/// <summary>
	/// Public interface for network-related functions.
	/// </summary>
	public partial class Ks
	{
		/// <summary>
		/// Sends an email.
		/// </summary>
		/// <param name="recipients">A list of receivers of the message.</param>
		/// <param name="subject">Subject of the message.</param>
		/// <param name="message">Message body.</param>
		/// <param name="options">A <see cref="Map"/> with any the following optional key/value pairs:<br/>
		/// attachments: A string or <see cref="Array"/> of strings of file paths to send as attachments.<br/>
		/// bcc: A string or <see cref="Array"/> of strings of blind carbon copy recipients.<br/>
		/// cc: A string or <see cref="Array"/> of strings of carbon copy recipients.<br/>
		/// from: A string of comma separated from address.<br/>
		/// replyto: A string of comma separated reply address.<br/>
		/// host: The SMTP client hostname and port string in the form "hostname:port".<br/>
		/// header: A string of additional header information.
		/// </param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any errors occur.</exception>
		public static object Mail(object recipients, string subject, string message, Map options = null)
		{
			var msg = new MailMessage { Subject = subject, Body = message };
			msg.From = new MailAddress(string.Concat(Environment.UserName, "@", Environment.UserDomainName));

			if (recipients is string s)
			{
				msg.To.Add(new MailAddress(s));
			}
			else if (recipients is IEnumerable enumerable)
			{
				foreach (var item in enumerable)
					if (!string.IsNullOrEmpty(item as string))
						msg.To.Add((string)item);
			}
			else
				return Errors.TypeErrorOccurred(recipients, typeof(IEnumerable));

			var smtpHost = "localhost";
			int? smtpPort = null;

			if (options == null)
				goto send;

			foreach (var (key, val) in options)
			{
				var item = key as string;

				if (string.IsNullOrEmpty(item))
					continue;

				string[] value;

				if (val is string s2)
					value = [s2];
				//else if (val is string[] sa)//Probably would never be a raw array of strings.
				//  value = sa;
				else if (val is Array arr)
				{
					value = new string[arr.Count];

					for (var i = 0; i < arr.Count; i++)
						value[i] = arr.array[i].ToString();//Access the underlying ArrayList directly for performance.
				}
				else
					continue;

				switch (item)
				{
					case var x when x.Equals(Keyword_Attachments, StringComparison.OrdinalIgnoreCase):
						foreach (var entry in value)
							if (File.Exists(entry))
								msg.Attachments.Add(new Attachment(entry));

						break;

					case var x when x.Equals(Keyword_Bcc, StringComparison.OrdinalIgnoreCase):
						foreach (var entry in value)
							msg.Bcc.Add(entry);

						break;

					case var x when x.Equals(Keyword_CC, StringComparison.OrdinalIgnoreCase):
						foreach (var entry in value)
							msg.CC.Add(entry);

						break;

					case var x when x.Equals(Keyword_From, StringComparison.OrdinalIgnoreCase):
						msg.From = new MailAddress(value[0]);
						break;

					case var x when x.Equals(Keyword_ReplyTo, StringComparison.OrdinalIgnoreCase):
						msg.ReplyToList.Add(new MailAddress(value[0]));
						break;

					case var x when x.Equals(Keyword_Host, StringComparison.OrdinalIgnoreCase):
					{
						smtpHost = value[0];
						var z = smtpHost.LastIndexOf(Keyword_Port);

						if (z != -1)
						{
							var port = smtpHost.AsSpan(z + 1);
							smtpHost = smtpHost.Substring(0, z);

							if (int.TryParse(port, out var n))
								smtpPort = n;
						}
					}
					break;

					default:
						msg.Headers.Add(item, value[0]);
						break;
				}
			}

			send:
			var client = smtpPort == null ? new SmtpClient(smtpHost) : new SmtpClient(smtpHost, (int)smtpPort);

			try
			{
				client.Send(msg);
				return DefaultObject;
			}
			catch (Exception ex)
			{
				return Errors.ErrorOccurred(ex.Message);
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
		/// <param name="address">URL of the file to download.<br/>
		/// For example, "https://someorg.org" might retrieve the welcome page for that organization.
		/// </param>
		/// <param name="filename">Specify the name of the file to be created locally, which is assumed to be in <see cref="A_WorkingDir"/> if an absolute path isn't specified.<br/>
		/// Any existing file will be overwritten by the new file.<br/>
		/// </param>
		/// <exception cref="Error">An <see cref="Error"/> exception is thrown if any errors occur.</exception>
		public static object Download(object url, object filename)
		{
			var address = url.As();
			var file = filename.As();
			var flags = -1;

			if (address.StartsWith('*'))
			{
				var splits = address.Split(SpaceTab);

				if (splits.Length == 2)
				{
					flags = splits[0].TrimStart('*').Ai();
					address = splits[1];
				}
			}

			var t = Task.Run(async () =>//We explicitly do NOT use Task.Factory.StartNew() here, because it does not understand async delegates.
			{
				try
				{
					using (var client = new HttpClient())
					{
						if (flags != 0)
						{
							client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
							{
								NoCache = true
							};
						}

						var uri = new Uri(address);

						using (var response = await client.GetStreamAsync(address))
						{
							using (var fs = new FileStream(file, FileMode.Create))
							{
								await response.CopyToAsync(fs);
								return true;
							}
						}
					}
				}
				catch (Exception ex)
				{
					_ = Errors.ErrorOccurred(ex.Message);
					return default;
				}
			});
			t.WaitWithoutInterruption();
			return DefaultObject;
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
