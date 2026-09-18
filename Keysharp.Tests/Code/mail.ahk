#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { Mail, A_DirSeparator }
#Include <assert>

#CSharp
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// A minimal SMTP server on the loopback interface, so the suite never touches the network. It speaks just
// enough of RFC 5321 for SmtpClient to complete a transaction, and records the envelope it was given.
static TcpListener listener;
static readonly List<string> conversation = new List<string>();

public static long StartServer()
{
    lock (conversation)
        conversation.Clear();

    if (listener != null)
        try { listener.Stop(); } catch { }

    listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    new Thread(Accept) { IsBackground = true }.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

public static object StopServer()
{
    listener.Stop();
    return "";
}

// The whole conversation, newline-separated, so a script can assert on what actually reached the server.
public static string Transcript()
{
    lock (conversation)
        return string.Join("\n", conversation);
}

static void Accept()
{
    try
    {
        while (true)
        {
            var client = listener.AcceptTcpClient();
            new Thread(() => { try { Handle(client); } catch { } }) { IsBackground = true }.Start();
        }
    }
    catch (SocketException) { }  // Stop() unblocks the accept by tearing the socket down.
    catch (InvalidOperationException) { }
}

static void Handle(TcpClient client)
{
    using (client)
    using (var stream = client.GetStream())
    {
        var reader = new StreamReader(stream, Encoding.ASCII);
        Say(stream, "220 localhost");

        string line;

        while ((line = reader.ReadLine()) != null)
        {
            var verb = line.Length >= 4 ? line.Substring(0, 4).ToUpperInvariant() : line.ToUpperInvariant();

            lock (conversation)
                conversation.Add(line);

            if (verb == "EHLO" || verb == "HELO")
                Say(stream, "250 localhost");
            else if (verb == "MAIL" || verb == "RCPT" || verb == "RSET")
                Say(stream, "250 OK");
            else if (verb == "DATA")
            {
                Say(stream, "354 End with a dot");

                while ((line = reader.ReadLine()) != null && line != ".")
                    lock (conversation)
                        conversation.Add("> " + line);

                Say(stream, "250 Queued");
            }
            else if (verb == "QUIT")
            {
                Say(stream, "221 Bye");
                return;
            }
            else
                Say(stream, "250 OK");
        }
    }
}

static void Say(NetworkStream stream, string text)
{
    var bytes = Encoding.ASCII.GetBytes(text + "\r\n");
    stream.Write(bytes, 0, bytes.Length);
    stream.Flush();
}
#EndCSharp

port := StartServer()
host := "127.0.0.1:" port

; ---- required options -------------------------------------------------------------------------------

; SMTP has no default server and no default sender, so neither is invented.
Throws(() => Mail("a@example.com", "s", "b"), A_LineNumber, ValueError)
Throws(() => Mail("a@example.com", "s", "b", Map("from", "me@example.com")), A_LineNumber, ValueError)
Throws(() => Mail("a@example.com", "s", "b", Map("host", host)), A_LineNumber, ValueError)

; ---- recipients -------------------------------------------------------------------------------------

opts := Map("host", host, "from", "me@example.com")

Throws(() => Mail("", "s", "b", opts), A_LineNumber, ValueError)
Throws(() => Mail([], "s", "b", opts), A_LineNumber, ValueError)
Throws(() => Mail([""], "s", "b", opts), A_LineNumber, ValueError)
Throws(() => Mail([1], "s", "b", opts), A_LineNumber, TypeError)
Throws(() => Mail(Map(), "s", "b", opts), A_LineNumber, TypeError)

; ---- options ----------------------------------------------------------------------------------------

; A typo is refused rather than silently becoming an SMTP header.
Throws(() => Mail("a@example.com", "s", "b", Map("host", host, "from", "me@example.com", "hosts", host)), A_LineNumber, ValueError)

; options is typed, so a non-Map cannot reach the body.
Throws(() => Mail("a@example.com", "s", "b", "host=" host), A_LineNumber, TypeError)

; A port which is not one is an error, not a silently truncated host.
for badHost in ["127.0.0.1:", "127.0.0.1:abc", "127.0.0.1:0", "127.0.0.1:70000", ":25"]
	Throws(() => Mail("a@example.com", "s", "b", Map("host", badHost, "from", "me@example.com")), A_LineNumber, ValueError)

; An address which cannot be parsed names itself.
Throws(() => Mail("not an address", "s", "b", opts), A_LineNumber, ValueError)
Throws(() => Mail("a@example.com", "s", "b", Map("host", host, "from", "not an address")), A_LineNumber, ValueError)

; An attachment which is not there is reported, where it used to be dropped in silence.
Throws(() => Mail("a@example.com", "s", "b", Map("host", host, "from", "me@example.com",
	"attachments", A_Temp A_DirSeparator "keysharp-mail-no-such-file.bin")), A_LineNumber, OSError)

; ---- a complete transaction -------------------------------------------------------------------------

Mail(["one@example.com", "two@example.com"], "Subject line", "Body text",
	Map("host", host,
		"from", "me@example.com",
		"cc", "three@example.com",
		"replyto", "reply@example.com",
		"headers", Map("X-Keysharp", "1")))

sent := Transcript()

Assert(InStr(sent, "MAIL FROM:<me@example.com>"), A_LineNumber)
Assert(InStr(sent, "RCPT TO:<one@example.com>"), A_LineNumber)
Assert(InStr(sent, "RCPT TO:<two@example.com>"), A_LineNumber)
Assert(InStr(sent, "RCPT TO:<three@example.com>"), A_LineNumber)
Assert(InStr(sent, "> Subject: Subject line"), A_LineNumber)
Assert(InStr(sent, "> Reply-To: reply@example.com"), A_LineNumber)
Assert(InStr(sent, "> X-Keysharp: 1"), A_LineNumber)
Assert(InStr(sent, "> Body text"), A_LineNumber)

; Bcc is an envelope recipient which must not appear in the headers.
port2 := StartServer()
Mail("one@example.com", "s", "b", Map("host", "127.0.0.1:" port2, "from", "me@example.com", "bcc", "hidden@example.com"))
sent := Transcript()

Assert(InStr(sent, "RCPT TO:<hidden@example.com>"), A_LineNumber)
Assert(!InStr(sent, "> Bcc:"), A_LineNumber)

; ---- an unreachable server --------------------------------------------------------------------------

; 0.0.0.0 fails the connect at once; a refused loopback port is retried for about two seconds on Windows.
StopServer()
Throws(() => Mail("a@example.com", "s", "b", Map("host", "0.0.0.0:" port2, "from", "me@example.com")), A_LineNumber, OSError)

FileAppend "pass", "*"
