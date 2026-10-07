using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EaglerLauncher
{
	// Exposes a Wisp relay (the "Wisp" connection mode of Eaglercraft) as a local
	// TCP port: Minecraft connects to 127.0.0.1:<port> and every TCP connection is
	// tunnelled as one Wisp v1 stream over wss://, i.e. through port 443.
	class WispBridge
	{
		const byte CONNECT = 1, DATA = 2, CONTINUE = 3, CLOSE = 4;
		const int Stream = 1;
		const int Chunk = 16 * 1024;

		readonly Uri relay;
		readonly string host;
		readonly int port;
		readonly Action<string> log;
		TcpListener listener;
		volatile bool running;

		public readonly int LocalPort;
		public readonly string Name;

		public WispBridge(string name, string relayUrl, string destination, int localPort, Action<string> log)
		{
			Name = name;
			string r = relayUrl.Trim();
			if (!r.Contains("://")) r = "wss://" + r;
			if (!r.EndsWith("/")) r += "/";
			relay = new Uri(r);
			ParseHostPort(destination, out host, out port);
			LocalPort = localPort;
			this.log = log;
		}

		public static void ParseHostPort(string s, out string h, out int p)
		{
			s = (s ?? "").Trim();
			int i = s.LastIndexOf(':');
			if (i > 0 && s.IndexOf(']') < i && int.TryParse(s.Substring(i + 1), out p)) h = s.Substring(0, i);
			else { h = s; p = 25565; }
			h = h.Trim('[', ']');
		}

		public void Start()
		{
			listener = new TcpListener(IPAddress.Loopback, LocalPort);
			listener.Start();
			running = true;
			Task.Run(async () =>
			{
				while (running)
				{
					TcpClient c;
					try { c = await listener.AcceptTcpClientAsync(); }
					catch { break; }
					var _ = Handle(c);
				}
			});
		}

		public void Stop()
		{
			running = false;
			try { listener.Stop(); } catch { }
		}

		static byte[] Packet(byte type, int stream, byte[] payload, int offset, int count)
		{
			var b = new byte[5 + count];
			b[0] = type;
			BitConverter.GetBytes(stream).CopyTo(b, 1);  // little-endian on Windows
			if (count > 0) Buffer.BlockCopy(payload, offset, b, 5, count);
			return b;
		}

		async Task<byte[]> Receive(ClientWebSocket ws, CancellationToken ct)
		{
			var buf = new byte[65536];
			var ms = new MemoryStream();
			for (;;)
			{
				var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
				if (r.MessageType == WebSocketMessageType.Close) return null;
				ms.Write(buf, 0, r.Count);
				if (r.EndOfMessage)
				{
					if (r.MessageType == WebSocketMessageType.Text) continue;  // not part of Wisp
					return ms.ToArray();
				}
			}
		}

		async Task Handle(TcpClient client)
		{
			var cts = new CancellationTokenSource();
			var ws = new ClientWebSocket();
			try
			{
				client.NoDelay = true;
				try
				{
					ws.Options.Proxy = WebRequest.GetSystemWebProxy();
					ws.Options.Proxy.Credentials = CredentialCache.DefaultCredentials;
				}
				catch { }
				ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
				// No Sec-WebSocket-Protocol header: the relay then speaks Wisp v1.
				await ws.ConnectAsync(relay, cts.Token);

				int credit = -1;
				while (credit < 0)
				{
					var first = await Receive(ws, cts.Token);
					if (first == null) throw new IOException("le relais Wisp a fermé la connexion");
					if (first.Length >= 9 && first[0] == CONTINUE && BitConverter.ToInt32(first, 1) == 0)
						credit = BitConverter.ToInt32(first, 5);
				}

				var dest = Encoding.UTF8.GetBytes(host);
				var connect = new byte[3 + dest.Length];
				connect[0] = 0x01;  // TCP
				connect[1] = (byte)(port & 0xff);
				connect[2] = (byte)(port >> 8);
				dest.CopyTo(connect, 3);
				await ws.SendAsync(new ArraySegment<byte>(Packet(CONNECT, Stream, connect, 0, connect.Length)), WebSocketMessageType.Binary, true, cts.Token);

				var tcp = client.GetStream();
				var creditSignal = new SemaphoreSlim(0);
				int creditBox = credit;

				// Relay -> game.
				var down = Task.Run(async () =>
				{
					for (;;)
					{
						var p = await Receive(ws, cts.Token);
						if (p == null || p.Length < 5) break;
						int sid = BitConverter.ToInt32(p, 1);
						if (sid != Stream) continue;
						if (p[0] == DATA) await tcp.WriteAsync(p, 5, p.Length - 5, cts.Token);
						else if (p[0] == CONTINUE && p.Length >= 9)
						{
							Interlocked.Exchange(ref creditBox, BitConverter.ToInt32(p, 5));
							creditSignal.Release();
						}
						else if (p[0] == CLOSE)
						{
							if (p.Length > 5 && p[5] != 0x02) log("[" + Name + "] le relais a fermé la connexion (code 0x" + p[5].ToString("x2") + ")");
							break;
						}
					}
				});

				// Game -> relay, respecting the relay's buffer (CONTINUE) credits.
				var up = Task.Run(async () =>
				{
					var buf = new byte[Chunk];
					for (;;)
					{
						int n = await tcp.ReadAsync(buf, 0, buf.Length, cts.Token);
						if (n <= 0) break;
						while (Volatile.Read(ref creditBox) <= 0) await creditSignal.WaitAsync(cts.Token);
						Interlocked.Decrement(ref creditBox);
						await ws.SendAsync(new ArraySegment<byte>(Packet(DATA, Stream, buf, 0, n)), WebSocketMessageType.Binary, true, cts.Token);
					}
					var reason = new byte[] { 0x02 };
					await ws.SendAsync(new ArraySegment<byte>(Packet(CLOSE, Stream, reason, 0, 1)), WebSocketMessageType.Binary, true, cts.Token);
				});

				await Task.WhenAny(down, up);
			}
			catch (Exception e)
			{
				if (!cts.IsCancellationRequested) log("[" + Name + "] " + e.Message);
			}
			finally
			{
				cts.Cancel();
				try { ws.Abort(); } catch { }
				ws.Dispose();
				try { client.Close(); } catch { }
			}
		}
	}
}
