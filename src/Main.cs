using System;
using System.Runtime.CompilerServices;
using Alta.Console;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(ArmorRestored.Main), ArmorRestored.Mod.Name, ArmorRestored.Mod.Version, "Shadow")]
[assembly: MelonGame("Alta", "A Township Tale")]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]

namespace ArmorRestored
{
    internal static class Mod
    {
        internal const string Name = "ArmorRestored";
        internal const string Version = "0.2.0";

        internal static bool IsServer;
        internal static MelonLogger.Instance Log = new MelonLogger.Instance(Name);
        internal static string DataDir;
    }

    public class Main : MelonMod
    {
        bool _running;
        string _lastTickError;

        public override void OnInitializeMelon()
        {
            Mod.Log = LoggerInstance;
            Mod.IsServer = IsServer();
            Mod.DataDir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, Mod.Name);
            try
            {
                System.IO.Directory.CreateDirectory(Mod.DataDir);
                Settings.Load();
                Pack.Load(!Mod.IsServer);
                Catalog.Check();
                if (Mod.IsServer) ServerArmor.Load();
                _running = true;
                Mod.Log.Msg(Mod.Version + " started on the " + (Mod.IsServer ? "server" : "game client") + ": "
                    + Pack.Pieces.Count + " pieces, join check " + Settings.Join + ", loot " + (Settings.Loot ? "on" : "off") + ".");
            }
            catch (Exception e)
            {
                Mod.Log.Error("failed to start, staying off: " + e);
            }
        }

        public override void OnUpdate()
        {
            if (!_running) return;
            try
            {
                if (Mod.IsServer) ServerArmor.Tick();
                else ClientArmor.Tick();
            }
            catch (Exception e) { Report(e); }
        }

        void Report(Exception e)
        {
            string m = e.GetType().Name + ": " + e.Message;
            if (m == _lastTickError) return;
            _lastTickError = m;
            Mod.Log.Error("tick threw: " + e);
        }

        internal static void RegisterCommands()
        {
            try
            {
                CommandService.CommandCollection.Collect(typeof(Main).Assembly);
                Mod.Log.Msg("console commands registered: armor.");
            }
            catch (Exception e)
            {
                Mod.Log.Error("console command registration failed: " + e.Message);
            }
        }

        static bool IsServer()
        {
            return ApplicationManager.IsHeadless
                || CommandLineArguments.Contains(CommandLineArguments.StartServerArgument);
        }
    }
}
