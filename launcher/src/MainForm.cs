using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EaglerLauncher
{
	class MainForm : Form
	{
		readonly Config cfg = Config.Load();
		readonly List<WispBridge> bridges = new List<WispBridge>();
		RelayBridge relayBridge;
		Process game;

		TextBox username, extraJvm, packUrl, logBox;
		RadioButton offlineMode, microsoftMode;
		Label accountLabel;
		Button accountButton;
		ComboBox version, join;
		NumericUpDown memory;
		CheckBox mods, minimize, useRelay;
		TextBox relayUrl;
		Button play;
		Label status;
		ProgressBar progress;
		ListView serverList;
		ListBox packList;

		public MainForm()
		{
			Text = "Eagler Java Launcher — Minecraft Java Edition";
			Font = new Font("Segoe UI", 9.5f);
			AutoScaleMode = AutoScaleMode.Dpi;
			MinimumSize = new Size(720, 520);
			Size = new Size(820, 600);
			StartPosition = FormStartPosition.CenterScreen;
			try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

			var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
			tabs.TabPages.Add(PlayTab());
			tabs.TabPages.Add(ServersTab());
			tabs.TabPages.Add(PacksTab());
			tabs.TabPages.Add(SettingsTab());
			RefreshAccount();
			tabs.SelectedIndexChanged += delegate { if (tabs.SelectedIndex == 2) RefreshPacks(); };
			Controls.Add(tabs);

			FormClosing += OnClosing;
			Shown += async delegate { await LoadVersions(); };
		}

		// ------------------------------------------------------------- layout

		static TableLayoutPanel Grid(int columns)
		{
			var g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, Padding = new Padding(14), AutoSize = false };
			return g;
		}

		static Label Caption(string text)
		{
			return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 8) };
		}

		TabPage PlayTab()
		{
			var page = new TabPage("Jouer");
			var g = Grid(2);
			g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

			username = new TextBox { Text = cfg.Username, Width = 220, MaxLength = 16 };
			version = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDown, Text = cfg.Version };
			memory = new NumericUpDown { Minimum = 1024, Maximum = 32768, Increment = 512, Value = Math.Max(1024, Math.Min(32768, cfg.MemoryMb)), Width = 100 };
			mods = new CheckBox { Text = "Mods d'optimisation (Fabric + Sodium, Lithium, FerriteCore, ImmediatelyFast, EntityCulling, Krypton)", Checked = cfg.OptimizationMods, AutoSize = true };
			join = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
			minimize = new CheckBox { Text = "Réduire le launcher pendant le jeu", Checked = cfg.CloseOnLaunch, AutoSize = true };
			FillJoin();

			play = new Button { Text = "JOUER", Height = 54, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 16f, FontStyle.Bold), BackColor = Color.FromArgb(76, 154, 42), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
			play.FlatAppearance.BorderColor = Color.FromArgb(45, 97, 24);
			play.Click += async delegate { await Play(); };
			progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous, Height = 14 };
			status = new Label { Text = "Prêt.", AutoSize = true, Margin = new Padding(3, 6, 3, 6) };
			logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 8.5f), BackColor = Color.FromArgb(24, 24, 24), ForeColor = Color.Gainsboro };

			offlineMode = new RadioButton { Text = "Sans compte (pseudo, hors ligne)", AutoSize = true, Checked = !cfg.Microsoft(), Margin = new Padding(3, 6, 12, 3) };
			microsoftMode = new RadioButton { Text = "Avec un compte Microsoft", AutoSize = true, Checked = cfg.Microsoft(), Margin = new Padding(3, 6, 3, 3) };
			EventHandler modeChanged = delegate
			{
				cfg.AuthMode = microsoftMode.Checked ? "microsoft" : "offline";
				cfg.Save();
				RefreshAccount();
			};
			offlineMode.CheckedChanged += modeChanged;
			microsoftMode.CheckedChanged += modeChanged;
			var modes = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
			modes.Controls.Add(offlineMode);
			modes.Controls.Add(microsoftMode);

			accountLabel = new Label { AutoSize = true, Margin = new Padding(3, 7, 10, 3) };
			accountButton = new Button { AutoSize = true, Height = 28 };
			accountButton.Click += delegate { AccountClicked(); };
			var account = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
			account.Controls.Add(username);
			account.Controls.Add(accountLabel);
			account.Controls.Add(accountButton);

			var mem = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
			mem.Controls.Add(memory);
			mem.Controls.Add(new Label { Text = "Mo", AutoSize = true, Margin = new Padding(4, 6, 0, 0) });

			g.Controls.Add(Caption("Connexion"), 0, 0); g.Controls.Add(modes, 1, 0);
			g.Controls.Add(Caption("Compte"), 0, 1); g.Controls.Add(account, 1, 1);
			g.Controls.Add(Caption("Version"), 0, 2); g.Controls.Add(version, 1, 2);
			g.Controls.Add(Caption("Mémoire"), 0, 3); g.Controls.Add(mem, 1, 3);
			g.Controls.Add(Caption("Rejoindre"), 0, 4); g.Controls.Add(join, 1, 4);
			g.Controls.Add(mods, 0, 5); g.SetColumnSpan(mods, 2);
			g.Controls.Add(minimize, 0, 6); g.SetColumnSpan(minimize, 2);
			g.Controls.Add(play, 0, 7); g.SetColumnSpan(play, 2);
			g.Controls.Add(progress, 0, 8); g.SetColumnSpan(progress, 2);
			g.Controls.Add(status, 0, 9); g.SetColumnSpan(status, 2);
			g.Controls.Add(logBox, 0, 10); g.SetColumnSpan(logBox, 2);
			for (int i = 0; i < 10; i++) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			g.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			page.Controls.Add(g);
			return page;
		}

		TabPage ServersTab()
		{
			var page = new TabPage("Serveurs");
			var g = Grid(2);
			g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			useRelay = new CheckBox
			{
				Text = "Passer par le relais WebSocket, comme la version navigateur (à cocher si le réseau bloque Minecraft)",
				Checked = cfg.RelayEnabled(), AutoSize = true, Margin = new Padding(3, 3, 3, 6)
			};
			relayUrl = new TextBox { Text = cfg.RelayUrl, Dock = DockStyle.Fill };
			var resetRelay = new Button { Text = "Par défaut", Width = 120, Height = 26 };
			resetRelay.Click += delegate { relayUrl.Text = Config.DefaultRelay; };
			useRelay.CheckedChanged += delegate { relayUrl.Enabled = useRelay.Checked; };
			relayUrl.Enabled = useRelay.Checked;
			serverList = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false, MultiSelect = false };
			serverList.Columns.Add("Nom", 200);
			serverList.Columns.Add("Connexion", 100);
			serverList.Columns.Add("Adresse", 260);
			serverList.Columns.Add("Packs serveur", 110);
			serverList.DoubleClick += delegate { EditServer(SelectedServer()); };

			var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
			Func<string, EventHandler, Button> btn = (t, h) => { var b = new Button { Text = t, Width = 120, Height = 30 }; b.Click += h; buttons.Controls.Add(b); return b; };
			btn("Ajouter…", delegate { EditServer(null); });
			btn("Modifier…", delegate { EditServer(SelectedServer()); });
			btn("Supprimer", delegate
			{
				var s = SelectedServer();
				if (s == null || MessageBox.Show(this, "Retirer « " + s.Name + " » ?", "Serveurs", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
				cfg.Servers.Remove(s); SaveServers();
			});
			btn("Monter", delegate { MoveServer(-1); });
			btn("Descendre", delegate { MoveServer(1); });

			var help = new Label
			{
				AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(3, 10, 3, 3), ForeColor = Color.DimGray,
				Text = "Relais coché : tous les serveurs (même ceux ajoutés en jeu ou en connexion directe) passent par le relais wss://, " +
					"exactement comme le mode « Relay » d'Eaglercraft. Garde le launcher ouvert pendant la partie.\n" +
					"Java direct : adresse classique (play.exemple.fr ou ip:port).\n" +
					"Wisp : même mode que dans Eaglercraft. Le launcher ouvre un tunnel local vers le relais wss:// " +
					"(passe par le port 443, utile si le port 25565 est bloqué). Laisse le launcher ouvert pendant la partie.\n" +
					"EaglerX : adresse wss:// d'un serveur Eaglercraft ; Minecraft Java se connecte au même serveur en TCP " +
					"(les proxys EaglerXServer acceptent Java et WebSocket sur le même serveur)."
			};
			g.Controls.Add(useRelay, 0, 0); g.SetColumnSpan(useRelay, 2);
			g.Controls.Add(relayUrl, 0, 1); g.Controls.Add(resetRelay, 1, 1);
			g.Controls.Add(serverList, 0, 2);
			g.Controls.Add(buttons, 1, 2);
			g.Controls.Add(help, 0, 3); g.SetColumnSpan(help, 2);
			g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			g.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			page.Controls.Add(g);
			RefreshServers();
			return page;
		}

		TabPage PacksTab()
		{
			var page = new TabPage("Packs de textures");
			var g = Grid(2);
			g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			packList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
			packUrl = new TextBox { Dock = DockStyle.Fill };
			var dlBtn = new Button { Text = "Télécharger", Width = 120, Height = 28 };
			dlBtn.Click += async delegate { await DownloadPack(); };
			packUrl.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await DownloadPack(); } };

			var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
			var add = new Button { Text = "Ajouter un .zip…", Width = 120, Height = 30 };
			add.Click += delegate
			{
				using (var d = new OpenFileDialog { Filter = "Packs de ressources (*.zip)|*.zip", Multiselect = true })
				{
					if (d.ShowDialog(this) != DialogResult.OK) return;
					foreach (var f in d.FileNames)
						try { Log("Pack installé : " + Packs.Install(f, Path.GetFileName(f))); }
						catch (Exception ex) { MessageBox.Show(this, Path.GetFileName(f) + " : " + ex.Message, "Pack"); }
					RefreshPacks();
				}
			};
			var del = new Button { Text = "Supprimer", Width = 120, Height = 30 };
			del.Click += delegate
			{
				var n = packList.SelectedItem as string;
				if (n == null || MessageBox.Show(this, "Supprimer « " + n + " » ?", "Packs", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
				try { var p = Path.Combine(Packs.Dir, n); if (Directory.Exists(p)) Directory.Delete(p, true); else File.Delete(p); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
				RefreshPacks();
			};
			var open = new Button { Text = "Ouvrir le dossier", Width = 120, Height = 30 };
			open.Click += delegate { Directory.CreateDirectory(Packs.Dir); Process.Start("explorer.exe", Packs.Dir); };
			buttons.Controls.AddRange(new Control[] { add, del, open });

			var note = new Label
			{
				AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 8, 3, 3),
				Text = "Colle un lien (direct, Dropbox, Google Drive ou GitHub) : le pack est converti et placé dans resourcepacks. " +
					"Les ZIP avec un sous-dossier sont réparés. Active-le ensuite en jeu (Options → Packs de ressources). " +
					"Les packs envoyés par les serveurs sont téléchargés directement par Minecraft Java (pas de blocage CORS)."
			};
			g.Controls.Add(packList, 0, 0); g.Controls.Add(buttons, 1, 0);
			g.Controls.Add(packUrl, 0, 1); g.Controls.Add(dlBtn, 1, 1);
			g.Controls.Add(note, 0, 2); g.SetColumnSpan(note, 2);
			g.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			page.Controls.Add(g);
			return page;
		}

		TabPage SettingsTab()
		{
			var page = new TabPage("Réglages");
			var g = Grid(2);
			g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			extraJvm = new TextBox { Text = cfg.ExtraJvmArgs, Dock = DockStyle.Fill };
			var dir = new LinkLabel { Text = Config.DataDir, AutoSize = true, Margin = new Padding(3, 8, 3, 8) };
			dir.LinkClicked += delegate { Directory.CreateDirectory(Config.DataDir); Process.Start("explorer.exe", Config.DataDir); };
			var logs = new LinkLabel { Text = "Ouvrir les journaux du jeu (logs)", AutoSize = true, Margin = new Padding(3, 8, 3, 8) };
			logs.LinkClicked += delegate { var p = Path.Combine(Config.GameDir, "logs"); Directory.CreateDirectory(p); Process.Start("explorer.exe", p); };
			var about = new Label
			{
				AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 12, 3, 3),
				Text = "Le launcher télécharge Minecraft Java depuis les serveurs officiels de Mojang, le Java 25 de Mojang, " +
					"Fabric et les mods depuis Modrinth. Sans compte Microsoft, le jeu se lance en mode hors ligne (pseudo), " +
					"comme sur les serveurs Eaglercraft ; avec un compte, tu peux aussi rejoindre les serveurs premium. Il faut posséder Minecraft Java Edition. Les arguments JVM du launcher sont déjà optimisés (G1, en-têtes d'objets compacts) ; " +
					"ajoute les tiens ci-dessus si besoin."
			};
			g.Controls.Add(Caption("Arguments JVM en plus"), 0, 0); g.Controls.Add(extraJvm, 1, 0);
			g.Controls.Add(Caption("Dossier du launcher"), 0, 1); g.Controls.Add(dir, 1, 1);
			g.Controls.Add(Caption("Journaux"), 0, 2); g.Controls.Add(logs, 1, 2);
			g.Controls.Add(about, 0, 3); g.SetColumnSpan(about, 2);
			for (int i = 0; i < 4; i++) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			page.Controls.Add(g);
			return page;
		}

		// ------------------------------------------------------------- helpers

		void Log(string line)
		{
			if (InvokeRequired) { BeginInvoke(new Action<string>(Log), line); return; }
			if (logBox.TextLength > 200000) logBox.Text = logBox.Text.Substring(100000);
			logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
		}

		void SetStatus(string text)
		{
			if (InvokeRequired) { BeginInvoke(new Action<string>(SetStatus), text); return; }
			status.Text = text;
			var m = Regex.Match(text, @"(\d+)/(\d+)");
			if (m.Success)
			{
				progress.Style = ProgressBarStyle.Continuous;
				progress.Value = Math.Min(100, (int)(100L * long.Parse(m.Groups[1].Value) / Math.Max(1, long.Parse(m.Groups[2].Value))));
			}
		}

		async Task LoadVersions()
		{
			try
			{
				var list = await Task.Run(() => new Installer(s => { }).ReleaseVersions());
				string current = version.Text;
				version.Items.Clear();
				foreach (var v in list.Take(40)) version.Items.Add(v);
				version.Text = current;
			}
			catch (Exception e) { Log("Liste des versions indisponible (" + e.Message + ")"); }
		}

		void FillJoin()
		{
			join.Items.Clear();
			join.Items.Add("Aucun — menu principal");
			int sel = 0;
			for (int i = 0; i < cfg.Servers.Count; i++)
			{
				join.Items.Add(cfg.Servers[i].Name);
				if (cfg.Servers[i].Name == cfg.JoinOnLaunch) sel = i + 1;
			}
			join.SelectedIndex = sel;
		}

		ServerEntry SelectedServer()
		{
			return serverList.SelectedItems.Count == 0 ? null : (ServerEntry)serverList.SelectedItems[0].Tag;
		}

		void RefreshServers()
		{
			serverList.Items.Clear();
			foreach (var s in cfg.Servers)
			{
				string packs = s.Packs == "accept" ? "Toujours accepter" : s.Packs == "refuse" ? "Refuser" : "Demander";
				string addr = s.Type == "wisp" ? s.Address + "  via " + s.Relay : s.Address;
				var item = new ListViewItem(new[] { s.Name, s.TypeLabel(), addr, packs }) { Tag = s };
				serverList.Items.Add(item);
			}
			if (join != null) FillJoin();
		}

		void SaveServers()
		{
			cfg.Save();
			try { ServerList.Sync(cfg); cfg.Save(); } catch (Exception e) { Log("servers.dat : " + e.Message); }
			RefreshServers();
		}

		void MoveServer(int delta)
		{
			var s = SelectedServer();
			if (s == null) return;
			int i = cfg.Servers.IndexOf(s), j = i + delta;
			if (j < 0 || j >= cfg.Servers.Count) return;
			cfg.Servers.RemoveAt(i);
			cfg.Servers.Insert(j, s);
			SaveServers();
			serverList.Items[j].Selected = true;
		}

		void EditServer(ServerEntry existing)
		{
			var s = existing ?? new ServerEntry { Type = "direct", Packs = "accept" };
			using (var d = new ServerDialog(s))
			{
				if (d.ShowDialog(this) != DialogResult.OK) return;
				if (existing == null) cfg.Servers.Add(s);
				if (s.Type == "wisp" && s.LocalPort == 0) s.LocalPort = cfg.FreeBridgePort();
				SaveServers();
			}
		}

		void RefreshPacks()
		{
			packList.Items.Clear();
			if (!Directory.Exists(Packs.Dir)) return;
			foreach (var p in Directory.GetFileSystemEntries(Packs.Dir).Select(Path.GetFileName).OrderBy(n => n))
				packList.Items.Add(p);
		}

		async Task DownloadPack()
		{
			string url = packUrl.Text.Trim();
			if (url.Length == 0) return;
			if (!url.Contains("://")) url = "https://" + url;
			try
			{
				string name = await Task.Run(() => Packs.Download(new Downloader(), url, SetStatus));
				SetStatus("Pack installé : " + name);
				Log("Pack installé : " + name);
				packUrl.Clear();
				RefreshPacks();
			}
			catch (Exception e)
			{
				SetStatus("Échec du téléchargement du pack.");
				MessageBox.Show(this, "Impossible de télécharger ce pack :\n" + e.Message, "Pack", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		// --------------------------------------------------------------- launch

		bool ReadForm()
		{
			string name = username.Text.Trim();
			if (!cfg.Microsoft() && !Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$"))
			{
				MessageBox.Show(this, "Le pseudo doit faire 3 à 16 caractères (lettres, chiffres, _).", "Pseudo", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return false;
			}
			cfg.Username = name;
			cfg.Version = version.Text.Trim();
			cfg.MemoryMb = (int)memory.Value;
			cfg.OptimizationMods = mods.Checked;
			cfg.CloseOnLaunch = minimize.Checked;
			cfg.ExtraJvmArgs = extraJvm.Text.Trim();
			cfg.NetworkMode = useRelay.Checked ? "relay" : "direct";
			cfg.RelayUrl = relayUrl.Text.Trim().Length > 0 ? relayUrl.Text.Trim() : Config.DefaultRelay;
			cfg.JoinOnLaunch = join.SelectedIndex > 0 ? cfg.Servers[join.SelectedIndex - 1].Name : null;
			cfg.Save();
			return true;
		}

		async Task Play()
		{
			if (game != null && !game.HasExited) { MessageBox.Show(this, "Minecraft est déjà lancé.", "Jouer"); return; }
			if (!ReadForm()) return;
			play.Enabled = false;
			progress.Value = 0;
			try
			{
				ServerEntry target = join.SelectedIndex > 0 ? cfg.Servers[join.SelectedIndex - 1] : null;
				string quick = target != null ? target.GameAddress() : null;
				Log("Préparation de Minecraft " + cfg.Version + (cfg.OptimizationMods ? " + Fabric" : "") + "…");
				StartBridges();
				int relayPort = relayBridge != null ? relayBridge.Port : 0;
				MinecraftSession session = null;
				if (cfg.Microsoft())
				{
					if (!cfg.SignedIn() && !SignIn())
					{
						SetStatus("Connexion Microsoft annulée. Connecte-toi, ou choisis « Sans compte ».");
						StopBridges();
						play.Enabled = true;
						return;
					}
					try { session = await CurrentSession(); }
					catch (Exception ex)
					{
						cfg.SignOut(); cfg.Save(); RefreshAccount();
						throw new Exception(ex.Message + "\nReconnecte-toi, ou choisis « Sans compte » pour jouer avec un pseudo.");
					}
				}
				var plan = await Task.Run(() => new Installer(SetStatus).Prepare(cfg, quick, relayPort, session));
				ServerList.Sync(cfg);
				cfg.Save();
				StartGame(plan);
			}
			catch (Exception e)
			{
				SetStatus("Erreur : " + e.Message);
				Log("ERREUR : " + e);
				MessageBox.Show(this, e.Message, "Lancement impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
				StopBridges();
				play.Enabled = true;
			}
		}

		void StartBridges()
		{
			StopBridges();
			if (cfg.RelayEnabled())
			{
				relayBridge = new RelayBridge(cfg.RelayUrl, Log);
				relayBridge.Start(27150);
				Log("Relais actif : toutes les connexions passent par " + cfg.RelayUrl + " (port local " + relayBridge.Port + ").");
			}
			foreach (var s in cfg.Servers.Where(x => x.Type == "wisp"))
			{
				try
				{
					var b = new WispBridge(s.Name, s.Relay, s.Address, s.LocalPort, Log);
					b.Start();
					bridges.Add(b);
					Log("Tunnel Wisp « " + s.Name + " » : 127.0.0.1:" + s.LocalPort + " → " + s.Address + " via " + s.Relay);
				}
				catch (Exception e) { Log("Tunnel Wisp « " + s.Name + " » impossible : " + e.Message); }
			}
		}

		void StopBridges()
		{
			foreach (var b in bridges) b.Stop();
			bridges.Clear();
			if (relayBridge != null) { relayBridge.Stop(); relayBridge = null; }
		}

		// Errors an offline (username-only) session always produces: Realms, Mojang
		// account attributes, profile keys, telemetry. They never affect play.
		static readonly Regex OfflineNoise = new Regex(
			@"[Rr]ealms|SignedJWT|InvalidCredentials|player/attributes|profile key|user properties|[Tt]elemetry|authlib|Status: 401",
			RegexOptions.Compiled);

		static string ArgFileQuote(string a)
		{
			return "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
		}

		void StartGame(LaunchPlan plan)
		{
			// An @argfile keeps the long classpath clear of the Windows command-line limit.
			string argFile = Path.Combine(Config.DataDir, "derniers-arguments.txt");
			File.WriteAllText(argFile, string.Join("\n", plan.Args.Select(ArgFileQuote)), new UTF8Encoding(false));
			var psi = new ProcessStartInfo(plan.Java, "@\"" + argFile + "\"")
			{
				WorkingDirectory = plan.GameDir,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			game = new Process { StartInfo = psi, EnableRaisingEvents = true };
			DataReceivedEventHandler onLine = (s, e) =>
			{
				if (e.Data == null) return;
				bool important = e.Data.Contains("/ERROR]") || e.Data.Contains("/FATAL]") || e.Data.Contains("Connecting to");
				if (important && !OfflineNoise.IsMatch(e.Data)) Log(e.Data.Length > 300 ? e.Data.Substring(0, 300) + "…" : e.Data);
			};
			Log("Mode hors ligne : les erreurs Realms / compte Mojang sont normales et masquées (détails dans logs\\latest.log).");
			game.OutputDataReceived += onLine;
			game.ErrorDataReceived += onLine;
			game.Exited += delegate { BeginInvoke(new Action(OnGameExit)); };
			game.Start();
			game.BeginOutputReadLine();
			game.BeginErrorReadLine();
			progress.Value = 100;
			SetStatus("Minecraft " + plan.VersionLabel + " est lancé" + (bridges.Count > 0 || relayBridge != null ? " — garde le launcher ouvert (relais actif)." : "."));
			Log("Minecraft lancé (pid " + game.Id + ").");
			if (cfg.CloseOnLaunch) WindowState = FormWindowState.Minimized;
		}

		void OnGameExit()
		{
			int code = game.ExitCode;
			StopBridges();
			play.Enabled = true;
			WindowState = FormWindowState.Normal;
			SetStatus(code == 0 ? "Minecraft fermé." : "Minecraft s'est arrêté (code " + code + "). Voir Réglages → Journaux.");
			Log("Minecraft fermé (code " + code + ").");
		}

		void OnClosing(object sender, FormClosingEventArgs e)
		{
			if (game != null && !game.HasExited && (bridges.Count > 0 || relayBridge != null) &&
				MessageBox.Show(this, "Minecraft est encore ouvert : fermer le launcher coupe la connexion aux serveurs (relais). Fermer quand même ?",
					"Eagler Java Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
			{
				e.Cancel = true;
				return;
			}
			try { ReadFormQuiet(); } catch { }
			StopBridges();
		}

		void ReadFormQuiet()
		{
			cfg.Username = username.Text.Trim();
			cfg.Version = version.Text.Trim();
			cfg.MemoryMb = (int)memory.Value;
			cfg.OptimizationMods = mods.Checked;
			cfg.CloseOnLaunch = minimize.Checked;
			cfg.ExtraJvmArgs = extraJvm.Text.Trim();
			cfg.NetworkMode = useRelay.Checked ? "relay" : "direct";
			cfg.RelayUrl = relayUrl.Text.Trim().Length > 0 ? relayUrl.Text.Trim() : Config.DefaultRelay;
			cfg.Save();
		}

		// ------------------------------------------------------------ account

		// Offline: username box. Microsoft: account name + sign in / sign out.
		void RefreshAccount()
		{
			bool ms = cfg.Microsoft();
			username.Visible = !ms;
			accountLabel.Visible = ms;
			accountButton.Visible = ms;
			if (!ms) return;
			bool signedIn = cfg.SignedIn();
			accountLabel.Text = signedIn ? cfg.MsName : "Non connecté";
			accountLabel.Font = new Font(Font, signedIn ? FontStyle.Bold : FontStyle.Italic);
			accountButton.Text = signedIn ? "Se déconnecter" : "Se connecter…";
		}

		void AccountClicked()
		{
			if (cfg.SignedIn())
			{
				if (MessageBox.Show(this, "Se déconnecter du compte " + cfg.MsName + " ?", "Compte", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
				cfg.SignOut();
				cfg.Save();
				RefreshAccount();
				return;
			}
			SignIn();
		}

		bool SignIn()
		{
			using (var d = new LoginDialog())
			{
				if (d.ShowDialog(this) != DialogResult.OK || d.Session == null) return false;
				StoreSession(d.Session, d.RefreshToken);
				Log("Connecté avec le compte Microsoft " + d.Session.Name + ".");
				RefreshAccount();
				return true;
			}
		}

		void StoreSession(MinecraftSession s, string refreshToken)
		{
			cfg.AuthMode = "microsoft";
			cfg.MsRefreshToken = MicrosoftAuth.Protect(refreshToken);
			cfg.MsName = s.Name;
			cfg.MsUuid = s.Uuid;
			cfg.MsXuid = s.Xuid;
			cfg.McToken = MicrosoftAuth.Protect(s.AccessToken);
			cfg.McTokenExpiryTicks = s.ExpiresUtc.Ticks;
			cfg.Save();
		}

		// Reuses the Minecraft token while valid, otherwise refreshes the whole chain.
		async Task<MinecraftSession> CurrentSession()
		{
			if (!cfg.Microsoft() || !cfg.SignedIn()) return null;
			string token = MicrosoftAuth.Unprotect(cfg.McToken);
			var expiry = new DateTime(cfg.McTokenExpiryTicks, DateTimeKind.Utc);
			if (token != null && expiry > DateTime.UtcNow.AddMinutes(10))
				return new MinecraftSession { Name = cfg.MsName, Uuid = cfg.MsUuid, Xuid = cfg.MsXuid, AccessToken = token, ExpiresUtc = expiry };
			string refresh = MicrosoftAuth.Unprotect(cfg.MsRefreshToken);
			if (refresh == null) throw new Exception("Session Microsoft illisible : reconnecte-toi.");
			SetStatus("Compte Microsoft : renouvellement de la session…");
			var auth = new MicrosoftAuth();
			var ms = await Task.Run(() => auth.Refresh(refresh));
			var session = await Task.Run(() => auth.Minecraft(ms.Item1));
			StoreSession(session, ms.Item2);
			return session;
		}
	}

	class ServerDialog : Form
	{
		readonly ServerEntry entry;
		readonly TextBox name, address, relay;
		readonly ComboBox type, packs;
		readonly Label hint;

		static readonly string[] Types = { "direct", "wisp", "eaglerx" };
		static readonly string[] PackModes = { "prompt", "accept", "refuse" };

		public ServerDialog(ServerEntry s)
		{
			entry = s;
			Text = s.Name == null ? "Ajouter un serveur" : "Modifier le serveur";
			Font = new Font("Segoe UI", 9.5f);
			AutoScaleMode = AutoScaleMode.Dpi;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = MinimizeBox = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(520, 300);

			var g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
			g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			name = new TextBox { Text = s.Name ?? "", Dock = DockStyle.Fill };
			type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
			type.Items.AddRange(new object[] { "Java direct", "Wisp (tunnel wss://)", "EaglerX (adresse wss://)" });
			type.SelectedIndex = Math.Max(0, Array.IndexOf(Types, s.Type ?? "direct"));
			address = new TextBox { Text = s.Address ?? "", Dock = DockStyle.Fill };
			relay = new TextBox { Text = s.Relay ?? "", Dock = DockStyle.Fill };
			packs = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
			packs.Items.AddRange(new object[] { "Demander", "Toujours accepter", "Refuser" });
			packs.SelectedIndex = Math.Max(0, Array.IndexOf(PackModes, s.Packs ?? "accept"));
			hint = new Label { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 8, 3, 3) };

			var ok = new Button { Text = "Enregistrer", DialogResult = DialogResult.None, Width = 110, Height = 30 };
			var cancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, Width = 110, Height = 30 };
			ok.Click += delegate { Commit(); };
			AcceptButton = ok;
			CancelButton = cancel;
			var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
			bar.Controls.Add(cancel);
			bar.Controls.Add(ok);

			Func<string, Label> cap = t => new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 10, 7) };
			g.Controls.Add(cap("Nom"), 0, 0); g.Controls.Add(name, 1, 0);
			g.Controls.Add(cap("Connexion"), 0, 1); g.Controls.Add(type, 1, 1);
			g.Controls.Add(cap("Adresse"), 0, 2); g.Controls.Add(address, 1, 2);
			g.Controls.Add(cap("Relais Wisp"), 0, 3); g.Controls.Add(relay, 1, 3);
			g.Controls.Add(cap("Packs serveur"), 0, 4); g.Controls.Add(packs, 1, 4);
			g.Controls.Add(hint, 0, 5); g.SetColumnSpan(hint, 2);
			g.Controls.Add(bar, 0, 6); g.SetColumnSpan(bar, 2);
			for (int i = 0; i < 7; i++) g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(g);
			type.SelectedIndexChanged += delegate { UpdateHint(); };
			UpdateHint();
		}

		void UpdateHint()
		{
			string t = Types[type.SelectedIndex];
			relay.Enabled = t == "wisp";
			if (t == "wisp") hint.Text = "Adresse = serveur Minecraft de destination (host:port), relais = URL wss:// du serveur Wisp (la même que dans Eaglercraft → Wisp Settings).";
			else if (t == "eaglerx") hint.Text = "Adresse = l'adresse wss:// du serveur Eaglercraft. Minecraft Java s'y connecte en TCP sur le même hôte (port 25565 si l'URL n'a pas de port spécial). Si ça ne marche pas, demande l'adresse Java du serveur.";
			else hint.Text = "Adresse = adresse Minecraft Java classique (play.exemple.fr ou 1.2.3.4:25565).";
		}

		void Commit()
		{
			string t = Types[type.SelectedIndex];
			string a = address.Text.Trim();
			if (a.Length == 0) { MessageBox.Show(this, "Entre une adresse.", Text); return; }
			if (t == "wisp" && relay.Text.Trim().Length == 0) { MessageBox.Show(this, "Entre l'URL du relais Wisp (wss://…).", Text); return; }
			if (t == "direct" && a.Contains("://"))
			{
				MessageBox.Show(this, "Une adresse wss:// est une adresse Eaglercraft : choisis EaglerX ou Wisp.", Text);
				return;
			}
			entry.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : a;
			entry.Type = t;
			entry.Address = a;
			entry.Relay = t == "wisp" ? relay.Text.Trim() : null;
			entry.Packs = PackModes[packs.SelectedIndex];
			DialogResult = DialogResult.OK;
			Close();
		}
	}

	class LoginDialog : Form
	{
		readonly MicrosoftAuth auth;
		readonly Label info, code, state;
		readonly Button open, copy;
		readonly System.Threading.CancellationTokenSource cts = new System.Threading.CancellationTokenSource();
		DeviceCode device;
		public MinecraftSession Session;
		public string RefreshToken;

		public LoginDialog()
		{
			auth = new MicrosoftAuth();
			Text = "Connexion Microsoft";
			Font = new Font("Segoe UI", 9.5f);
			AutoScaleMode = AutoScaleMode.Dpi;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = MinimizeBox = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(460, 250);
			var g = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1 };
			info = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Text = "Demande d'un code à Microsoft…" };
			code = new Label { AutoSize = true, Font = new Font("Consolas", 22f, FontStyle.Bold), Margin = new Padding(3, 12, 3, 12) };
			open = new Button { Text = "Ouvrir la page de connexion", AutoSize = true, Enabled = false };
			copy = new Button { Text = "Copier le code", AutoSize = true, Enabled = false };
			state = new Label { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 10, 3, 3) };
			var bar = new FlowLayoutPanel { AutoSize = true };
			bar.Controls.Add(open);
			bar.Controls.Add(copy);
			g.Controls.Add(info);
			g.Controls.Add(code);
			g.Controls.Add(bar);
			g.Controls.Add(state);
			Controls.Add(g);
			open.Click += delegate { try { Process.Start(device.VerificationUri); } catch { } };
			copy.Click += delegate { try { Clipboard.SetText(device.UserCode); state.Text = "Code copié."; } catch { } };
			Shown += async delegate { await Run(); };
			FormClosing += delegate { cts.Cancel(); };
		}

		async Task Run()
		{
			try
			{
				device = await Task.Run(() => auth.Begin());
				info.Text = "Va sur " + device.VerificationUri + ", entre ce code, puis connecte-toi avec le compte qui possède Minecraft :";
				code.Text = device.UserCode;
				open.Enabled = copy.Enabled = true;
				try { Clipboard.SetText(device.UserCode); } catch { }
				try { Process.Start(device.VerificationUri); } catch { }
				state.Text = "Code copié dans le presse-papiers. En attente de ta connexion…";
				var tokens = await Task.Run(() => auth.WaitForApproval(device, cts.Token));
				state.Text = "Connexion à Xbox Live et à Minecraft…";
				Session = await Task.Run(() => auth.Minecraft(tokens.Item1));
				RefreshToken = tokens.Item2;
				DialogResult = DialogResult.OK;
				Close();
			}
			catch (OperationCanceledException) { }
			catch (Exception e)
			{
				if (IsDisposed) return;
				state.ForeColor = Color.Firebrick;
				state.Text = e.GetBaseException().Message;
			}
		}
	}
}
