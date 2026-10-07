using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EaglerLauncher
{
	static class Packs
	{
		public static string Dir { get { return Path.Combine(Config.GameDir, "resourcepacks"); } }

		// Share links -> direct download links.
		public static string DirectLink(string url)
		{
			Uri u;
			if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out u)) return url;
			string h = u.Host.ToLowerInvariant();
			if (h == "www.dropbox.com" || h == "dropbox.com")
			{
				var b = new UriBuilder(u) { Host = "dl.dropboxusercontent.com" };
				string q = Regex.Replace(b.Query.TrimStart('?'), @"(^|&)dl=\d", "");
				b.Query = (q.Length > 0 ? q.Trim('&') + "&" : "") + "dl=1";
				return b.Uri.ToString();
			}
			if (h == "drive.google.com")
			{
				var m = Regex.Match(u.AbsolutePath, @"/file/d/([^/]+)");
				string id = m.Success ? m.Groups[1].Value : Regex.Match(u.Query, @"[?&]id=([^&]+)").Groups[1].Value;
				if (id.Length > 0) return "https://drive.usercontent.google.com/download?id=" + id + "&export=download&confirm=t";
			}
			if (h == "github.com")
			{
				var m = Regex.Match(u.AbsolutePath, @"^/([^/]+)/([^/]+)/(?:blob|raw)/(.+)$");
				if (m.Success) return "https://raw.githubusercontent.com/" + m.Groups[1].Value + "/" + m.Groups[2].Value + "/" + m.Groups[3].Value;
			}
			return url;
		}

		public static async Task<string> Download(Downloader dl, string url, Action<string> status)
		{
			string direct = DirectLink(url);
			status("Téléchargement du pack…");
			byte[] data;
			string name = null;
			using (var resp = await dl.Http.GetAsync(direct, HttpCompletionOption.ResponseHeadersRead))
			{
				resp.EnsureSuccessStatusCode();
				var cd = resp.Content.Headers.ContentDisposition;
				if (cd != null) name = (cd.FileNameStar ?? cd.FileName ?? "").Trim('"');
				data = await resp.Content.ReadAsByteArrayAsync();
			}
			if (string.IsNullOrEmpty(name)) name = Uri.UnescapeDataString(new Uri(direct).AbsolutePath.Split('/').Last());
			if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name += ".zip";
			string tmp = Path.Combine(Path.GetTempPath(), "eagler-pack-" + Guid.NewGuid().ToString("N") + ".zip");
			File.WriteAllBytes(tmp, data);
			try { return Install(tmp, name); }
			finally { try { File.Delete(tmp); } catch { } }
		}

		// Copies a pack zip into resourcepacks/. Zips that wrap the pack in a folder
		// ("MyPack/pack.mcmeta") are rebuilt so pack.mcmeta sits at the root, which
		// Minecraft requires.
		public static string Install(string zipPath, string fileName)
		{
			Directory.CreateDirectory(Dir);
			string safe = Regex.Replace(Path.GetFileNameWithoutExtension(fileName), @"[^\w\-. ]+", "_").Trim();
			if (safe.Length == 0) safe = "pack";
			string target = Path.Combine(Dir, safe + ".zip");
			string prefix;
			using (var zip = ZipFile.OpenRead(zipPath))
			{
				if (zip.GetEntry("pack.mcmeta") != null) prefix = "";
				else
				{
					var nested = zip.Entries.Where(e => Regex.IsMatch(e.FullName.Replace('\\', '/'), @"^[^/]+/pack\.mcmeta$")).ToList();
					if (nested.Count != 1) throw new InvalidDataException("Aucun pack.mcmeta : ce fichier n'est pas un pack de ressources.");
					prefix = nested[0].FullName.Replace('\\', '/');
					prefix = prefix.Substring(0, prefix.Length - "pack.mcmeta".Length);
				}
				if (prefix.Length > 0)
				{
					if (File.Exists(target)) File.Delete(target);
					using (var outZip = ZipFile.Open(target, ZipArchiveMode.Create))
						foreach (var e in zip.Entries)
						{
							string n = e.FullName.Replace('\\', '/');
							if (!n.StartsWith(prefix) || n.EndsWith("/") || n.Length == prefix.Length) continue;
							var ne = outZip.CreateEntry(n.Substring(prefix.Length), CompressionLevel.Optimal);
							using (var s = e.Open()) using (var d = ne.Open()) s.CopyTo(d);
						}
					return Path.GetFileName(target);
				}
			}
			File.Copy(zipPath, target, true);
			return Path.GetFileName(target);
		}
	}
}
