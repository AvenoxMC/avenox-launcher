using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EaglerLauncher
{
	public class ServerEntry
	{
		public string Name { get; set; }
		// "direct" = Java TCP host:port, "wisp" = tunnel through a Wisp relay,
		// "eaglerx" = Java connection to the TCP side of an EaglerX proxy.
		public string Type { get; set; }
		public string Address { get; set; }
		public string Relay { get; set; }
		public string Packs { get; set; }   // prompt | accept | refuse
		public int LocalPort { get; set; }

		public string TypeLabel()
		{
			if (Type == "wisp") return "Wisp";
			if (Type == "eaglerx") return "EaglerX";
			return "Java direct";
		}

		// Address written into servers.dat / used for quick play.
		public string GameAddress()
		{
			if (Type == "wisp") return "127.0.0.1:" + LocalPort;
			if (Type == "eaglerx") return EaglerXToJava(Address);
			return Address;
		}

		// wss://host[:port][/path] -> host:port. A TLS port (443) or no port means the
		// proxy sits behind a web server, so the Java listener is assumed on 25565.
		public static string EaglerXToJava(string addr)
		{
			Uri u;
			string a = (addr ?? "").Trim();
			if (!a.Contains("://")) a = "wss://" + a;
			if (!Uri.TryCreate(a, UriKind.Absolute, out u)) return addr;
			int port = u.IsDefaultPort || u.Port == 443 || u.Port == 80 ? 25565 : u.Port;
			return u.Host + (port == 25565 ? "" : ":" + port);
		}
	}

	public class Config
	{
		public string Username { get; set; }
		public string Version { get; set; }
		public int MemoryMb { get; set; }
		public bool OptimizationMods { get; set; }
		public string JoinOnLaunch { get; set; }
		public bool CloseOnLaunch { get; set; }
		public string ExtraJvmArgs { get; set; }
		// "relay" = every server goes through the WebSocket relay (like the browser
		// version, works when the network blocks Minecraft ports), "direct" = plain TCP.
		public string NetworkMode { get; set; }
		public string RelayUrl { get; set; }

		// "offline" (username only) or "microsoft". Tokens are DPAPI-encrypted.
		public string AuthMode { get; set; }
		public string MsRefreshToken { get; set; }
		public string MsName { get; set; }
		public string MsUuid { get; set; }
		public string MsXuid { get; set; }
		public string McToken { get; set; }
		public long McTokenExpiryTicks { get; set; }

		// The player's choice; signing out keeps it, so the next launch asks to sign in again.
		public bool Microsoft() { return AuthMode == "microsoft"; }
		public bool SignedIn() { return !string.IsNullOrEmpty(MsRefreshToken); }

		public void SignOut()
		{
			MsRefreshToken = MsName = MsUuid = MsXuid = McToken = null;
			McTokenExpiryTicks = 0;
		}

		public const string DefaultRelay = "wss://eagler-minecraft-relay.u2471966200.workers.dev/minecraft";
		public bool RelayEnabled() { return NetworkMode != "direct"; }
		public List<ServerEntry> Servers { get; set; }
		public List<string> ManagedServerIps { get; set; }

		public static string DataDir
		{
			get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EaglerJavaLauncher"); }
		}
		public static string GameDir { get { return Path.Combine(DataDir, "jeu"); } }
		static string FilePath { get { return Path.Combine(DataDir, "launcher.json"); } }

		public static Config Load()
		{
			Config c = null;
			try { if (File.Exists(FilePath)) c = Json.Parse<Config>(File.ReadAllText(FilePath, Encoding.UTF8)); } catch { }
			if (c == null) c = new Config();
			if (string.IsNullOrEmpty(c.Version)) c.Version = "26.2";
			if (c.MemoryMb <= 0) c.MemoryMb = DefaultMemory();
			if (c.Servers == null) { c.Servers = new List<ServerEntry>(); c.OptimizationMods = true; }
			if (c.ManagedServerIps == null) c.ManagedServerIps = new List<string>();
			if (c.ExtraJvmArgs == null) c.ExtraJvmArgs = "";
			if (string.IsNullOrEmpty(c.NetworkMode)) c.NetworkMode = "relay";
			if (string.IsNullOrEmpty(c.AuthMode)) c.AuthMode = "offline";
			if (string.IsNullOrWhiteSpace(c.RelayUrl)) c.RelayUrl = DefaultRelay;
			if (string.IsNullOrEmpty(c.Username)) c.Username = "Joueur" + new Random().Next(1000, 9999);
			return c;
		}

		public void Save()
		{
			Directory.CreateDirectory(DataDir);
			File.WriteAllText(FilePath, Json.Write(this), Encoding.UTF8);
		}

		// A quarter of the RAM, clamped to 2-6 GB: enough for Sodium without
		// starving the browser/OS on 8 GB school machines.
		static int DefaultMemory()
		{
			try
			{
				var info = new Microsoft.VisualBasic.Devices.ComputerInfo();
				long total = (long)(info.TotalPhysicalMemory / (1024 * 1024));
				return (int)Math.Max(2048, Math.Min(6144, total / 4 / 512 * 512));
			}
			catch { return 3072; }
		}

		public int FreeBridgePort()
		{
			for (int p = 27100; p < 27300; p++)
				if (!Servers.Any(s => s.LocalPort == p)) return p;
			return 27300;
		}
	}

	static class ServerList
	{
		// Rewrites the launcher-managed entries of servers.dat, keeping servers the
		// player added in game.
		public static void Sync(Config cfg)
		{
			string path = Path.Combine(Config.GameDir, "servers.dat");
			NbtCompound root = null;
			try { if (File.Exists(path)) root = Nbt.Read(File.ReadAllBytes(path)); } catch { root = null; }
			if (root == null) root = new NbtCompound();
			var list = root.Get("servers");
			var kept = new List<NbtTag>();
			if (list != null && list.Type == 9)
			{
				foreach (var t in (List<NbtTag>)list.Value)
				{
					var c = t.Value as NbtCompound;
					if (c == null) continue;
					if (cfg.ManagedServerIps.Contains(c.GetString("ip") ?? "")) continue;
					kept.Add(t);
				}
			}
			var items = new List<NbtTag>();
			var managed = new List<string>();
			foreach (var s in cfg.Servers)
			{
				var c = new NbtCompound();
				string ip = s.GameAddress();
				c.Set("ip", new NbtTag(8, ip));
				c.Set("name", new NbtTag(8, string.IsNullOrEmpty(s.Name) ? ip : s.Name));
				if (s.Packs == "accept") c.Set("acceptTextures", new NbtTag(1, (sbyte)1));
				else if (s.Packs == "refuse") c.Set("acceptTextures", new NbtTag(1, (sbyte)0));
				items.Add(new NbtTag(10, c));
				managed.Add(ip);
			}
			items.AddRange(kept);
			var tag = new NbtTag(9, items);
			tag.ListType = 10;
			root.Set("servers", tag);
			Directory.CreateDirectory(Config.GameDir);
			File.WriteAllBytes(path, Nbt.Write(root));
			cfg.ManagedServerIps = managed;
		}
	}
}
