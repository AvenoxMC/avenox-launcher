using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EaglerLauncher
{
	class DeviceCode
	{
		public string UserCode, DeviceCodeValue, VerificationUri, Message;
		public int Interval, ExpiresIn;
	}

	class MinecraftSession
	{
		public string Name, Uuid, AccessToken, Xuid;
		public DateTime ExpiresUtc;
	}

	// Microsoft account -> Xbox Live -> XSTS -> Minecraft services, using the OAuth
	// device code flow (the player signs in on microsoft.com/link in any browser).
	// The Azure app must be a public client with Minecraft API access approved by Mojang.
	class MicrosoftAuth
	{
		const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";
		const string Scope = "XboxLive.signin offline_access";

		// Azure app "Eagler Java Launcher" (personal Microsoft accounts, public client).
		// A public client ID is not a secret: it only names the app to Microsoft.
		public const string ClientId = "1b5191b4-8d39-4ce8-b8c0-6bb11c044842";
		readonly HttpClient http;
		readonly string clientId;

		public MicrosoftAuth()
		{
			clientId = ClientId;
			var h = new HttpClientHandler();
			try
			{
				h.Proxy = WebRequest.GetSystemWebProxy();
				h.Proxy.Credentials = CredentialCache.DefaultCredentials;
				h.UseProxy = true;
			}
			catch { }
			http = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(30) };
			http.DefaultRequestHeaders.UserAgent.ParseAdd("EaglerJavaLauncher/1.0");
		}

		async Task<Tuple<int, object>> PostForm(string url, Dictionary<string, string> form)
		{
			using (var resp = await http.PostAsync(url, new FormUrlEncodedContent(form)))
				return Tuple.Create((int)resp.StatusCode, SafeParse(await resp.Content.ReadAsStringAsync()));
		}

		async Task<Tuple<int, object>> PostJson(string url, object body)
		{
			var content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");
			var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
			req.Headers.Accept.ParseAdd("application/json");
			using (var resp = await http.SendAsync(req))
				return Tuple.Create((int)resp.StatusCode, SafeParse(await resp.Content.ReadAsStringAsync()));
		}

		static object SafeParse(string text)
		{
			try { return string.IsNullOrWhiteSpace(text) ? null : Json.Parse(text); } catch { return null; }
		}

		public async Task<DeviceCode> Begin()
		{
			var r = await PostForm(Authority + "devicecode", new Dictionary<string, string> { { "client_id", clientId }, { "scope", Scope } });
			if (r.Item1 != 200) throw new Exception("Microsoft a refusé la demande : " + (Json.Str(r.Item2, "error_description") ?? ("HTTP " + r.Item1)));
			return new DeviceCode
			{
				UserCode = Json.Str(r.Item2, "user_code"),
				DeviceCodeValue = Json.Str(r.Item2, "device_code"),
				VerificationUri = Json.Str(r.Item2, "verification_uri") ?? "https://www.microsoft.com/link",
				Message = Json.Str(r.Item2, "message"),
				Interval = (int)Math.Max(2, Json.Long(r.Item2, "interval")),
				ExpiresIn = (int)Math.Max(60, Json.Long(r.Item2, "expires_in"))
			};
		}

		// Returns the Microsoft refresh token once the player has approved the sign-in.
		public async Task<Tuple<string, string>> WaitForApproval(DeviceCode code, CancellationToken ct)
		{
			int interval = code.Interval;
			var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresIn);
			while (DateTime.UtcNow < deadline)
			{
				await Task.Delay(interval * 1000, ct);
				var r = await PostForm(Authority + "token", new Dictionary<string, string>
				{
					{ "grant_type", "urn:ietf:params:oauth:grant-type:device_code" },
					{ "client_id", clientId },
					{ "device_code", code.DeviceCodeValue }
				});
				if (r.Item1 == 200) return Tuple.Create(Json.Str(r.Item2, "access_token"), Json.Str(r.Item2, "refresh_token"));
				string err = Json.Str(r.Item2, "error");
				if (err == "authorization_pending") continue;
				if (err == "slow_down") { interval += 5; continue; }
				if (err == "authorization_declined") throw new Exception("Connexion refusée sur la page Microsoft.");
				if (err == "expired_token") break;
				throw new Exception("Erreur Microsoft : " + (Json.Str(r.Item2, "error_description") ?? err ?? ("HTTP " + r.Item1)));
			}
			throw new Exception("Le code a expiré : recommence la connexion.");
		}

		public async Task<Tuple<string, string>> Refresh(string refreshToken)
		{
			var r = await PostForm(Authority + "token", new Dictionary<string, string>
			{
				{ "grant_type", "refresh_token" },
				{ "client_id", clientId },
				{ "refresh_token", refreshToken },
				{ "scope", Scope }
			});
			if (r.Item1 != 200) throw new Exception("Session Microsoft expirée : reconnecte-toi (" + (Json.Str(r.Item2, "error") ?? ("HTTP " + r.Item1)) + ").");
			return Tuple.Create(Json.Str(r.Item2, "access_token"), Json.Str(r.Item2, "refresh_token") ?? refreshToken);
		}

		public async Task<MinecraftSession> Minecraft(string msAccessToken)
		{
			var xbl = await PostJson("https://user.auth.xboxlive.com/user/authenticate", new Dictionary<string, object>
			{
				{ "Properties", new Dictionary<string, object> { { "AuthMethod", "RPS" }, { "SiteName", "user.auth.xboxlive.com" }, { "RpsTicket", "d=" + msAccessToken } } },
				{ "RelyingParty", "http://auth.xboxlive.com" },
				{ "TokenType", "JWT" }
			});
			if (xbl.Item1 != 200) throw new Exception("Xbox Live a refusé la connexion (HTTP " + xbl.Item1 + ").");
			string xblToken = Json.Str(xbl.Item2, "Token");

			var xsts = await PostJson("https://xsts.auth.xboxlive.com/xsts/authorize", new Dictionary<string, object>
			{
				{ "Properties", new Dictionary<string, object> { { "SandboxId", "RETAIL" }, { "UserTokens", new[] { xblToken } } } },
				{ "RelyingParty", "rp://api.minecraftservices.com/" },
				{ "TokenType", "JWT" }
			});
			if (xsts.Item1 != 200)
			{
				long xerr = Json.Long(xsts.Item2, "XErr");
				if (xerr == 2148916233) throw new Exception("Ce compte Microsoft n'a pas de profil Xbox : ouvre une fois xbox.com pour le créer.");
				if (xerr == 2148916235) throw new Exception("Xbox Live n'est pas disponible dans le pays de ce compte.");
				if (xerr == 2148916236 || xerr == 2148916237) throw new Exception("Ce compte doit être vérifié comme adulte sur xbox.com.");
				if (xerr == 2148916238) throw new Exception("Compte mineur : il doit être ajouté à une famille Microsoft par un parent.");
				throw new Exception("Xbox (XSTS) a refusé la connexion (HTTP " + xsts.Item1 + (xerr > 0 ? ", XErr " + xerr : "") + ").");
			}
			string xstsToken = Json.Str(xsts.Item2, "Token");
			var xui = Json.Arr(Json.Get(xsts.Item2, "DisplayClaims", "xui"));
			string uhs = xui.Length > 0 ? Json.Str(xui[0], "uhs") : "";
			string xid = xui.Length > 0 ? Json.Str(xui[0], "xid") : null;

			var mc = await PostJson("https://api.minecraftservices.com/authentication/login_with_xbox",
				new Dictionary<string, object> { { "identityToken", "XBL3.0 x=" + uhs + ";" + xstsToken } });
			if (mc.Item1 == 403)
				throw new Exception("Mojang a refusé cet ID d'application Azure : il doit être approuvé pour l'API Minecraft (formulaire « Minecraft API access » de Mojang).");
			if (mc.Item1 != 200) throw new Exception("Connexion aux services Minecraft impossible (HTTP " + mc.Item1 + ").");
			string token = Json.Str(mc.Item2, "access_token");
			long expires = Json.Long(mc.Item2, "expires_in");

			var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
			req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
			using (var resp = await http.SendAsync(req))
			{
				if (resp.StatusCode == HttpStatusCode.NotFound) throw new Exception("Ce compte ne possède pas Minecraft Java Edition.");
				if (!resp.IsSuccessStatusCode) throw new Exception("Profil Minecraft introuvable (HTTP " + (int)resp.StatusCode + ").");
				var profile = Json.Parse(await resp.Content.ReadAsStringAsync());
				string id = Json.Str(profile, "id");
				return new MinecraftSession
				{
					Name = Json.Str(profile, "name"),
					Uuid = id.Length == 32 ? id.Substring(0, 8) + "-" + id.Substring(8, 4) + "-" + id.Substring(12, 4) + "-" + id.Substring(16, 4) + "-" + id.Substring(20) : id,
					AccessToken = token,
					Xuid = xid ?? "0",
					ExpiresUtc = DateTime.UtcNow.AddSeconds(expires > 0 ? expires : 86400)
				};
			}
		}

		// Tokens are stored encrypted for the current Windows user (DPAPI).
		public static string Protect(string value)
		{
			if (string.IsNullOrEmpty(value)) return null;
			return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
		}

		public static string Unprotect(string value)
		{
			if (string.IsNullOrEmpty(value)) return null;
			try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
			catch { return null; }
		}
	}
}
