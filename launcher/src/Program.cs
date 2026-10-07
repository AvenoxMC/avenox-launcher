using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Eagler Java Launcher")]
[assembly: System.Reflection.AssemblyProduct("Eagler Java Launcher")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
namespace EaglerLauncher
{
	static class Program
	{
		[DllImport("user32.dll")]
		static extern bool SetProcessDPIAware();

		[STAThread]
		static void Main()
		{
			bool created;
			using (var single = new Mutex(true, "EaglerJavaLauncher-instance", out created))
			{
				if (!created)
				{
					MessageBox.Show("Le launcher est déjà ouvert.", "Eagler Java Launcher");
					return;
				}
				try { SetProcessDPIAware(); } catch { }
				// TLS 1.2/1.3 for Mojang, Fabric and Modrinth (3072 | 12288).
				ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 12288);
				ServicePointManager.DefaultConnectionLimit = 32;
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				Application.Run(new MainForm());
			}
		}
	}
}
