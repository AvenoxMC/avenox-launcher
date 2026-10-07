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
	// Same transport as the Eaglercraft browser client's "Relay" mode: one WebSocket
	// per connection to <relay>?target=host:port, carrying the raw Java TCP stream.
	// The Eagler Relay mod points every remote connection at this local port; the
	// target is read from the Minecraft handshake packet the game sends first.
	class RelayBridge
	{
		readonly string relay;
		readonly Action<string> log;
		TcpListener listener;
		volatile bool running;
		public int Port { get; private set; }

		public RelayBridge(string relayUrl, Action<string> log)
		{
			string r = relayUrl.Trim();
			if (!r.Contains("://")) r = "wss://" + r;
			relay = r;
			this.log = log;
		}

		public void Start(int preferredPort)
		{
			for (int p = preferredPort; p < preferredPort + 20; p++)
			{
				try
				{
					listener = new TcpListener(IPAddress.Loopback, p);
					listener.Start();
					Port = p;
					break;
				}
				catch (SocketException) { listener = null; }
			}
			if (listener == null) throw new IOException("aucun port local libre pour le relais");
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

		static async Task<int> ReadVarInt(Stream s, MemoryStream copy)
		{
			int value = 0, shift = 0;
			var one = new byte[1];
			for (;;)
			{
				if (await s.ReadAsync(one, 0, 1) != 1) throw new EndOfStreamException();
				copy.WriteByte(one[0]);
				value |= (one[0] & 0x7f) << shift;
				if ((one[0] & 0x80) == 0) return value;
				shift += 7;
				if (shift > 28) throw new InvalidDataException("VarInt trop long");
			}
		}

		static int VarInt(byte[] b, ref int pos)
		{
			int value = 0, shift = 0;
			for (;;)
			{
				byte x = b[pos++];
				value |= (x & 0x7f) << shift;
				if ((x & 0x80) == 0) return value;
				shift += 7;
			}
		}

		async Task Handle(TcpClient client)
		{
			var cts = new CancellationTokenSource();
			string target = "?";
			try
			{
				client.NoDelay = true;
				var tcp = client.GetStream();

				// Handshake: [len][0x00][protocol][host][port u16 BE][intent]
				var first = new MemoryStream();
				int len = await ReadVarInt(tcp, first);
				if (len <= 0 || len > 4096) throw new InvalidDataException("premier paquet inattendu");
				var body = new byte[len];
				for (int got = 0; got < len; )
				{
					int n = await tcp.ReadAsync(body, got, len - got);
					if (n <= 0) throw new EndOfStreamException();
					got += n;
				}
				first.Write(body, 0, len);
				int pos = 0;
				if (VarInt(body, ref pos) != 0) throw new InvalidDataException("ce n'est pas un handshake Minecraft");
				VarInt(body, ref pos);  // protocol version
				int hostLen = VarInt(body, ref pos);
				string host = Encoding.UTF8.GetString(body, pos, hostLen);
				pos += hostLen;
				int port = (body[pos] << 8) | body[pos + 1];
				int nul = host.IndexOf('\0');  // Forge-style markers
				if (nul >= 0) host = host.Substring(0, nul);
				host = host.TrimEnd('.');
				target = port == 25565 ? host : host + ":" + port;

				await Tunnel(relay, target, tcp, first.ToArray(), cts.Token);
			}
			catch (Exception e)
			{
				if (!cts.IsCancellationRequested) log("[relais] " + target + " : " + e.GetBaseException().Message);
			}
			finally
			{
				cts.Cancel();
				try { client.Close(); } catch { }
			}
		}

		// Opens <relay>?target=host[:port] and pipes the raw TCP stream both ways.
		public static async Task Tunnel(string relay, string target, Stream tcp, byte[] first, CancellationToken ct)
		{
			using (var ws = new ClientWebSocket())
			{
				try
				{
					ws.Options.Proxy = WebRequest.GetSystemWebProxy();
					ws.Options.Proxy.Credentials = CredentialCache.DefaultCredentials;
				}
				catch { }
				ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
				string sep = relay.Contains("?") ? "&" : "?";
				await ws.ConnectAsync(new Uri(relay + sep + "target=" + Uri.EscapeDataString(target)), ct);
				try
				{
					if (first != null && first.Length > 0)
						await ws.SendAsync(new ArraySegment<byte>(first), WebSocketMessageType.Binary, true, ct);
					var down = Task.Run(async () =>
					{
						var buf = new byte[65536];
						for (;;)
						{
							var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
							if (r.MessageType == WebSocketMessageType.Close) break;
							if (r.MessageType == WebSocketMessageType.Binary && r.Count > 0) await tcp.WriteAsync(buf, 0, r.Count, ct);
						}
					});
					var up = Task.Run(async () =>
					{
						var buf = new byte[32768];
						for (;;)
						{
							int n = await tcp.ReadAsync(buf, 0, buf.Length, ct);
							if (n <= 0) break;
							await ws.SendAsync(new ArraySegment<byte>(buf, 0, n), WebSocketMessageType.Binary, true, ct);
						}
					});
					await Task.WhenAny(down, up);
				}
				finally { try { ws.Abort(); } catch { } }
			}
		}
	}
}
