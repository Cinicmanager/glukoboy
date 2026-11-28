using System;
using System.Collections.Generic;

namespace Bubliklyuk
{
    // Each enumeration returns disposable handles over shared state, like COM.
    // No Windows audio setters or playback streams are used by these checks.
    internal static class AudioRestoreChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            Recreated(check, false, false);
            Recreated(check, true, false);
            Recreated(check, true, true);
            RecreatedAtRelease(check);
            IndependentInstances(check);
            RepeatedAttacks(check);
            FreshHandles(check);
            FailedRestoration(check);
            EnumerationRecovery(check);
            PartialEnumeration(check);
            DeviceReconnect(check);
            VolumeRecovery(check);
            ReadBack(check);
            RestartWhilePending(check);
            DisposalFailure(check);
        }

        private static void Recreated(Action<bool, string> check, bool expired, bool replacementFirst)
        {
            Rig rig = new Rig();
            Session first = rig.Add("roblox-1", "roblox", false);
            rig.Mute.BeginAttack();
            Session second = new Session("roblox-2", "roblox", first.Muted);
            first.Expired = expired; first.Disconnected = true;
            rig.Endpoint.Sessions.Clear();
            if (expired && !replacementFirst) rig.Endpoint.Sessions.Add(first);
            rig.Endpoint.Sessions.Add(second);
            if (expired && replacementFirst) rig.Endpoint.Sessions.Add(first);
            rig.Mute.Refresh();
            check(second.Muted, "Recreated session remains silent during attack");
            rig.Mute.Begin(); // attack -> night must retain the same original
            rig.Mute.Restore();
            check(!second.Muted && !rig.Mute.HasPendingRestore,
                "Absent/expired predecessor transfers original mute independent of enumeration order");
            check(rig.Source.LiveHandles == 0, "Recreated session restoration releases every acquired handle");
        }

        private static void RecreatedAtRelease(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session first = rig.Add("roblox-1", "roblox", false);
            rig.Mute.BeginAttack();
            Session second = new Session("roblox-2", "roblox", first.Muted);
            first.Disconnected = true;
            rig.Endpoint.Sessions.Clear(); rig.Endpoint.Sessions.Add(second);
            // No timer tick between recreation and victory.
            rig.Mute.Restore();
            check(!second.Muted && !rig.Mute.HasPendingRestore,
                "Victory discovers and releases a replacement before the next attack poll");

            rig.Mute.BeginAttack();
            Session third = new Session("roblox-3", "roblox", second.Muted);
            second.Disconnected = true;
            rig.Endpoint.Sessions.Clear(); rig.Endpoint.Sessions.Add(third); rig.Mute.Refresh();
            Session fourth = new Session("roblox-4", "roblox", third.Muted);
            third.Disconnected = true;
            rig.Endpoint.Sessions.Clear(); rig.Endpoint.Sessions.Add(fourth); rig.Mute.Refresh();
            rig.Mute.Restore();
            check(!fourth.Muted && !rig.Mute.HasPendingRestore,
                "Several successive recreations retain the first unmuted baseline");
        }

        private static void IndependentInstances(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session first = rig.Add("game-one", "same-application", false);
            Session second = rig.Add("game-two", "same-application", true);
            rig.Mute.BeginAttack(); rig.Mute.Restore();
            check(!first.Muted && second.Muted, "Simultaneous instances preserve their distinct original mutes");

            rig.Mute.BeginAttack();
            Session third = rig.Add("game-three", "same-application", true);
            rig.Mute.Refresh(); rig.Mute.Restore();
            check(!first.Muted && second.Muted && third.Muted,
                "A muted live peer is never unmuted merely because its application identity matches");

            Rig endpoints = new Rig();
            Session audible = endpoints.Add("one", "application", false);
            Endpoint separate = new Endpoint("headphones");
            Session intentionallyMuted = new Session("two", "application", true);
            separate.Sessions.Add(intentionallyMuted); endpoints.Source.Items.Add(separate);
            endpoints.Mute.BeginAttack(); endpoints.Mute.Restore();
            check(!audible.Muted && intentionallyMuted.Muted,
                "Session originals stay scoped to their endpoint even for matching application identifiers");
        }

        private static void RepeatedAttacks(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session first = rig.Add("game-1", "game", true);
            rig.Mute.BeginAttack();
            Session second = new Session("game-2", "game", first.Muted);
            first.Disconnected = true;
            rig.Endpoint.Sessions.Clear(); rig.Endpoint.Sessions.Add(second);
            rig.Mute.Refresh(); rig.Mute.Restore();
            check(second.Muted && !rig.Mute.HasPendingRestore,
                "A deliberately muted application stays muted after recreation and victory");
            second.Muted = false;
            rig.Mute.BeginAttack(); rig.Mute.Restore(); rig.Mute.Refresh();
            check(!second.Muted && !rig.Mute.HasPendingRestore,
                "A later attack captures the user's new baseline and idle refresh cannot mute again");
        }

        private static void FreshHandles(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session session = rig.Add("game", "game", false);
            rig.Endpoint.Muted = true; rig.Endpoint.Level = 0;
            rig.Mute.BeginAttack();
            // Both old RCWs are stale, but the same IDs can be reacquired.
            rig.Endpoint.Generation++; session.Generation++;
            rig.Mute.Restore();
            check(!session.Muted && rig.Endpoint.Muted && rig.Endpoint.Level == 0 && !rig.Mute.HasPendingRestore,
                "Restore reacquires current device/session handles instead of retrying stale handles");
            check(rig.Source.LiveHandles == 0, "Fresh-handle restoration disposes old and new handles exactly once");
        }

        private static void FailedRestoration(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session blocked = rig.Add("blocked", "blocked", false);
            Session healthy = rig.Add("healthy", "healthy", false);
            rig.Mute.BeginAttack(); blocked.FailMute = true;
            rig.Mute.Restore();
            check(blocked.Muted && !healthy.Muted && rig.Mute.HasPendingRestore,
                "Two failed restore attempts retain the original and do not block healthy sessions");
            healthy.Muted = true; // User changes an already released app.
            blocked.FailMute = false; rig.Mute.Refresh();
            check(!blocked.Muted && healthy.Muted && !rig.Mute.HasPendingRestore,
                "Idle retry restores only unfinished properties and preserves later user changes");
            check(rig.Source.LiveHandles == 0, "Successful retry releases all pending handles");
        }

        private static void EnumerationRecovery(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session first = rig.Add("game-1", "game", false);
            rig.Mute.BeginAttack();
            Session replacement = new Session("game-2", "game", first.Muted);
            rig.Endpoint.Sessions.Clear(); rig.Endpoint.Sessions.Add(replacement);
            rig.Endpoint.FailSessions = true;
            // The old setter can succeed while the new session remains unseen.
            rig.Mute.Restore();
            check(!first.Muted && replacement.Muted && rig.Mute.HasPendingRestore,
                "Failed enumeration retains history even after an old handle reports successful restoration");
            rig.Endpoint.FailSessions = false; rig.Mute.Refresh();
            check(!replacement.Muted && !rig.Mute.HasPendingRestore,
                "Recovered session enumeration releases the previously unseen replacement");

            rig.Mute.BeginAttack(); rig.Source.FailEnumeration = true;
            replacement.Generation++; rig.Mute.Restore();
            check(rig.Mute.HasPendingRestore, "Device enumeration failures retain pending originals");
            rig.Source.FailEnumeration = false; rig.Mute.Refresh();
            check(!replacement.Muted && !rig.Mute.HasPendingRestore, "Device enumeration recovery reacquires the session");
        }

        private static void DeviceReconnect(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session game = rig.Add("game", "game", false);
            rig.Endpoint.Muted = true;
            rig.Mute.BeginAttack();
            rig.Endpoint.Disconnected = true; game.Disconnected = true; rig.Source.Items.Clear();
            rig.Mute.Restore();
            check(rig.Mute.HasPendingRestore, "An unplugged device keeps its unrestored snapshot");
            rig.Endpoint.Disconnected = false; game.Disconnected = false;
            rig.Endpoint.Generation++; game.Generation++; rig.Source.Items.Add(rig.Endpoint);
            rig.Mute.Refresh();
            check(rig.Endpoint.Muted && !game.Muted && !rig.Mute.HasPendingRestore,
                "Reconnected output restores its own master and session originals");
        }

        private static void PartialEnumeration(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session first = rig.Add("game-1", "game", false);
            rig.Mute.BeginAttack();
            Session replacement = new Session("game-2", "game", first.Muted);
            replacement.FailMuteRead = true;
            rig.Endpoint.Sessions.Clear(); rig.Endpoint.Sessions.Add(replacement);
            rig.Mute.Restore();
            check(!first.Muted && replacement.Muted && rig.Mute.HasPendingRestore,
                "A failing replacement getter prevents partial enumeration from discarding its history");
            replacement.FailMuteRead = false; rig.Mute.Refresh();
            check(!replacement.Muted && !rig.Mute.HasPendingRestore,
                "A replacement whose getter recovered inherits the original unmuted state");

            rig.Mute.BeginAttack();
            Session third = new Session("game-3", "game", replacement.Muted);
            rig.Endpoint.Sessions.Clear(); rig.Endpoint.PartialSessions = true;
            rig.Mute.Restore();
            check(rig.Mute.HasPendingRestore, "Native partial-session enumeration explicitly retains originals");
            rig.Endpoint.PartialSessions = false; rig.Endpoint.Sessions.Add(third); rig.Mute.Refresh();
            check(!third.Muted && !rig.Mute.HasPendingRestore,
                "A previously omitted native session is restored when enumeration recovers");
        }

        private static void VolumeRecovery(Action<bool, string> check)
        {
            Rig rig = new Rig();
            rig.Endpoint.Muted = true; rig.Endpoint.Level = 0;
            Session own = rig.Add("own", "own", true); own.Own = true; own.Level = 0;
            rig.Mute.BeginAttack();
            own.FailVolume = true; rig.Endpoint.FailVolume = true;
            rig.Mute.Restore();
            check(own.Muted && rig.Endpoint.Muted && own.Level == 1 && rig.Endpoint.Level == 0.25f && rig.Mute.HasPendingRestore,
                "Mute restoration proceeds when original zero-volume restoration fails");
            own.Muted = false; rig.Endpoint.Muted = false;
            own.FailVolume = false; rig.Endpoint.FailVolume = false; rig.Mute.Refresh();
            check(own.Level == 0 && rig.Endpoint.Level == 0 && !own.Muted && !rig.Endpoint.Muted && !rig.Mute.HasPendingRestore,
                "Volume retry preserves already-restored mute properties changed later by the user");
        }

        private static void ReadBack(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session session = rig.Add("game", "game", false);
            rig.Mute.BeginAttack(); session.IgnoreMute = true; rig.Mute.Restore();
            check(session.Muted && rig.Mute.HasPendingRestore, "A setter with unchanged read-back cannot discard its original");
            session.IgnoreMute = false; rig.Mute.Refresh();
            check(!session.Muted && !rig.Mute.HasPendingRestore, "Read-back mismatch is retried until actual release");
        }

        private static void RestartWhilePending(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session session = rig.Add("game", "game", false);
            rig.Mute.BeginAttack(); session.FailMute = true; rig.Mute.Restore();
            rig.Mute.BeginAttack(); // Failure persists into a second attack.
            session.FailMute = false; rig.Mute.Restore();
            check(!session.Muted && !rig.Mute.HasPendingRestore,
                "A new attack during failed release cannot recapture its own temporary mute as the original");

            Rig changed = new Rig();
            Session blocked = changed.Add("blocked", "blocked", false);
            Session own = changed.Add("own", "own", true); own.Own = true; own.Level = 0;
            changed.Mute.BeginAttack(); blocked.FailMute = true; own.FailVolume = true;
            changed.Mute.Restore();
            changed.Endpoint.Muted = true; own.Muted = false;
            changed.Mute.BeginAttack();
            blocked.FailMute = false; own.FailVolume = false; changed.Mute.Restore();
            check(changed.Endpoint.Muted && !own.Muted && own.Level == 0 && !blocked.Muted && !changed.Mute.HasPendingRestore,
                "A new attack rebases released properties changed by the user while retaining unfinished originals");
        }

        private static void DisposalFailure(Action<bool, string> check)
        {
            Rig rig = new Rig();
            Session first = rig.Add("one", "one", false);
            Session second = rig.Add("two", "two", false);
            rig.Mute.BeginAttack(); rig.Source.ThrowOnDispose = true; rig.Mute.Restore();
            check(!first.Muted && !second.Muted && !rig.Mute.HasPendingRestore && rig.Source.LiveHandles == 0,
                "One handle disposal failure cannot interrupt other restorations or cleanup");
        }

        private sealed class Rig
        {
            internal readonly Source Source = new Source();
            internal readonly Endpoint Endpoint = new Endpoint("speakers");
            internal readonly AudioMute Mute;
            internal Rig() { Source.Items.Add(Endpoint); Mute = new AudioMute(Source, false); }
            internal Session Add(string id, string identifier, bool muted)
            {
                Session session = new Session(id, identifier, muted); Endpoint.Sessions.Add(session); return session;
            }
        }

        private sealed class Source : IAttackAudioDevices
        {
            internal readonly List<Endpoint> Items = new List<Endpoint>();
            internal bool FailEnumeration, ThrowOnDispose;
            internal int LiveHandles;
            public IList<IAttackAudioDevice> OpenActiveDevices()
            {
                if (FailEnumeration) throw new InvalidOperationException("Fake audio service unavailable.");
                List<IAttackAudioDevice> result = new List<IAttackAudioDevice>();
                foreach (Endpoint endpoint in Items) result.Add(new DeviceHandle(this, endpoint));
                return result;
            }
        }

        private sealed class Endpoint
        {
            internal readonly string Id;
            internal readonly List<Session> Sessions = new List<Session>();
            internal bool Muted, FailSessions, PartialSessions, Disconnected, FailVolume;
            internal float Level = 0.7f;
            internal int Generation;
            internal Endpoint(string id) { Id = id; }
        }

        private sealed class Session
        {
            internal readonly string Id, Identifier;
            internal bool Own, Expired, Muted, Disconnected, FailMute, FailMuteRead, FailVolume, IgnoreMute;
            internal float Level = 0.8f;
            internal int Generation;
            internal Session(string id, string identifier, bool muted) { Id = id; Identifier = identifier; Muted = muted; }
        }

        private abstract class Handle : IDisposable
        {
            protected readonly Source source;
            private bool disposed;
            protected Handle(Source source) { this.source = source; source.LiveHandles++; }
            protected void Check(bool valid)
            {
                if (disposed || !valid) throw new InvalidOperationException("Fake disposed or stale Core Audio handle.");
            }
            public void Dispose()
            {
                if (disposed) throw new InvalidOperationException("Double disposal.");
                disposed = true; source.LiveHandles--;
                if (source.ThrowOnDispose) throw new InvalidOperationException("Fake handle release failure.");
            }
        }

        private sealed class DeviceHandle : Handle, IAttackAudioDevice
        {
            private readonly Endpoint endpoint;
            private readonly int generation;
            internal DeviceHandle(Source source, Endpoint endpoint) : base(source) { this.endpoint = endpoint; generation = endpoint.Generation; }
            private void Valid() { Check(!endpoint.Disconnected && generation == endpoint.Generation); }
            public string Id { get { return endpoint.Id; } }
            public bool IsDefault { get { return endpoint.Id == "speakers"; } }
            public bool Mute { get { Valid(); return endpoint.Muted; } set { Valid(); endpoint.Muted = value; } }
            public float Volume
            {
                get { Valid(); return endpoint.Level; }
                set { Valid(); if (endpoint.FailVolume) throw new InvalidOperationException("Fake endpoint volume failure."); endpoint.Level = value; }
            }
            public IList<IAttackAudioSession> OpenSessions()
            {
                Valid();
                if (endpoint.FailSessions) throw new InvalidOperationException("Fake session enumeration failure.");
                AudioSessionList result = new AudioSessionList();
                result.Complete = !endpoint.PartialSessions;
                foreach (Session session in endpoint.Sessions) result.Add(new SessionHandle(source, session));
                return result;
            }
        }

        private sealed class SessionHandle : Handle, IAttackAudioSession
        {
            private readonly Session session;
            private readonly int generation;
            private readonly bool expired;
            internal SessionHandle(Source source, Session session) : base(source)
            { this.session = session; generation = session.Generation; expired = session.Expired; }
            private void Valid() { Check(!session.Disconnected && generation == session.Generation); }
            public string Id { get { return session.Id; } }
            public string SessionIdentifier { get { return session.Identifier; } }
            public bool IsExpired { get { return expired; } }
            public bool IsOwn { get { return session.Own; } }
            public bool Mute
            {
                get { Valid(); if (session.FailMuteRead) throw new InvalidOperationException("Fake mute getter failure."); return session.Muted; }
                set { Valid(); if (session.FailMute) throw new InvalidOperationException("Fake mute setter failure."); if (!session.IgnoreMute) session.Muted = value; }
            }
            public float Volume
            {
                get { Valid(); return session.Level; }
                set { Valid(); if (session.FailVolume) throw new InvalidOperationException("Fake volume setter failure."); session.Level = value; }
            }
        }
    }
}
