using System;
using Alta.Networking;
using UnityEngine;

namespace ArmorRestored
{
    internal static class Gate
    {
        internal static bool IsOpen;
        internal static string ServerVersion;
        static bool _clearPending;
        static Connection _listening;

        internal static void Listen(Connection connection)
        {
            if (IsOpen) Close("a new server connection");
            Unregister();
            _listening = connection;
            Net.ToServer = connection;
            connection.SetHandler(Net.Message, Net.ClientRead);
            connection.Disconnected += Disconnected;
        }

        static void Disconnected(Connection connection)
        {
            if (ReferenceEquals(connection, _listening)) Close("left the server");
        }

        internal static void Open(Connection connection, string version)
        {
            if (IsOpen || !ReferenceEquals(connection, _listening)) return;
            IsOpen = true;
            ServerVersion = version ?? "";
            Mod.Log.Msg("this server runs ArmorRestored " + ServerVersion + ": armor is on until you leave.");
            Net.SendAck(connection);
            _loadFrame = Time.frameCount + 2;
            _loadTime = Time.unscaledTime + 0.2f;
            _loading = true;
        }

        static bool _loading;
        static int _loadFrame;
        static float _loadTime;

        static void Load()
        {
            _loading = false;
            Connection connection = _listening;
            if (!IsOpen || connection == null || connection.IsDisposed) return;
            if (!Pack.Loaded)
            {
                Pack.Load(true);
                Catalog.Check();
            }
            Prefabs.Register("announce");
            Net.SendHello(connection);
            Mod.Log.Msg("hello sent on message " + (int)Net.Message + "; refused: " + (Prefabs.RefusedLine().Length == 0 ? "none" : Prefabs.RefusedLine()));
        }

        internal static void Close(string why)
        {
            bool was = IsOpen;
            IsOpen = false;
            _loading = false;
            _listening = null;
            Net.ToServer = null;
            ClientArmor.Clear();
            _clearPending = Prefabs.Done;
            if (was) Mod.Log.Msg("armor off: " + why + ".");
        }

        internal static void Tick()
        {
            if (_listening != null && _listening.IsDisposed) Close("left the server");
            if (_loading && Time.frameCount >= _loadFrame && Time.unscaledTime >= _loadTime) Load();
        }

        internal static void SceneLoaded(string scene)
        {
            if (!IsOpen && _listening == null) Unregister();
        }

        static void Unregister()
        {
            if (!_clearPending && !Prefabs.Done) return;
            _clearPending = false;
            try { Mod.Log.Msg(Prefabs.Unregister()); }
            catch (Exception e) { Mod.Log.Error("could not remove the armor pieces: " + e); }
        }
    }
}
