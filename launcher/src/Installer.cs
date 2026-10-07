using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EaglerLauncher
{
	class DlItem
	{
		public string Url, Path, Sha1;
		public long Size = -1;
		public DlItem(string url, string path, string sha1, long size) { Url = url; Path = path; Sha1 = sha1; Size = size; }
	}

	class Downloader
	{
		public readonly HttpClient Http;

		public Downloader()
		{
			var h = new HttpClientHandler();
			h.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			try
			{
				h.Proxy = WebRequest.GetSystemWebProxy();
				h.Proxy.Credentials = CredentialCache.DefaultCredentials;
				h.UseProxy = true;
			}
			catch { }
			Http = new HttpClient(h);
			Http.Timeout = TimeSpan.FromMinutes(10);
			Http.DefaultRequestHeaders.UserAgent.ParseAdd("EaglerJavaLauncher/1.0 (+offline launcher)");
		}

		public async Task<string> Text(string url)
		{
			for (int attempt = 1; ; attempt++)
			{
				try { return await Http.GetStringAsync(url); }
				catch { if (attempt >= 3) throw; }
				await Task.Delay(800 * attempt);
			}
		}

		public static string Sha1Of(string path)
		{
			using (var s = File.OpenRead(path))
			using (var sha = SHA1.Create())
				return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
		}

		// Existing files are trusted when their size matches (hashing ~1 GB on
		// every launch would be slow); fresh downloads are always hash-checked.
		public static bool UpToDate(DlItem it)
		{
			var f = new FileInfo(it.Path);
			if (!f.Exists) return false;
			if (it.Size >= 0) return f.Length == it.Size;
			return it.Sha1 == null || Sha1Of(it.Path) == it.Sha1;
		}

		public async Task File1(DlItem it)
		{
			if (UpToDate(it)) return;
			Directory.CreateDirectory(Path.GetDirectoryName(it.Path));
			string part = it.Path + ".part";
			for (int attempt = 1; ; attempt++)
			{
				try
				{
					using (var resp = await Http.GetAsync(it.Url, HttpCompletionOption.ResponseHeadersRead))
					{
						resp.EnsureSuccessStatusCode();
						using (var src = await resp.Content.ReadAsStreamAsync())
						using (var dst = File.Create(part))
							await src.CopyToAsync(dst, 81920);
					}
					if (it.Sha1 != null && Sha1Of(part) != it.Sha1.ToLowerInvariant())
						throw new IOException("empreinte SHA-1 invalide pour " + Path.GetFileName(it.Path));
					if (File.Exists(it.Path)) File.Delete(it.Path);
					File.Move(part, it.Path);
					return;
				}
				catch (Exception)
				{
					try { if (File.Exists(part)) File.Delete(part); } catch { }
					if (attempt >= 4) throw;
				}
				await Task.Delay(1000 * attempt);
			}
		}

		public async Task Many(List<DlItem> items, int parallel, Action<string> status, string label)
		{
			var todo = items.Where(i => !UpToDate(i)).ToList();
			if (todo.Count == 0) return;
			long totalBytes = todo.Sum(i => Math.Max(0, i.Size));
			long doneBytes = 0;
			int done = 0;
			var gate = new SemaphoreSlim(parallel);
			var tasks = todo.Select(async it =>
			{
				await gate.WaitAsync();
				try { await File1(it); }
				finally { gate.Release(); }
				int n = Interlocked.Increment(ref done);
				long b = Interlocked.Add(ref doneBytes, Math.Max(0, it.Size));
				status(label + " : " + n + "/" + todo.Count + (totalBytes > 0 ? "  (" + (b >> 20) + " / " + (totalBytes >> 20) + " Mo)" : ""));
			}).ToList();
			await Task.WhenAll(tasks);
		}
	}

	class LaunchPlan
	{
		public string Java;
		public List<string> Args = new List<string>();
		public string GameDir;
		public string VersionLabel;
	}

	class Installer
	{
		const string Manifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
		const string JavaRuntimes = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
		const string Assets = "https://resources.download.minecraft.net/";

		// Optimisation mods (Modrinth slugs). Missing ones for a version are skipped.
		public static readonly string[] Mods = { "fabric-api", "sodium", "lithium", "ferrite-core", "immediatelyfast", "entityculling", "krypton" };

		readonly Downloader dl = new Downloader();
		readonly Action<string> status;
		readonly string root = Config.DataDir;

		public Installer(Action<string> status) { this.status = status; }

		string P(params string[] parts) { return Path.Combine(new[] { root }.Concat(parts).ToArray()); }

		public async Task<List<string>> ReleaseVersions()
		{
			var m = Json.Parse(await dl.Text(Manifest));
			return Json.Arr(Json.Get(m, "versions")).Where(v => Json.Str(v, "type") == "release").Select(v => Json.Str(v, "id")).ToList();
		}

		static bool RulesAllow(object rules, ICollection<string> features)
		{
			var list = Json.Arr(rules);
			if (list.Length == 0) return true;
			bool allowed = false;
			foreach (var r in list)
			{
				bool match = true;
				var os = Json.Obj(Json.Get(r, "os"));
				if (os != null)
				{
					if (os.ContainsKey("name") && Json.Str(os, "name") != "windows") match = false;
					if (os.ContainsKey("arch") && Json.Str(os, "arch") != (Environment.Is64BitOperatingSystem ? "x86_64" : "x86")) match = false;
				}
				var feats = Json.Obj(Json.Get(r, "features"));
				if (feats != null)
					foreach (var f in feats) if (Convert.ToBoolean(f.Value) != features.Contains(f.Key)) match = false;
				if (match) allowed = Json.Str(r, "action") == "allow";
			}
			return allowed;
		}

		static string MavenPath(string name)
		{
			// group:artifact:version[:classifier][@ext]
			string ext = "jar";
			int at = name.IndexOf('@');
			if (at > 0) { ext = name.Substring(at + 1); name = name.Substring(0, at); }
			var p = name.Split(':');
			string file = p[1] + "-" + p[2] + (p.Length > 3 ? "-" + p[3] : "") + "." + ext;
			return p[0].Replace('.', '/') + "/" + p[1] + "/" + p[2] + "/" + file;
		}

		static string LibKey(string name)
		{
			var p = name.Split('@')[0].Split(':');
			return p[0] + ":" + p[1] + (p.Length > 3 ? ":" + p[3] : "");
		}

		async Task<object> CachedJson(string url, string path, string sha1)
		{
			await dl.File1(new DlItem(url, path, sha1, -1));
			return Json.Parse(File.ReadAllText(path, Encoding.UTF8));
		}

		async Task<string> InstallJava(string component)
		{
			string dir = P("runtime", component);
			string javaw = Path.Combine(dir, "bin", "javaw.exe");
			string marker = Path.Combine(dir, ".installe");
			if (File.Exists(javaw) && File.Exists(marker)) return javaw;
			status("Java : recherche du runtime " + component + "…");
			var all = Json.Parse(await dl.Text(JavaRuntimes));
			string platform = Environment.Is64BitOperatingSystem ? "windows-x64" : "windows-x86";
			var entry = Json.Arr(Json.Get(all, platform, component)).FirstOrDefault();
			if (entry == null) throw new Exception("Runtime Java " + component + " indisponible pour " + platform);
			var man = Json.Parse(await dl.Text(Json.Str(entry, "manifest", "url")));
			var items = new List<DlItem>();
			foreach (var f in Json.Obj(Json.Get(man, "files")))
			{
				string target = Path.Combine(dir, f.Key.Replace('/', '\\'));
				string type = Json.Str(f.Value, "type");
				if (type == "directory") Directory.CreateDirectory(target);
				else if (type == "file")
					items.Add(new DlItem(Json.Str(f.Value, "downloads", "raw", "url"), target,
						Json.Str(f.Value, "downloads", "raw", "sha1"), Json.Long(f.Value, "downloads", "raw", "size")));
			}
			await dl.Many(items, 12, status, "Java " + Json.Str(entry, "version", "name"));
			File.WriteAllText(marker, Json.Str(entry, "version", "name"));
			return javaw;
		}

		async Task<object> ModrinthVersion(string idOrSlug, string game)
		{
			string url = "https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(idOrSlug) +
				"/version?loaders=%5B%22fabric%22%5D&game_versions=%5B%22" + Uri.EscapeDataString(game) + "%22%5D";
			object[] versions;
			try { versions = Json.Arr(Json.Parse(await dl.Text(url))); }
			catch { return null; }
			return versions.FirstOrDefault(v => Json.Str(v, "version_type") == "release") ?? versions.FirstOrDefault();
		}

		async Task InstallMods(string game, string modsDir, bool enabled)
		{
			Directory.CreateDirectory(modsDir);
			string listFile = Path.Combine(modsDir, ".gere-par-le-launcher.txt");
			var previous = File.Exists(listFile) ? File.ReadAllLines(listFile).ToList() : new List<string>();
			var wanted = new Dictionary<string, DlItem>();
			if (enabled)
			{
				var queue = new Queue<string>(Mods);
				var seen = new HashSet<string>();
				while (queue.Count > 0)
				{
					string id = queue.Dequeue();
					if (!seen.Add(id)) continue;
					status("Mods : " + id + "…");
					var v = await ModrinthVersion(id, game);
					if (v == null) continue;
					seen.Add(Json.Str(v, "project_id"));
					var files = Json.Arr(Json.Get(v, "files"));
					var file = files.FirstOrDefault(f => Convert.ToBoolean(Json.Get(f, "primary") ?? false)) ?? files.FirstOrDefault();
					if (file == null) continue;
					string name = Json.Str(file, "filename");
					wanted[name] = new DlItem(Json.Str(file, "url"), Path.Combine(modsDir, name), Json.Str(file, "hashes", "sha1"), Json.Long(file, "size"));
					foreach (var d in Json.Arr(Json.Get(v, "dependencies")))
						if (Json.Str(d, "dependency_type") == "required" && Json.Str(d, "project_id") != null) queue.Enqueue(Json.Str(d, "project_id"));
				}
			}
			// Only remove jars this launcher installed earlier; the player's own mods stay.
			foreach (var old in previous)
				if (!wanted.ContainsKey(old)) try { File.Delete(Path.Combine(modsDir, old)); } catch { }
			await dl.Many(wanted.Values.ToList(), 6, status, "Mods");
			File.WriteAllLines(listFile, wanted.Keys.ToArray());
		}

		// The Eagler Relay mod ships inside the launcher; it only acts when the
		// launcher passes -Deaglerrelay.port, so it is harmless in direct mode.
		const string RelayModFile = "eagler-relay-1.0.0.jar";

		static void InstallRelayMod(string modsDir, bool fabric)
		{
			string path = Path.Combine(modsDir, RelayModFile);
			if (!fabric) { if (File.Exists(path)) File.Delete(path); return; }
			Directory.CreateDirectory(modsDir);
			using (var res = typeof(Installer).Assembly.GetManifestResourceStream("eagler-relay.jar"))
			{
				if (res == null) throw new Exception("mod relais absent du launcher (recompile avec compiler.bat)");
				var ms = new MemoryStream();
				res.CopyTo(ms);
				var bytes = ms.ToArray();
				if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length) File.WriteAllBytes(path, bytes);
			}
		}

		public async Task<LaunchPlan> Prepare(Config cfg, string quickJoin, int relayPort, MinecraftSession session)
		{
			string id = cfg.Version;
			status("Liste des versions Minecraft…");
			var manifest = Json.Parse(await dl.Text(Manifest));
			var ventry = Json.Arr(Json.Get(manifest, "versions")).FirstOrDefault(v => Json.Str(v, "id") == id);
			if (ventry == null) throw new Exception("Version Minecraft introuvable : " + id);
			var vjson = await CachedJson(Json.Str(ventry, "url"), P("versions", id, id + ".json"), Json.Str(ventry, "sha1"));

			string java = await InstallJava(Json.Str(vjson, "javaVersion", "component") ?? "java-runtime-delta");

			var features = new HashSet<string>();
			if (!string.IsNullOrEmpty(quickJoin)) features.Add("is_quick_play_multiplayer");

			// Libraries: vanilla first, Fabric entries replace same group:artifact.
			var libs = new List<DlItem>();
			var classpath = new Dictionary<string, string>();
			var order = new List<string>();
			Action<string, string> addCp = (key, path) =>
			{
				if (!classpath.ContainsKey(key)) order.Add(key);
				classpath[key] = path;
			};
			foreach (var lib in Json.Arr(Json.Get(vjson, "libraries")))
			{
				if (!RulesAllow(Json.Get(lib, "rules"), features)) continue;
				var art = Json.Get(lib, "downloads", "artifact");
				if (art == null) continue;
				string path = P("libraries", Json.Str(art, "path").Replace('/', '\\'));
				libs.Add(new DlItem(Json.Str(art, "url"), path, Json.Str(art, "sha1"), Json.Long(art, "size")));
				addCp(LibKey(Json.Str(lib, "name")), path);
			}

			string mainClass = Json.Str(vjson, "mainClass");
			var extraJvm = new List<string>();
			string versionName = id;
			bool fabric = cfg.OptimizationMods || cfg.RelayEnabled();
			if (fabric)
			{
				status("Fabric : recherche du loader…");
				var loaders = Json.Arr(Json.Parse(await dl.Text("https://meta.fabricmc.net/v2/versions/loader/" + Uri.EscapeDataString(id))));
				var loader = loaders.FirstOrDefault(l => Convert.ToBoolean(Json.Get(l, "loader", "stable") ?? false)) ?? loaders.FirstOrDefault();
				if (loader == null) throw new Exception("Fabric n'est pas encore disponible pour " + id + " : décoche les mods d'optimisation et passe la connexion en directe.");
				string lv = Json.Str(loader, "loader", "version");
				var profile = await CachedJson("https://meta.fabricmc.net/v2/versions/loader/" + id + "/" + lv + "/profile/json",
					P("versions", "fabric-" + id + "-" + lv + ".json"), null);
				mainClass = Json.Str(profile, "mainClass");
				versionName = Json.Str(profile, "id") ?? versionName;
				foreach (var lib in Json.Arr(Json.Get(profile, "libraries")))
				{
					string name = Json.Str(lib, "name");
					string rel = MavenPath(name);
					string baseUrl = (Json.Str(lib, "url") ?? "https://maven.fabricmc.net/").TrimEnd('/') + "/";
					string path = P("libraries", rel.Replace('/', '\\'));
					libs.Add(new DlItem(baseUrl + rel, path, Json.Str(lib, "sha1"), Json.Long(lib, "size")));
					addCp(LibKey(name), path);
				}
				foreach (var a in Json.Arr(Json.Get(profile, "arguments", "jvm"))) if (a is string) extraJvm.Add((string)a);
			}

			string clientJar = P("versions", id, id + ".jar");
			libs.Add(new DlItem(Json.Str(vjson, "downloads", "client", "url"), clientJar,
				Json.Str(vjson, "downloads", "client", "sha1"), Json.Long(vjson, "downloads", "client", "size")));
			await dl.Many(libs, 8, status, "Bibliothèques");

			// Assets (sounds, languages...).
			string assetId = Json.Str(vjson, "assetIndex", "id");
			var index = await CachedJson(Json.Str(vjson, "assetIndex", "url"), P("assets", "indexes", assetId + ".json"), Json.Str(vjson, "assetIndex", "sha1"));
			var assets = new List<DlItem>();
			var objects = Json.Obj(Json.Get(index, "objects"));
			if (objects != null)
			{
				var seen = new HashSet<string>();
				foreach (var o in objects)
				{
					string h = Json.Str(o.Value, "hash");
					if (!seen.Add(h)) continue;
					assets.Add(new DlItem(Assets + h.Substring(0, 2) + "/" + h, P("assets", "objects", h.Substring(0, 2), h), h, Json.Long(o.Value, "size")));
				}
			}
			await dl.Many(assets, 16, status, "Ressources du jeu");

			// Mojang's launcher log config prints log4j XML to the console; the game's
			// built-in config gives plain text (and still writes logs/latest.log).

			string gameDir = Config.GameDir;
			await InstallMods(id, Path.Combine(gameDir, "mods"), cfg.OptimizationMods);
			InstallRelayMod(Path.Combine(gameDir, "mods"), fabric);
			WriteDefaultOptions(gameDir, clientJar);

			string natives = P("versions", id, "natives");
			Directory.CreateDirectory(natives);
			var cp = order.Select(k => classpath[k]).ToList();
			cp.Add(clientJar);

			var vars = new Dictionary<string, string>
			{
				{ "auth_player_name", session != null ? session.Name : cfg.Username },
				{ "version_name", versionName },
				{ "game_directory", gameDir },
				{ "assets_root", P("assets") },
				{ "assets_index_name", assetId },
				{ "auth_uuid", session != null ? session.Uuid : OfflineUuid(cfg.Username) },
				{ "auth_access_token", session != null ? session.AccessToken : "0" },
				{ "clientid", session != null ? MicrosoftAuth.ClientId : "0" },
				{ "auth_xuid", session != null ? session.Xuid : "0" },
				{ "user_type", session != null ? "msa" : "legacy" },
				{ "user_properties", "{}" },
				{ "version_type", "release" },
				{ "natives_directory", natives },
				{ "launcher_name", "EaglerJavaLauncher" },
				{ "launcher_version", "1.0" },
				{ "classpath", string.Join(";", cp) },
				{ "classpath_separator", ";" },
				{ "library_directory", P("libraries") },
				{ "quickPlayMultiplayer", quickJoin ?? "" }
			};

			var plan = new LaunchPlan { Java = java, GameDir = gameDir, VersionLabel = versionName };
			plan.Args.Add("-Xms" + Math.Min(1024, cfg.MemoryMb) + "M");
			plan.Args.Add("-Xmx" + cfg.MemoryMb + "M");
			if (relayPort > 0) plan.Args.Add("-Deaglerrelay.port=" + relayPort);
			plan.Args.AddRange(PerformanceFlags());
			if (!string.IsNullOrWhiteSpace(cfg.ExtraJvmArgs))
				plan.Args.AddRange(cfg.ExtraJvmArgs.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
			plan.Args.AddRange(extraJvm);
			AddArgs(plan.Args, Json.Get(vjson, "arguments", "jvm"), features, vars);
			plan.Args.Add(mainClass);
			AddArgs(plan.Args, Json.Get(vjson, "arguments", "game"), features, vars);
			return plan;
		}

		// G1 tuned for a game client (short pauses, young-gen heavy allocation).
		// Unknown flags are ignored so a future Java does not refuse to start.
		static IEnumerable<string> PerformanceFlags()
		{
			return new[]
			{
				"-XX:+IgnoreUnrecognizedVMOptions", "-XX:+UnlockExperimentalVMOptions",
				"-XX:+UseG1GC", "-XX:MaxGCPauseMillis=50", "-XX:+ParallelRefProcEnabled",
				"-XX:G1NewSizePercent=20", "-XX:G1ReservePercent=20", "-XX:G1HeapRegionSize=16M",
				"-XX:InitiatingHeapOccupancyPercent=20", "-XX:G1MixedGCCountTarget=4",
				"-XX:+DisableExplicitGC", "-XX:+PerfDisableSharedMem", "-XX:+UseCompactObjectHeaders",
				"-XX:+UseStringDeduplication", "-Dfile.encoding=UTF-8"
			};
		}

		static void AddArgs(List<string> into, object spec, ICollection<string> features, Dictionary<string, string> vars)
		{
			foreach (var a in Json.Arr(spec))
			{
				IEnumerable<object> values;
				if (a is string) values = new object[] { a };
				else
				{
					if (!RulesAllow(Json.Get(a, "rules"), features)) continue;
					var v = Json.Get(a, "value");
					values = v is string ? new object[] { v } : Json.Arr(v);
				}
				foreach (var v in values)
				{
					string s = (string)v;
					foreach (var kv in vars) s = s.Replace("${" + kv.Key + "}", kv.Value);
					into.Add(s);
				}
			}
		}

		// Same algorithm as the game for offline players: UUID v3 of "OfflinePlayer:<name>".
		public static string OfflineUuid(string name)
		{
			byte[] h;
			using (var md5 = MD5.Create()) h = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
			h[6] = (byte)((h[6] & 0x0f) | 0x30);
			h[8] = (byte)((h[8] & 0x3f) | 0x80);
			string x = BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
			return x.Substring(0, 8) + "-" + x.Substring(8, 4) + "-" + x.Substring(12, 4) + "-" + x.Substring(16, 4) + "-" + x.Substring(20);
		}

		// First launch only: French, sensible render distance, no first-join nags.
		static void WriteDefaultOptions(string gameDir, string clientJar)
		{
			string path = Path.Combine(gameDir, "options.txt");
			if (File.Exists(path)) return;
			string dataVersion = null;
			try
			{
				using (var zip = ZipFile.OpenRead(clientJar))
				{
					var e = zip.GetEntry("version.json");
					if (e != null)
						using (var r = new StreamReader(e.Open()))
							dataVersion = Json.Str(Json.Parse(r.ReadToEnd()), "world_version");
				}
			}
			catch { }
			if (dataVersion == null) return;  // never write options the game would "upgrade" wrongly
			Directory.CreateDirectory(gameDir);
			File.WriteAllText(path, string.Join("\n", new[]
			{
				"version:" + dataVersion, "lang:fr_fr", "renderDistance:8", "simulationDistance:6",
				"maxFps:120", "skipMultiplayerWarning:true", "onboardAccessibility:false", "joinedFirstServer:true"
			}) + "\n");
		}
	}
}
