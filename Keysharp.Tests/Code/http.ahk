#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon
#import KS { Http, Url, Await, Task, Clr, A_KsVersion, A_DirSeparator }
#Include <assert>

#CSharp
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// A minimal HTTP/1.1 server on the loopback interface, so the suite never touches the network. HttpListener
// is deliberately not used: on Windows it needs a URL reservation a non-elevated test process does not have.
static TcpListener listener;

public static long StartServer()
{
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

static void Accept()
{
    try
    {
        while (true)
        {
            var client = listener.AcceptTcpClient();
            // A client that drops mid-body faults the worker, and an unhandled exception on a background thread
            // would take the test host down with it.
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
        var head = new List<byte>();

        while (head.Count < 4 || head[head.Count - 4] != 13 || head[head.Count - 3] != 10
               || head[head.Count - 2] != 13 || head[head.Count - 1] != 10)
        {
            var b = stream.ReadByte();

            if (b < 0)
                return;

            head.Add((byte)b);
        }

        var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
        var start = lines[0].Split(' ');
        var method = start[0];
        var path = start.Length > 1 ? start[1] : "/";
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');

            if (colon > 0)
                headers[lines[i].Substring(0, colon)] = lines[i].Substring(colon + 1).Trim();
        }

        var length = headers.TryGetValue("Content-Length", out var cl) ? int.Parse(cl) : 0;
        var body = new byte[length];
        var got = 0;

        while (got < length)
        {
            var n = stream.Read(body, got, length - got);

            if (n <= 0)
                break;

            got += n;
        }

        Respond(stream, method, path, headers, body);
    }
}

static void Respond(NetworkStream stream, string method, string path,
                    Dictionary<string, string> headers, byte[] body)
{
    switch (path)
    {
        case "/text":
            Write(stream, 200, "OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("hello"));
            return;

        case "/json":
            Write(stream, 200, "OK", "application/json", Encoding.UTF8.GetBytes("{\"a\":1,\"b\":\"x\"}"));
            return;

        case "/missing":
            Write(stream, 404, "Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("nope"));
            return;

        case "/echo":
            Write(stream, 200, "OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(
                method + "|" + (headers.TryGetValue("Content-Type", out var ct) ? ct : "-")
                + "|" + Encoding.UTF8.GetString(body)));
            return;

        case "/header":
            Write(stream, 200, "OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(
                (headers.TryGetValue("X-Test", out var x) ? x : "-")
                + "|" + (headers.TryGetValue("User-Agent", out var ua) ? ua : "-")));
            return;

        case "/latin":
            // charset names a non-UTF-8 encoding, so Text has to follow it rather than assume UTF-8.
            Write(stream, 200, "OK", "text/plain; charset=iso-8859-1", new byte[] { 0xE4, 0xF6 });
            return;

        case "/big":
            var big = new byte[300000];

            for (var i = 0; i < big.Length; i++)
                big[i] = (byte)('a' + (i % 26));

            Write(stream, 200, "OK", "application/octet-stream", big);
            return;

        case "/bom":
            // A UTF-8 BOM in front of JSON, which several real APIs send.
            var bom = new List<byte> { 0xEF, 0xBB, 0xBF };
            bom.AddRange(Encoding.UTF8.GetBytes("{\"a\":1}"));
            Write(stream, 200, "OK", "application/json", bom.ToArray());
            return;

        case "/cache":
            Write(stream, 200, "OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(
                headers.TryGetValue("Cache-Control", out var cc) ? cc : "-"));
            return;

        case "/setcookie":
            WriteWith(stream, 200, "OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("set"),
                      "Set-Cookie: sid=abc; Path=/\r\n");
            return;

        case "/cookie":
            Write(stream, 200, "OK", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(
                headers.TryGetValue("Cookie", out var ck) ? ck : "-"));
            return;

        case "/redirect":
            WriteWith(stream, 302, "Found", "text/plain; charset=utf-8", new byte[0], "Location: /text\r\n");
            return;

        case "/slow":
            // Written in pieces with gaps wider than the OnData coalescing window, so a streaming callback is
            // called several times and an abort lands mid-body rather than after the last byte.
            var piece = new byte[50000];

            for (var i = 0; i < piece.Length; i++)
                piece[i] = (byte)'x';

            var head = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n"
                + "Content-Length: " + (piece.Length * 6) + "\r\nConnection: close\r\n\r\n");
            stream.Write(head, 0, head.Length);

            for (var n = 0; n < 6; n++)
            {
                stream.Write(piece, 0, piece.Length);
                stream.Flush();
                Thread.Sleep(80);
            }

            return;

        case "/stall":
            // Headers promised and never sent, which is what an idle timeout is for.
            var promise = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 10\r\nConnection: close\r\n\r\n");
            stream.Write(promise, 0, promise.Length);
            stream.Flush();
            Thread.Sleep(10000);
            return;

        default:
            // Named rather than catch-all, so a typo in a test URL fails instead of quietly passing.
            Write(stream, 404, "Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("no route " + path));
            return;
    }
}

// A minimal FTP server: enough of RFC 959 for Download's ftp:// path — login, binary RETR of one file, the 550
// a directory URL is answered with, and the LIST that follows it. Passive mode only, one data transfer per
// connection, which is what FtpWebRequest asks for with KeepAlive off.
static TcpListener ftpListener;
static string ftpLastPath = "";

public static long StartFtpServer()
{
    ftpListener = new TcpListener(IPAddress.Loopback, 0);
    ftpListener.Start();
    new Thread(AcceptFtp) { IsBackground = true }.Start();
    return ((IPEndPoint)ftpListener.LocalEndpoint).Port;
}

public static object StopFtpServer()
{
    ftpListener.Stop();
    return "";
}

static void AcceptFtp()
{
    try
    {
        while (true)
        {
            var client = ftpListener.AcceptTcpClient();
            new Thread(() => { try { HandleFtp(client); } catch { } }) { IsBackground = true }.Start();
        }
    }
    catch (SocketException) { }
    catch (InvalidOperationException) { }
}

static void HandleFtp(TcpClient client)
{
    using (client)
    using (var stream = client.GetStream())
    using (var reader = new StreamReader(stream, Encoding.ASCII))
    {
        TcpListener data = null;
        var user = "";

        Say(stream, "220 Test FTP");

        while (true)
        {
            var line = reader.ReadLine();

            if (line == null)
                return;

            var space = line.IndexOf(' ');
            var verb = (space < 0 ? line : line.Substring(0, space)).ToUpperInvariant();
            var arg = space < 0 ? "" : line.Substring(space + 1);

            switch (verb)
            {
                case "USER": user = arg; Say(stream, "331 Need password"); break;
                case "PASS": Say(stream, user == "bad" ? "530 Not logged in" : "230 Logged in"); break;
                case "OPTS": case "TYPE": case "PWD": case "CWD": Say(stream, "200 Ok"); break;
                case "QUIT": Say(stream, "221 Bye"); return;

                case "PASV":
                    data = new TcpListener(IPAddress.Loopback, 0);
                    data.Start();
                    var port = ((IPEndPoint)data.LocalEndpoint).Port;
                    Say(stream, "227 Entering Passive Mode (127,0,0,1," + (port / 256) + "," + (port % 256) + ")");
                    break;

                case "RETR":
                    ftpLastPath = arg;

                    if (arg.EndsWith("dir"))
                    {
                        Say(stream, "550 Not a plain file");
                        break;
                    }

                    SendData(stream, data, "ftp-file-body");
                    break;

                case "LIST":
                    // A path that is not a directory either lists as nothing, which is how a mistyped remote
                    // filename looks after its RETR has already been refused.
                    SendData(stream, data, ftpLastPath.Contains("nothing")
                                           ? "" : "-rw-r--r-- 1 u g 13 Jan 1 00:00 ftp-file-body");
                    break;

                default: Say(stream, "500 Unknown"); break;
            }
        }
    }
}

static void SendData(NetworkStream control, TcpListener data, string body)
{
    Say(control, "150 Opening data connection");

    using (var peer = data.AcceptTcpClient())
    using (var out_ = peer.GetStream())
    {
        var bytes = Encoding.ASCII.GetBytes(body);
        out_.Write(bytes, 0, bytes.Length);
        out_.Flush();
    }

    data.Stop();
    Say(control, "226 Transfer complete");
}

static void Say(NetworkStream stream, string line)
{
    var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
    stream.Write(bytes, 0, bytes.Length);
    stream.Flush();
}

static void Write(NetworkStream stream, int status, string reason, string contentType, byte[] body)
    => WriteWith(stream, status, reason, contentType, body, "");

static void WriteWith(NetworkStream stream, int status, string reason, string contentType, byte[] body, string extra)
{
    var head = Encoding.ASCII.GetBytes(
        "HTTP/1.1 " + status + " " + reason + "\r\n"
        + "Content-Type: " + contentType + "\r\n"
        + "Content-Length: " + body.Length + "\r\n"
        + extra
        + "Connection: close\r\n\r\n");
    stream.Write(head, 0, head.Length);
    stream.Write(body, 0, body.Length);
    stream.Flush();
}
#EndCSharp

port := StartServer()
root := "http://127.0.0.1:" port

; ---- Url codec -----------------------------------------------------------------------------

AssertEq(Url.Encode("a b&c=d"), "a%20b%26c%3Dd", A_LineNumber)
AssertEq(Url.Encode("aA0-._~"), "aA0-._~", A_LineNumber)         ; the unreserved set is never escaped
AssertEq(Url.Encode("ä"), "%C3%A4", A_LineNumber)                 ; per byte, UTF-8 by default
AssertEq(Url.Encode("ab", "UTF-16"), "a%00b%00", A_LineNumber)
AssertEq(Url.Decode("a%20b%26c"), "a b&c", A_LineNumber)
AssertEq(Url.Decode("%C3%A4"), "ä", A_LineNumber)
AssertEq(Url.Decode("a+b"), "a+b", A_LineNumber)                  ; + means a space only in a form body
AssertEq(Url.Decode("100%"), "100%", A_LineNumber)                ; a lone % stands for itself
AssertEq(Url.Decode(Url.Encode("häl lo/?#")), "häl lo/?#", A_LineNumber)
AssertEq(Url.Decode(Url.Encode("ab", "UTF-16"), "UTF-16"), "ab", A_LineNumber)   ; a wide encoding round-trips
AssertEq(Url.Decode(Url.Encode("😀")), "😀", A_LineNumber)                        ; and so does a surrogate pair
AssertEq(Url.Decode("a😀%20b"), "a😀 b", A_LineNumber)                            ; literal text is not re-encoded
AssertEq(Url.Encode(""), "", A_LineNumber)
Throws(() => Url.Encode("abc", "no-such-encoding"), A_LineNumber, ValueError)

; ---- static shortcuts ----------------------------------------------------------------------------------

r := Http.Get(root "/text")
AssertEq(r.Status, 200, A_LineNumber)
AssertEq(r.StatusText, "OK", A_LineNumber)
AssertEq(r.IsSuccess, true, A_LineNumber)
AssertEq(r.Text, "hello", A_LineNumber)
AssertEq(r.Url, root "/text", A_LineNumber)
AssertEq(r.Headers["Content-Type"], "text/plain; charset=utf-8", A_LineNumber)
AssertEq(r.Headers["content-type"], "text/plain; charset=utf-8", A_LineNumber)  ; case-insensitive
AssertEq(r.Body.Size, 5, A_LineNumber)

; A non-2xx status is an answer, not an error: the body is where the reason is.
r := Http.Get(root "/missing")
AssertEq(r.Status, 404, A_LineNumber)
AssertEq(r.IsSuccess, false, A_LineNumber)
AssertEq(r.Text, "nope", A_LineNumber)

r := Http.Get(root "/json")
AssertEq(r.Json()["a"], 1, A_LineNumber)
AssertEq(r.Json()["b"], "x", A_LineNumber)

; The response charset decides Text, rather than UTF-8 being assumed.
AssertEq(Http.Get(root "/latin").Text, "äö", A_LineNumber)

; A byte-order mark is framing, not content, so it never reaches Text and never breaks Json().
r := Http.Get(root "/bom")
AssertEq(r.Text, '{"a":1}', A_LineNumber)
AssertEq(r.Json()["a"], 1, A_LineNumber)

; Redirects are followed, and Url reports where the response actually came from.
r := Http.Get(root "/redirect")
AssertEq(r.Text, "hello", A_LineNumber)
AssertEq(r.Url, root "/text", A_LineNumber)

; ---- bodies --------------------------------------------------------------------------------------------

AssertEq(Http.Post(root "/echo", "abc").Text, "POST|text/plain; charset=utf-8|abc", A_LineNumber)
AssertEq(Http.Post(root "/echo", , {Json: Map("k", 2)}).Text,
	'POST|application/json; charset=utf-8|{"k":2}', A_LineNumber)
; Request takes its body positionally too, which is what PUT and PATCH need.
AssertEq(Http.Request("PUT", root "/echo", "z").Text, "PUT|text/plain; charset=utf-8|z", A_LineNumber)

b := Buffer(2)
NumPut("UChar", 65, "UChar", 66, b)
AssertEq(Http.Post(root "/echo", b).Text, "POST|application/octet-stream|AB", A_LineNumber)

; An explicit Content-Type replaces the one the body type implies.
AssertEq(Http.Post(root "/echo", "q", {Headers: Map("Content-Type", "text/csv")}).Text,
	"POST|text/csv|q", A_LineNumber)

; Body and Json are both the request body, so asking for both is an error rather than a silent winner.
Throws(() => Http.Post(root "/echo", "a", {Json: 1}), A_LineNumber, ValueError)

; ---- headers -------------------------------------------------------------------------------------------

AssertEq(Http.Get(root "/header", {Headers: Map("X-Test", "v")}).Text, "v|Keysharp/" A_KsVersion, A_LineNumber)

; "" removes a header, including the default User-Agent.
AssertEq(Http.Get(root "/header", {Headers: Map("User-Agent", "")}).Text, "-|-", A_LineNumber)
AssertEq(Http.Get(root "/header", {Headers: Map("User-Agent", "mine")}).Text, "-|mine", A_LineNumber)

; A Content-* header describes a body, so a request without one ignores it rather than failing.
AssertEq(Http.Get(root "/header", {Headers: Map("Content-Type", "text/csv", "X-Test", "v")}).Text,
	"v|Keysharp/" A_KsVersion, A_LineNumber)

; ---- sessions ------------------------------------------------------------------------------------------

api := Http({BaseUrl: root "/", Headers: Map("X-Test", "session")})
AssertEq(api.Get("header").Text, "session|Keysharp/" A_KsVersion, A_LineNumber)
AssertEq(api.Get("text").Text, "hello", A_LineNumber)

; A request merges over the session key by key, and "" removes one for that request only.
AssertEq(api.Get("header", {Headers: Map("X-Test", "once")}).Text, "once|Keysharp/" A_KsVersion, A_LineNumber)
AssertEq(api.Get("header", {Headers: Map("X-Test", "")}).Text, "-|Keysharp/" A_KsVersion, A_LineNumber)
AssertEq(api.Get("header").Text, "session|Keysharp/" A_KsVersion, A_LineNumber)

api.Headers["X-Test"] := "changed"
AssertEq(api.Get("header").Text, "changed|Keysharp/" A_KsVersion, A_LineNumber)
AssertEq(api.BaseUrl, root "/", A_LineNumber)
AssertEq(api.Timeout, 30, A_LineNumber)

; The session's header map compares names case-insensitively, as HTTP does, even though the caller's Map did not.
AssertEq(api.Headers["x-test"], "changed", A_LineNumber)

; A session keeps cookies across its own requests; the stateless shortcuts hold none.
jar := Http({BaseUrl: root "/"})
jar.Get("setcookie")
AssertEq(jar.Get("cookie").Text, "sid=abc", A_LineNumber)
Http.Get(root "/setcookie")
AssertEq(Http.Get(root "/cookie").Text, "-", A_LineNumber)

; An absolute URL ignores BaseUrl.
AssertEq(api.Get(root "/text").Text, "hello", A_LineNumber)

; A body describes one request, so it is not something a session can default.
Throws(() => Http({BaseUrl: root "/", Body: "default"}), A_LineNumber, ValueError)
Throws(() => Http({Json: 5}), A_LineNumber, ValueError)

; BaseUrl is resolved at send time, not baked into the connection, so a request may carry its own.
AssertEq(Http.Get("text", {BaseUrl: root "/"}).Text, "hello", A_LineNumber)

; ---- option validation ---------------------------------------------------------------------------------

Throws(() => Http.Get(root "/text", {Nonsense: 1}), A_LineNumber, ValueError)
Throws(() => Http.Get(root "/text", {Auth: ["u", "p"]}), A_LineNumber, ValueError)  ; session-only
Throws(() => Http.Get(""), A_LineNumber, ValueError)
Throws(() => Http.Get("not-a-url"), A_LineNumber, ValueError)
Throws(() => Http.Get("ftp://example.com/f"), A_LineNumber, ValueError)   ; Http is http and https only
Throws(() => Http({Auth: "Nope"}), A_LineNumber, ValueError)
Throws(() => Http({Proxy: "not a url"}), A_LineNumber, ValueError)

; A body has to be something a request can carry, rather than being stringified into one.
Throws(() => Http.Post(root "/echo", Map("a", 1)), A_LineNumber, TypeError)
Throws(() => Http.Post(root "/echo", , {Body: [1, 2]}), A_LineNumber, TypeError)

; A Handler has to be an HttpMessageHandler, not any value that happens to be given.
Throws(() => Http({Handler: 1}), A_LineNumber, TypeError)

; It is the connection, so the options that would configure one are refused rather than ignored.
handler := Clr.Load("System.Net.Http").System.Net.Http.SocketsHttpHandler()
Throws(() => Http({Handler: handler, Auth: ["u", "p"]}), A_LineNumber, ValueError)
Throws(() => Http({Handler: handler, Proxy: ""}), A_LineNumber, ValueError)
AssertEq(Http({Handler: handler}).Get(root "/text").Text, "hello", A_LineNumber)
Throws(() => Http.Request("bad method", root "/text"), A_LineNumber, ValueError)
Throws(() => Http.Get(root "/text", {Timeout: "soon"}), A_LineNumber, ValueError)
Throws(() => Http.Get(root "/text", {Timeout: 0}), A_LineNumber, ValueError)
Throws(() => Http({BaseUrl: "not a url"}).Get("text"), A_LineNumber, ValueError)

; A closed session refuses further work rather than failing somewhere deeper.
gone := Http()
gone.Close()
Throws(() => gone.Get(root "/text"), A_LineNumber)

; A session accepts what only a session can configure.
authed := Http({BaseUrl: root "/", Auth: ["u", "p"], Proxy: "", IgnoreCertificateErrors: true})
AssertEq(authed.Get("text").Text, "hello", A_LineNumber)

; ---- async ---------------------------------------------------------------------------------------------

t := Http.GetAsync(root "/text")
AssertEq(Type(t), "Task", A_LineNumber)
AssertEq(Await(t).Text, "hello", A_LineNumber)

both := Await(Task.WhenAll(Http.GetAsync(root "/text"), api.GetAsync("json")))
AssertEq(both[1].Text, "hello", A_LineNumber)
AssertEq(both[2].Json()["a"], 1, A_LineNumber)

; An accumulator object, because a fat arrow assigning a bare name would write a local of its own.
acc := {text: "", seen: 0, calls: 0, total: 0}
Await(Http.GetAsync(root "/text").Then(res => acc.text := res.Text))
AssertEq(acc.text, "hello", A_LineNumber)

; ---- OnData --------------------------------------------------------------------------------------------

onData := (chunk, received, total) => (acc.seen += chunk.Size, acc.calls += 1, acc.total := total, 0)
r := Http.Get(root "/big", {OnData: onData})
AssertEq(acc.seen, 300000, A_LineNumber)
AssertEq(acc.total, 300000, A_LineNumber)
Assert(acc.calls >= 1, A_LineNumber)

; The bytes went to the callback, so the response carries none.
AssertEq(r.Body.Size, 0, A_LineNumber)
AssertEq(r.Text, "", A_LineNumber)
AssertEq(r.Status, 200, A_LineNumber)

; A callback may declare fewer than three parameters, as every other callback in the language may.
acc.seen := 0, acc.calls := 0
Http.Get(root "/big", {OnData: chunk => (acc.seen += chunk.Size, 0)})
AssertEq(acc.seen, 300000, A_LineNumber)
acc.calls := 0
Http.Get(root "/big", {OnData: () => (acc.calls += 1, 0)})
Assert(acc.calls >= 1, A_LineNumber)

; The delivered bytes are the body, reassembled across whatever chunk boundaries the flush rules produced.
acc.text := ""
Http.Get(root "/big", {OnData: (chunk, *) => (acc.text .= StrGet(chunk, chunk.Size, "CP0"), 0)})
AssertEq(StrLen(acc.text), 300000, A_LineNumber)
AssertEq(SubStr(acc.text, 1, 3), "abc", A_LineNumber)
AssertEq(SubStr(acc.text, 27, 1), "a", A_LineNumber)      ; the pattern wraps every 26 bytes

; /slow arrives in pieces, so a streaming callback is called several times with a Received that only grows.
acc.calls := 0, acc.seen := 0, acc.total := 0
Http.Get(root "/slow", {OnData: (chunk, received) => (acc.calls += 1,
	Assert(received > acc.seen, A_LineNumber), acc.seen := received, 0)})
Assert(acc.calls >= 2, A_LineNumber)
AssertEq(acc.seen, 300000, A_LineNumber)

; A non-zero Integer stops the transfer, and it stops it where it is: /slow arrives in pieces, so aborting on
; the first delivery must leave most of the body unread rather than merely throwing after the last byte.
acc.seen := 0
Throws(() => Http.Get(root "/slow", {OnData: (chunk, *) => (acc.seen += chunk.Size, 1)}), A_LineNumber)
Assert(acc.seen < 300000, A_LineNumber)

; Anything else keeps going: 0, and a value which is not an Integer at all.
acc.seen := 0
Http.Get(root "/big", {OnData: (chunk, *) => (acc.seen += chunk.Size, "stop")})
AssertEq(acc.seen, 300000, A_LineNumber)

; A callback that raises fails the request rather than being swallowed.
httpUnhandled := []
RecordHttpUnhandled(exception, mode) {
	httpUnhandled.Push(exception.Message " " mode)
}
OnError(RecordHttpUnhandled)
Throws(() => Http.Get(root "/big", {OnData: (*) => Chr(-1)}), A_LineNumber)
AssertEq(httpUnhandled.Length, 0, A_LineNumber)
OnError(RecordHttpUnhandled, 0)

; Aborting an async transfer cancels its task rather than raising where it was started.
t := Http.GetAsync(root "/big", {OnData: (*) => 1})
Throws(() => Await(t), A_LineNumber)
AssertEq(t.Status, "Canceled", A_LineNumber)

; Delivery is dispatch of data that has already arrived, so Critical does not stall it and a raised thread
; priority does not drop it. Either would hang or fail a synchronous streamed request.
acc.seen := 0
Critical "On"
Http.Get(root "/slow", {OnData: (chunk, *) => (acc.seen += chunk.Size, 0)})
Critical "Off"
AssertEq(acc.seen, 300000, A_LineNumber)

acc.seen := 0
Thread "Priority", 1
Http.Get(root "/big", {OnData: (chunk, *) => (acc.seen += chunk.Size, 0)})
Thread "Priority", 0
AssertEq(acc.seen, 300000, A_LineNumber)

; ---- Http.Download -------------------------------------------------------------------------------------

; Straight to a file, carrying the session's headers and credentials, which the global Download cannot do.
dl := A_Temp A_DirSeparator "ks-http-dl.bin"
saved := Http.Download(root "/text", dl)
AssertEq(FileRead(dl), "hello", A_LineNumber)
AssertEq(saved.Status, 200, A_LineNumber)
AssertEq(saved.Body.Size, 0, A_LineNumber)        ; the file took the body
AssertEq(api.Download("header", dl).Status, 200, A_LineNumber)
AssertEq(FileRead(dl), "changed|Keysharp/" A_KsVersion, A_LineNumber)
AssertEq(Await(Http.DownloadAsync(root "/text", dl)).Status, 200, A_LineNumber)
AssertEq(FileRead(dl), "hello", A_LineNumber)
; The file and OnData are both the body's sink, so asking for both is an error rather than a silent winner,
; and the file is left as it was.
FileDelete(dl)
FileAppend("keep", dl)
Throws(() => Http.Download(root "/text", dl, {OnData: (*) => 0}), A_LineNumber, ValueError)
AssertEq(FileRead(dl), "keep", A_LineNumber)

; A session's OnData default belongs to its other calls: a download ignores it and reads back as a property.
streamer := Http({BaseUrl: root "/", OnData: (chunk, *) => (acc.seen += chunk.Size, 0)})
Assert(streamer.OnData is Func, A_LineNumber)
acc.seen := 0
AssertEq(streamer.Get("text").Body.Size, 0, A_LineNumber)
AssertEq(acc.seen, 5, A_LineNumber)
acc.seen := 0
AssertEq(streamer.Download("text", dl).Status, 200, A_LineNumber)
AssertEq(FileRead(dl), "hello", A_LineNumber)
AssertEq(acc.seen, 0, A_LineNumber)
streamer.OnData := ""
AssertEq(streamer.OnData, "", A_LineNumber)
AssertEq(streamer.Get("text").Text, "hello", A_LineNumber)

; A non-2xx body is saved like any other, so IsSuccess is the test before trusting the file.
AssertEq(Http.Download(root "/missing", dl).Status, 404, A_LineNumber)
AssertEq(FileRead(dl), "nope", A_LineNumber)
FileDelete(dl)

; ---- timeouts and transport failures ------------------------------------------------------------------

; The timeout is idle rather than total. /slow takes about 400 ms in 80 ms steps, so a 0.3 s budget is under
; the total and over every gap: a total timeout would raise here, and an idle one does not.
AssertEq(Http.Get(root "/slow", {Timeout: 0.3}).Body.Size, 300000, A_LineNumber)
Throws(() => Http.Get(root "/stall", {Timeout: 0.3}), A_LineNumber, TimeoutError)

; A host that cannot be reached is an OSError, not a status. Port 0 fails the connect at once, where a refused port is
; retried for about two seconds on Windows.
Throws(() => Http.Get("http://127.0.0.1:0/nothing"), A_LineNumber, OSError)

; Http.Response is nameable but not constructible from script.
AssertEq(Type(Http.Get(root "/text")), "Http.Response", A_LineNumber)

; ---- Download ------------------------------------------------------------------------------------------

tmp := A_Temp A_DirSeparator "ks-http-test.bin"
Download(root "/text", tmp)
AssertEq(FileRead(tmp), "hello", A_LineNumber)
FileDelete(tmp)

; By default every cache along the way is asked for a fresh copy; *0 permits caching, as in AutoHotkey.
AssertEq(Http.Get(root "/cache").Text, "-", A_LineNumber)
Download(root "/cache", tmp)
AssertEq(FileRead(tmp), "no-cache", A_LineNumber)
Download("*0 " root "/cache", tmp)
AssertEq(FileRead(tmp), "-", A_LineNumber)
FileDelete(tmp)

; A server that answers an error with a page saves that page, as AutoHotkey's WinInet download does.
Download(root "/missing", tmp)
AssertEq(FileRead(tmp), "nope", A_LineNumber)
FileDelete(tmp)
Throws(() => Download("not-a-url", tmp), A_LineNumber, ValueError)
Throws(() => Download("gopher://example.com/x", tmp), A_LineNumber, ValueError)
Throws(() => Download("*1 " root "/text", tmp), A_LineNumber, ValueError)   ; only *0 is supported

; A download that never reaches a reply leaves an existing file alone.
FileAppend("keep", tmp)
Throws(() => Download("http://127.0.0.1:0/nothing", tmp), A_LineNumber, OSError)
AssertEq(FileRead(tmp), "keep", A_LineNumber)
FileDelete(tmp)

; Download also takes ftp, which Http deliberately does not.
ftpRoot := "ftp://127.0.0.1:" StartFtpServer()
Download(ftpRoot "/pub/file.bin", tmp)
AssertEq(FileRead(tmp), "ftp-file-body", A_LineNumber)
FileDelete(tmp)

; Credentials ride in the URL's userinfo, and a rejected login is an OSError.
Download(StrReplace(ftpRoot, "://", "://good:pw@") "/pub/file.bin", tmp)
AssertEq(FileRead(tmp), "ftp-file-body", A_LineNumber)
FileDelete(tmp)
Throws(() => Download(StrReplace(ftpRoot, "://", "://bad:pw@") "/pub/file.bin", tmp), A_LineNumber, OSError)

; A path the server refuses as a file is listed instead, as WinInet's download does for a directory URL.
Download(ftpRoot "/pub/dir", tmp)
Assert(InStr(FileRead(tmp), "ftp-file-body"), A_LineNumber)
FileDelete(tmp)

; A path that is neither a file nor a directory reports the server's refusal, rather than leaving an empty file
; behind and calling it a success.
FileAppend("keep", tmp)
Throws(() => Download(ftpRoot "/pub/nothing-dir", tmp), A_LineNumber, OSError)
AssertEq(FileRead(tmp), "keep", A_LineNumber)
FileDelete(tmp)
StopFtpServer()

; ---- escape hatches ------------------------------------------------------------------------------------

Assert(IsObject(api.ToClr()), A_LineNumber)
Assert(Http.Get(root "/text").ToClr().IsSuccessStatusCode, A_LineNumber)

StopServer()
FileAppend "pass", "*"
