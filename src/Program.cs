using System;
using System.Threading;
using System.Windows.Forms;

namespace RoundedTask
{
    internal static class Program
    {
        private const string SingleInstanceName = @"Local\RoundedTask.SingleInstance";
        private const string ShowSettingsSignalName = @"Local\RoundedTask.ShowSettings";

        [STAThread]
        private static void Main(string[] args)
        {
            string command = args != null && args.Length > 0 ? args[0].Trim().ToLowerInvariant() : String.Empty;
            if (IsUtilityCommand(command))
            {
                RunCommand(command);
                return;
            }

            bool createdNew;
            bool eventCreated;
            using (EventWaitHandle showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignalName, out eventCreated))
            using (Mutex mutex = new Mutex(true, SingleInstanceName, out createdNew))
            {
                if (!createdNew)
                {
                    if (command != "--tray")
                    {
                        showSettingsSignal.Set();
                    }

                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new RoundedTaskContext(showSettingsSignal, command != "--tray"));
            }
        }

        private static bool IsUtilityCommand(string command)
        {
            return command == "--restore" || command == "--apply-once";
        }

        private static void RunCommand(string command)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (command == "--restore")
            {
                TaskbarStyler styler = new TaskbarStyler(new AppSettings());
                styler.RestoreAll();
                return;
            }

            if (command == "--apply-once")
            {
                AppSettings settings = AppSettings.Load();
                TaskbarStyler styler = new TaskbarStyler(settings);
                styler.Apply(true);
                return;
            }
        }
    }
}
