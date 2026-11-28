using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace Bubliklyuk
{
    internal static class AttackAudioLifecycleChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static void Run(Action<bool, string> check)
        {
            foreach (bool failRestore in new bool[] { false, true })
            {
                Device device = new Device();
                Session game = new Session { Id = "game", SessionIdentifier = "game", Volume = 0.8f };
                Session mutedBefore = new Session { Id = "muted-before", SessionIdentifier = "muted-before", Mute = true, Volume = 0.4f };
                device.Sessions.Add(game); device.Sessions.Add(mutedBefore);
                AudioMute audio = new AudioMute(new Devices(device), false);
                Settings settings = new Settings { NightFrom = TimeSpan.Zero, NightTo = TimeSpan.Zero,
                    SchoolFrom = TimeSpan.Zero, SchoolTo = TimeSpan.Zero };
                GameContext context = (GameContext)FormatterServices.GetUninitializedObject(typeof(GameContext));
                GC.SuppressFinalize(context);
                Timer cycle = new Timer();
                typeof(GameContext).GetField("settings", Private).SetValue(context, settings);
                typeof(GameContext).GetField("nightSession", Private).SetValue(context, new NightSession());
                typeof(GameContext).GetField("audio", Private).SetValue(context, audio);
                typeof(GameContext).GetField("cycleTimer", Private).SetValue(context, cycle);
                // Keep scheduling dormant after restoration; no real hotkeys, sound or windows.
                typeof(GameContext).GetField("waitingForInput", Private).SetValue(context, true);
                try
                {
                    using (AttackForm form = new AttackForm(settings, new TaskGenerator(settings), 4,
                        delegate(Exception error) { throw error; }))
                    {
                        form.TopMost = false;
                        typeof(GameContext).GetField("attack", Private).SetValue(context, form);
                        FormClosedEventHandler closed = (FormClosedEventHandler)Delegate.CreateDelegate(
                            typeof(FormClosedEventHandler), context, typeof(GameContext).GetMethod("OnAttackClosed", Private));
                        form.FormClosed += closed;
                        audio.BeginAttack();
                        check(game.Mute && mutedBefore.Mute, "Lifecycle fixture mutes other applications during attack");
                        game.FailUnmute = failRestore;
                        // Exercise AttackForm -> real FormClosed event -> GameContext restore.
                        typeof(AttackForm).GetMethod("OnFormClosed", Private).Invoke(form,
                            new object[] { new FormClosedEventArgs(CloseReason.None) });
                        check(typeof(GameContext).GetField("attack", Private).GetValue(context) == null,
                            "Attack close releases the owning form reference");
                        if (failRestore)
                        {
                            check(game.Mute && audio.HasPendingRestore, "Close retains failed audio restoration for retry");
                            game.FailUnmute = false;
                            audio.Refresh();
                        }
                        check(!game.Mute && mutedBefore.Mute && game.Volume == 0.8f && !audio.HasPendingRestore,
                            "Attack completion restores audible game while preserving pre-existing mute and volume");
                        audio.Refresh(); audio.Restore();
                        check(!game.Mute, "Late refresh or repeated close cannot re-mute a completed attack");
                    }
                }
                finally { game.FailUnmute = false; audio.Restore(); cycle.Dispose(); }
            }
        }

        private sealed class Devices : IAttackAudioDevices
        {
            private readonly Device device;
            internal Devices(Device device) { this.device = device; }
            public IList<IAttackAudioDevice> OpenActiveDevices() { return new List<IAttackAudioDevice> { device }; }
        }
        private sealed class Device : IAttackAudioDevice
        {
            public string Id { get { return "lifecycle-device"; } }
            public bool IsDefault { get { return true; } }
            public bool Mute { get; set; }
            public float Volume { get; set; }
            internal readonly List<IAttackAudioSession> Sessions = new List<IAttackAudioSession>();
            internal Device() { Volume = 0.6f; }
            public IList<IAttackAudioSession> OpenSessions() { return new List<IAttackAudioSession>(Sessions); }
            public void Dispose() { }
        }
        private sealed class Session : IAttackAudioSession
        {
            public string Id { get; set; }
            public string SessionIdentifier { get; set; }
            public bool IsExpired { get { return false; } }
            public bool IsOwn { get { return false; } }
            public float Volume { get; set; }
            private bool mute;
            internal bool FailUnmute;
            public bool Mute
            {
                get { return mute; }
                set { if (!value && FailUnmute) throw new InvalidOperationException("Temporary audio restore failure"); mute = value; }
            }
            public void Dispose() { }
        }
    }
}
