using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bloodfall.Core;
using Bloodfall.Data;
using Bloodfall.Simulation;

namespace Bloodfall.Protocol
{
    public sealed class HelloInfo
    {
        public ushort ProtocolVersion = ProtocolInfo.Version;
        public string ContentHash;
        public string ClientVersion;
        /// <summary>Signed match ticket issued by the backend (online) or "offline:&lt;name&gt;" for local games.</summary>
        public string Ticket;
        public bool Spectator;
    }

    /// <summary>Binary encoding of every game-server message. Shared by client and server.</summary>
    public static class Codec
    {
        // ------------------------------------------------------------------ client -> server

        public static byte[] Hello(HelloInfo h)
        {
            var w = new NetWriter(128);
            w.WriteByte((byte)MsgType.Hello);
            w.WriteUShort(h.ProtocolVersion);
            w.WriteString(h.ContentHash);
            w.WriteString(h.ClientVersion);
            w.WriteString(h.Ticket);
            w.WriteBool(h.Spectator);
            return w.ToArray();
        }

        public static HelloInfo ReadHello(NetReader r) => new HelloInfo
        {
            ProtocolVersion = r.ReadUShort(),
            ContentHash = r.ReadString(128),
            ClientVersion = r.ReadString(64),
            Ticket = r.ReadString(2048),
            Spectator = r.ReadBool(),
        };

        public static byte[] Command(Order o, ContentIndex index)
        {
            var w = new NetWriter(32);
            w.WriteByte((byte)MsgType.Command);
            w.WriteByte((byte)o.Type);
            w.WriteVarUInt((uint)o.UnitId);
            w.WriteVarUInt((uint)o.TargetId);
            w.WritePos(o.Point);
            w.WritePos(o.Point2);
            w.WriteVarUInt((uint)Math.Max(0, o.Slot));
            w.WriteVarUInt((uint)Math.Max(0, o.Slot2));
            // Train and Build name a unit definition, Research an upgrade; everything else names an item.
            w.WriteVarUInt((uint)(NamesUnit(o.Type) ? index.UnitId(o.ItemId) : o.Type == OrderType.Research ? index.UpgradeId(o.ItemId) : index.ItemId(o.ItemId)));
            w.WriteBool(o.Queue);
            int group = Math.Min(Order.MaxGroup - 1, o.Group?.Length ?? 0);
            w.WriteVarUInt((uint)group);
            for (int i = 0; i < group; i++) w.WriteVarUInt((uint)Math.Max(0, o.Group[i]));
            return w.ToArray();
        }

        public static Order ReadCommand(NetReader r, ContentIndex index)
        {
            var o = new Order
            {
                Type = (OrderType)r.ReadByte(),
                UnitId = (int)r.ReadVarUInt(),
                TargetId = (int)r.ReadVarUInt(),
                Point = r.ReadPos(),
                Point2 = r.ReadPos(),
                Slot = (int)r.ReadVarUInt(),
                Slot2 = (int)r.ReadVarUInt(),
            };
            int named = (int)r.ReadVarUInt();
            o.ItemId = NamesUnit(o.Type) ? index.Unit(named) : o.Type == OrderType.Research ? index.Upgrade(named) : index.Item(named);
            o.Queue = r.ReadBool();
            int group = (int)Math.Min(r.ReadVarUInt(), (uint)(Order.MaxGroup - 1));
            if (group > 0)
            {
                o.Group = new int[group];
                for (int i = 0; i < group; i++) o.Group[i] = (int)r.ReadVarUInt();
            }
            if (!Enum.IsDefined(typeof(OrderType), o.Type)) o.Type = OrderType.None;
            if (o.Slot > 200) o.Slot = 0;
            if (o.Slot2 > 200) o.Slot2 = 0;
            return o;
        }

        private static bool NamesUnit(OrderType t) => t == OrderType.Train || t == OrderType.Build;

        public static byte[] Chat(string text, bool teamOnly)
        {
            var w = new NetWriter(64);
            w.WriteByte((byte)MsgType.Chat);
            w.WriteBool(teamOnly);
            w.WriteString(text);
            return w.ToArray();
        }

        public static byte[] LoadProgress(float progress)
        {
            var w = new NetWriter(8);
            w.WriteByte((byte)MsgType.LoadProgress);
            w.WriteByte((byte)Math.Max(0, Math.Min(100, (int)(progress * 100))));
            return w.ToArray();
        }

        public static byte[] PickHero(string heroId)
        {
            var w = new NetWriter(32);
            w.WriteByte((byte)MsgType.PickHero);
            w.WriteString(heroId);
            return w.ToArray();
        }

        public static byte[] Ping(int clientTimeMs)
        {
            var w = new NetWriter(8);
            w.WriteByte((byte)MsgType.Ping);
            w.WriteInt(clientTimeMs);
            return w.ToArray();
        }

        public static byte[] Pong(int clientTimeMs, int serverTick)
        {
            var w = new NetWriter(12);
            w.WriteByte((byte)MsgType.Pong);
            w.WriteInt(clientTimeMs);
            w.WriteInt(serverTick);
            return w.ToArray();
        }

        public static byte[] Leave()
        {
            return new[] { (byte)MsgType.Leave };
        }

        // ------------------------------------------------------------------ server -> client

        public static byte[] Welcome(WelcomeInfo info)
        {
            var w = new NetWriter(64);
            w.WriteByte((byte)MsgType.Welcome);
            w.WriteVarInt(info.PlayerId);
            w.WriteByte((byte)info.Team);
            w.WriteBool(info.Spectator);
            w.WriteString(info.MatchId);
            w.WriteString(info.MapId);
            w.WriteString(info.ModeId);
            w.WriteByte((byte)info.TickRate);
            w.WriteInt(info.ServerTick);
            w.WriteBool(info.Reconnected);
            return w.ToArray();
        }

        public static WelcomeInfo ReadWelcome(NetReader r) => new WelcomeInfo
        {
            PlayerId = r.ReadVarInt(),
            Team = (Team)r.ReadByte(),
            Spectator = r.ReadBool(),
            MatchId = r.ReadString(),
            MapId = r.ReadString(),
            ModeId = r.ReadString(),
            TickRate = r.ReadByte(),
            ServerTick = r.ReadInt(),
            Reconnected = r.ReadBool(),
        };

        public static byte[] Reject(string reason)
        {
            var w = new NetWriter(64);
            w.WriteByte((byte)MsgType.Reject);
            w.WriteString(reason);
            return w.ToArray();
        }

        public static void WritePlayerView(NetWriter w, PlayerView p, ContentIndex index)
        {
            w.WriteVarUInt((uint)p.Id);
            w.WriteString(p.Name);
            w.WriteString(p.AccountId);
            w.WriteByte((byte)p.Team);
            w.WriteByte((byte)p.Slot);
            w.WriteVarUInt((uint)index.UnitId(p.HeroId));
            w.WriteBool(p.HeroLocked);
            w.WriteVarUInt((uint)p.HeroUnitId);
            w.WriteBool(p.IsBot);
            w.WriteByte((byte)p.Connection);
            w.WriteByte((byte)p.Level);
            w.WriteVarUInt((uint)p.Kills); w.WriteVarUInt((uint)p.Deaths); w.WriteVarUInt((uint)p.Assists);
            w.WriteVarUInt((uint)p.LastHits); w.WriteVarUInt((uint)p.Denies);
            w.WriteVarInt(p.NetWorth);
            w.WriteTenths(p.RespawnIn);
            w.WriteVarUInt((uint)Math.Max(0, p.PingMs));
            w.WriteByte((byte)(Math.Max(0, Math.Min(1, p.LoadProgress)) * 100));
            w.WriteVarUInt((uint)Math.Max(0, p.Gpm));
            w.WriteVarUInt((uint)Math.Max(0, p.Xpm));
            w.WriteString(p.RtsFaction);
            w.WriteBool(p.Eliminated);
        }

        public static PlayerView ReadPlayerView(NetReader r, ContentIndex index) => new PlayerView
        {
            Id = (int)r.ReadVarUInt(),
            Name = r.ReadString(),
            AccountId = r.ReadString(),
            Team = (Team)r.ReadByte(),
            Slot = r.ReadByte(),
            HeroId = index.Unit((int)r.ReadVarUInt()),
            HeroLocked = r.ReadBool(),
            HeroUnitId = (int)r.ReadVarUInt(),
            IsBot = r.ReadBool(),
            Connection = (PlayerConnection)r.ReadByte(),
            Level = r.ReadByte(),
            Kills = (int)r.ReadVarUInt(), Deaths = (int)r.ReadVarUInt(), Assists = (int)r.ReadVarUInt(),
            LastHits = (int)r.ReadVarUInt(), Denies = (int)r.ReadVarUInt(),
            NetWorth = r.ReadVarInt(),
            RespawnIn = r.ReadTenths(),
            PingMs = (int)r.ReadVarUInt(),
            LoadProgress = r.ReadByte() / 100f,
            Gpm = r.ReadVarUInt(),
            Xpm = r.ReadVarUInt(),
            RtsFaction = r.ReadString(64),
            Eliminated = r.ReadBool(),
        };

        public static PlayerView MakePlayerView(Match m, Player p, Team viewer)
        {
            bool friendly = viewer == Team.None || viewer == p.Team;
            return new PlayerView
            {
                Id = p.Id,
                Name = p.Name,
                AccountId = p.AccountId,
                Team = p.Team,
                Slot = p.Slot,
                HeroId = p.HeroLocked || friendly || m.Phase != MatchPhase.HeroSelect ? p.HeroId : null,
                HeroLocked = p.HeroLocked,
                HeroUnitId = p.Hero?.Id ?? 0,
                IsBot = p.IsBot,
                Connection = p.Connection,
                Level = p.Hero?.Level ?? 1,
                Kills = p.Kills, Deaths = p.Deaths, Assists = p.Assists, LastHits = p.LastHits, Denies = p.Denies,
                NetWorth = friendly ? m.NetWorth(p) : -1,
                RespawnIn = p.Hero != null && p.Hero.Dead ? Math.Max(0, p.Hero.RespawnAt - m.Time) : 0,
                PingMs = p.PingMs,
                LoadProgress = p.LoadProgress,
                Gpm = p.Gpm(m.MatchSeconds),
                Xpm = p.Xpm(m.MatchSeconds),
                RtsFaction = p.RtsFaction?.Id,
                Eliminated = p.Eliminated,
            };
        }

        public static byte[] MatchState(Match m, Team viewer, ContentIndex index)
        {
            var w = new NetWriter(256);
            w.WriteByte((byte)MsgType.MatchState);
            w.WriteByte((byte)m.Phase);
            w.WriteTenths(Math.Max(0, m.PhaseTimer));
            w.WriteByte((byte)m.Players.Count);
            foreach (var p in m.Players) WritePlayerView(w, MakePlayerView(m, p, viewer), index);
            return w.ToArray();
        }

        public static MatchStateInfo ReadMatchState(NetReader r, ContentIndex index)
        {
            var s = new MatchStateInfo { Phase = (MatchPhase)r.ReadByte(), PhaseTimer = r.ReadTenths() };
            int n = r.ReadByte();
            for (int i = 0; i < n; i++) s.Players.Add(ReadPlayerView(r, index));
            return s;
        }

        // ------------------------------------------------------------------ snapshots

        /// <summary>Should 'u' be sent to a client on 'viewer' team? Enforces fog of war on the server.</summary>
        public static bool IsVisibleForViewer(Match m, Unit u, Team viewer)
        {
            if (u.Removed) return false;
            if (viewer == Team.None) return true; // spectator / replay
            if (u.Team == viewer) return true;
            if (u.Kind == UnitKind.Resource) return true; // RTS veins are part of the map layout
            // MOBA structures are fixed and known (last-seen state rendered client-side); RTS buildings appear
            // anywhere, so the enemy's stay hidden until scouted.
            if (u.IsStructure && !m.IsRts) return true;
            if (u.IsStructure) return u.VisibleTo[(int)viewer];
            if (u.Dead && !u.IsHero) return u.VisibleTo[(int)viewer] || Math.Abs(m.Time - u.DeathTime) < 0.2f;
            return u.VisibleTo[(int)viewer];
        }

        private static EntityFlags FlagsOf(Unit u)
        {
            var f = EntityFlags.None;
            if (u.Dead) f |= EntityFlags.Dead;
            if (u.Invulnerable) f |= EntityFlags.Invulnerable;
            if (u.IsMagicImmune) f |= EntityFlags.MagicImmune;
            if (u.IsInvisible) f |= EntityFlags.Invisible;
            if (u.HasFlag(StatusFlags.Stunned) || u.HasFlag(StatusFlags.Frozen) || u.HasFlag(StatusFlags.Sleeping)) f |= EntityFlags.Stunned;
            if (u.HasFlag(StatusFlags.Silenced)) f |= EntityFlags.Silenced;
            if (u.HasFlag(StatusFlags.Rooted)) f |= EntityFlags.Rooted;
            if (u.HasFlag(StatusFlags.Hexed)) f |= EntityFlags.Hexed;
            if (u.HasFlag(StatusFlags.Disarmed)) f |= EntityFlags.Disarmed;
            if (u.HasFlag(StatusFlags.Feared)) f |= EntityFlags.Feared;
            if (u.IsIllusion) f |= EntityFlags.Illusion;
            if (u.Motion != null && u.Motion.Kind == MotionKind.Leap) f |= EntityFlags.Airborne;
            if (u.IsMoving || u.Motion != null) f |= EntityFlags.Moving;
            if (u.IsHero) f |= EntityFlags.Hero;
            if (u.IsStructure) f |= EntityFlags.Structure;
            if (u.Action == ActionState.Channeling) f |= EntityFlags.Channeling;
            return f;
        }

        public static byte[] Snapshot(Match m, Team viewer, Player viewerPlayer, ContentIndex index, NetWriter w = null)
        {
            w = w ?? new NetWriter(4096);
            w.Reset();
            w.WriteByte((byte)MsgType.Snapshot);
            w.WriteInt(m.Tick);
            w.WriteFloat(m.Time);
            w.WriteByte((byte)m.Phase);
            w.WriteTenths(Math.Max(0, m.PhaseTimer));
            w.WriteBool(m.IsNight);
            w.WriteTenths(Math.Max(0, m.DayNightTimer));
            w.WriteVarUInt((uint)m.TeamKills[0]);
            w.WriteVarUInt((uint)m.TeamKills[1]);
            w.WriteByte((byte)m.VharothState);
            w.WriteByte((byte)m.VharothSealsBroken);

            // Entities.
            int countPos = w.Length;
            w.WriteUShort(0);
            int count = 0;
            foreach (var u in m.Units)
            {
                if (!IsVisibleForViewer(m, u, viewer)) continue;
                count++;
                WriteEntity(w, m, u, viewer, index);
            }
            w.Buffer[countPos] = (byte)count;
            w.Buffer[countPos + 1] = (byte)(count >> 8);

            // Players.
            w.WriteByte((byte)m.Players.Count);
            foreach (var p in m.Players) WritePlayerView(w, MakePlayerView(m, p, viewer), index);

            // Private state.
            var hero = viewerPlayer?.Hero;
            w.WriteBool(hero != null);
            if (hero != null) WritePrivate(w, m, viewerPlayer, hero, index);
            bool rts = viewerPlayer != null && m.IsRts;
            w.WriteBool(rts);
            if (rts)
            {
                w.WriteVarUInt((uint)Math.Max(0, viewerPlayer.Gold));
                w.WriteVarUInt((uint)Math.Max(0, viewerPlayer.Lumber));
                w.WriteVarUInt((uint)Math.Max(0, viewerPlayer.SupplyUsed));
                w.WriteVarUInt((uint)Math.Max(0, viewerPlayer.SupplyCap));
                int ups = Math.Min(64, viewerPlayer.Upgrades.Count);
                w.WriteByte((byte)ups);
                int written = 0;
                foreach (var id in viewerPlayer.Upgrades)
                {
                    if (written++ >= ups) break;
                    w.WriteVarUInt((uint)index.UpgradeId(id));
                }
                // v7: the player's heroes. Ability ids, levels and cooldowns travel in each hero's entity.
                var heroes = viewerPlayer.RtsHeroes;
                int heroCount = Math.Min(8, heroes.Count);
                w.WriteByte((byte)heroCount);
                var table = m.Rules.Experience.Cumulative;
                for (int i = 0; i < heroCount; i++)
                {
                    var h = heroes[i];
                    w.WriteVarUInt((uint)h.Id);
                    w.WriteVarUInt((uint)index.UnitId(h.DefId));
                    w.WriteByte((byte)Math.Min(255, h.Level));
                    w.WriteBool(h.Dead);
                    w.WriteVarUInt((uint)Math.Max(0, h.Xp));
                    w.WriteVarUInt((uint)table[Math.Min(table.Length - 1, h.Level - 1)]);
                    w.WriteVarUInt((uint)m.XpForNextLevel(h));
                    w.WriteByte((byte)Math.Min(255, h.AbilityPoints));
                    int mask = 0;
                    for (int a = 0; a < Math.Min(31, h.Abilities.Count); a++)
                        if (m.CanLevelAbility(h, h.Abilities[a])) mask |= 1 << a;
                    w.WriteVarUInt((uint)mask);
                }
            }
            return w.ToArray();
        }

        private static void WriteEntity(NetWriter w, Match m, Unit u, Team viewer, ContentIndex index)
        {
            w.WriteVarUInt((uint)u.Id);
            w.WriteVarUInt((uint)index.UnitId(u.DefId));
            w.WriteByte((byte)u.Kind);
            w.WriteByte((byte)u.Team);
            var flags = FlagsOf(u);
            w.WriteUShort((ushort)flags);
            w.WritePos(u.Position);
            w.WriteAngle(u.Facing);
            float height = 0f;
            if (u.Motion != null && u.Motion.Height > 0)
            {
                float t = MathUtil.Clamp01(u.Motion.Elapsed / u.Motion.Duration);
                height = u.Motion.Height * 4f * t * (1f - t);
            }
            w.WriteByte((byte)Math.Min(255, (int)(height * 10)));
            w.WriteVarUInt((uint)Math.Max(0, Math.Ceiling(u.Hp)));
            w.WriteVarUInt((uint)Math.Max(1, Math.Round(u.Stats.MaxHp)));
            w.WriteVarUInt((uint)Math.Max(0, Math.Round(u.Mana)));
            w.WriteVarUInt((uint)Math.Max(0, Math.Round(u.Stats.MaxMana)));
            w.WriteByte((byte)Math.Min(255, u.Level));
            w.WriteByte((byte)u.Action);
            w.WriteVarUInt((uint)Math.Max(0, m.Tick - u.ActionStartTick));
            var ab = u.Action == ActionState.CastWindup || u.Action == ActionState.Channeling || u.Action == ActionState.CastBackswing ? u.GetAbility(u.ActionAbility) : null;
            w.WriteVarUInt((uint)index.AbilityId(ab?.Def.Id));
            w.WriteVarUInt((uint)(u.Action == ActionState.AttackWindup || u.Action == ActionState.AttackBackswing ? u.AttackTargetId : u.ActionTargetId));
            w.WriteByte((byte)Math.Min(255, (int)(u.Stats.AttackPoint * 100)));
            w.WriteByte((byte)Math.Min(255, (int)(u.Stats.AttackTime * 50)));
            w.WriteByte((byte)Math.Min(255, (int)(u.Stats.MoveSpeed * 20)));
            w.WriteSByte((sbyte)(u.Owner?.Id ?? (u.Summoner?.Owner?.Id ?? -1)));
            w.WriteVarInt((int)Math.Round(u.Stats.Armor * 10));
            w.WriteVarUInt((uint)Math.Max(0, Math.Round(u.Stats.AverageDamage)));
            string modelOverride = null;
            for (int i = u.Statuses.Count - 1; i >= 0; i--) if (!string.IsNullOrEmpty(u.Statuses[i].Def.ModelOverride)) { modelOverride = u.Statuses[i].Def.ModelOverride; break; }
            w.WriteString(modelOverride);

            // Visible statuses.
            int n = 0;
            foreach (var s in u.Statuses) if (!s.Def.Hidden) n++;
            n = Math.Min(n, 24);
            w.WriteByte((byte)n);
            int written = 0;
            foreach (var s in u.Statuses)
            {
                if (s.Def.Hidden || written >= n) continue;
                written++;
                w.WriteVarUInt((uint)index.StatusId(s.Def.Id));
                w.WriteTenths(s.Permanent ? 0 : s.Remaining);
                w.WriteTenths(s.Permanent ? 0 : s.Duration);
                w.WriteByte((byte)Math.Min(255, s.Stacks));
            }

            // RTS: construction, training (owner's team only), carried cargo, vein contents.
            if (u.Kind == UnitKind.Building)
            {
                bool friendly = viewer == Team.None || viewer == u.Team;
                w.WriteBool(u.UnderConstruction);
                w.WriteByte((byte)Math.Round(MathUtil.Clamp01(u.BuildProgress) * 255));
                int queued = friendly ? Math.Min(8, u.TrainQueue?.Count ?? 0) : 0;
                w.WriteByte((byte)queued);
                // Each entry is a unit or a research: index << 1, low bit set for research.
                for (int i = 0; i < queued; i++)
                {
                    int up = index.UpgradeId(u.TrainQueue[i]);
                    w.WriteVarUInt(up > 0 ? (uint)(up << 1 | 1) : (uint)(index.UnitId(u.TrainQueue[i]) << 1));
                }
                if (queued > 0) w.WriteByte((byte)Math.Round(MathUtil.Clamp01(u.TrainProgress) * 255));
                bool rally = friendly && u.HasRally;
                w.WriteBool(rally);
                if (rally) w.WritePos(u.RallyPoint);
            }
            else if (u.Kind == UnitKind.Worker)
            {
                w.WriteByte((byte)Math.Min(255, u.CarryGold));
                w.WriteByte((byte)Math.Min(255, u.CarryLumber));
            }
            else if (u.Kind == UnitKind.Resource) w.WriteVarUInt((uint)Math.Max(0, u.ResourceAmount));

            // Heroes: ability levels (cooldowns only for friendly viewers) + items.
            if (u.IsHero)
            {
                bool friendly = viewer == Team.None || viewer == u.Team;
                w.WriteByte((byte)u.Abilities.Count);
                foreach (var a in u.Abilities)
                {
                    w.WriteVarUInt((uint)index.AbilityId(a.Def.Id));
                    w.WriteByte((byte)a.Level);
                    w.WriteTenths(friendly ? a.Cooldown : 0);
                    w.WriteTenths(friendly ? a.CooldownTotal : 0);
                }
                int items = u.Inventory?.Length ?? 0;
                w.WriteByte((byte)items);
                for (int i = 0; i < items; i++) w.WriteVarUInt((uint)index.ItemId(u.Inventory[i]?.Def.Id));
            }
        }

        private static void WritePrivate(NetWriter w, Match m, Player p, Unit hero, ContentIndex index)
        {
            w.WriteVarUInt((uint)Math.Max(0, p.Gold));
            w.WriteVarUInt((uint)hero.Xp);
            var table = m.Rules.Experience.Cumulative;
            w.WriteVarUInt((uint)table[Math.Min(table.Length - 1, hero.Level - 1)]);
            w.WriteVarUInt((uint)m.XpForNextLevel(hero));
            w.WriteByte((byte)hero.AbilityPoints);
            w.WriteVarUInt((uint)m.BuybackCost(p));
            w.WriteTenths(Math.Max(0, p.BuybackCooldownUntil - m.Time));
            w.WriteBool(m.AtBase(hero));
            w.WriteBool(m.InShopRange(hero, ItemShop.Secret));
            w.WriteByte((byte)hero.Abilities.Count);
            foreach (var a in hero.Abilities)
            {
                w.WriteVarUInt((uint)index.AbilityId(a.Def.Id));
                w.WriteByte((byte)a.Level);
                w.WriteTenths(a.Cooldown);
                w.WriteTenths(a.CooldownTotal);
                w.WriteByte((byte)Math.Max(0, a.Charges));
                w.WriteBool(a.ToggledOn);
                w.WriteBool(m.CanLevelAbility(hero, a));
                w.WriteVarUInt((uint)Math.Round(a.Level > 0 ? m.ManaCostOf(hero, a) : a.Def.ManaCost.Get(1)));
                w.WriteVarUInt((uint)Math.Round(a.Level > 0 ? m.HealthCostOf(hero, a) : a.Def.HealthCost.Get(1)));
            }
            // 16 fixed slots: 0-5 inventory, 6-8 backpack, 10-15 stash.
            for (int slot = 0; slot < 16; slot++)
            {
                var it = m.GetItemAt(hero, slot);
                w.WriteVarUInt((uint)index.ItemId(it?.Def.Id));
                if (it == null) continue;
                w.WriteVarUInt((uint)Math.Max(0, it.Charges));
                w.WriteTenths(it.Active?.Cooldown ?? 0);
                w.WriteTenths(it.Active?.CooldownTotal ?? 0);
                w.WriteVarUInt((uint)m.SellValue(it));
            }
            var courier = p.Courier;
            w.WriteVarUInt((uint)(courier?.Id ?? 0));
            if (courier != null)
            {
                w.WriteByte((byte)courier.CourierState);
                w.WriteTenths(courier.Dead ? Math.Max(0f, courier.RespawnAt - m.Time) : 0f);
                w.WriteByte((byte)Math.Min(255, courier.Carried.Count));
            }
        }

        public static SnapshotFrame ReadSnapshot(NetReader r, ContentIndex index)
        {
            var f = new SnapshotFrame
            {
                Tick = r.ReadInt(),
                Time = r.ReadFloat(),
                Phase = (MatchPhase)r.ReadByte(),
                PhaseTimer = r.ReadTenths(),
                IsNight = r.ReadBool(),
                DayNightRemaining = r.ReadTenths(),
            };
            f.TeamKills[0] = (int)r.ReadVarUInt();
            f.TeamKills[1] = (int)r.ReadVarUInt();
            f.VharothPhase = r.ReadByte();
            f.VharothSeals = r.ReadByte();
            int count = r.ReadUShort();
            for (int i = 0; i < count; i++)
            {
                var e = ReadEntity(r, index);
                e.ActionStartTick = f.Tick + e.ActionStartTick; // wire value is the negative age in ticks
                f.Entities.Add(e);
            }
            int players = r.ReadByte();
            for (int i = 0; i < players; i++) f.Players.Add(ReadPlayerView(r, index));
            if (r.ReadBool()) f.Me = ReadPrivate(r, index);
            if (r.ReadBool())
            {
                f.Rts = new RtsPrivateState { Gold = (int)r.ReadVarUInt(), Lumber = (int)r.ReadVarUInt(), SupplyUsed = (int)r.ReadVarUInt(), SupplyCap = (int)r.ReadVarUInt() };
                int ups = r.ReadByte();
                for (int i = 0; i < ups; i++)
                {
                    var id = index.Upgrade((int)r.ReadVarUInt());
                    if (id != null) f.Rts.Upgrades.Add(id);
                }
                int heroes = r.ReadByte();
                for (int i = 0; i < heroes; i++)
                    f.Rts.Heroes.Add(new RtsHeroState
                    {
                        UnitId = (int)r.ReadVarUInt(),
                        HeroId = index.Unit((int)r.ReadVarUInt()),
                        Level = r.ReadByte(),
                        Dead = r.ReadBool(),
                        Xp = (int)r.ReadVarUInt(),
                        XpLevelStart = (int)r.ReadVarUInt(),
                        XpNextLevel = (int)r.ReadVarUInt(),
                        AbilityPoints = r.ReadByte(),
                        CanLevelMask = (int)r.ReadVarUInt(),
                    });
            }
            return f;
        }

        private static EntityState ReadEntity(NetReader r, ContentIndex index)
        {
            var e = new EntityState
            {
                Id = (int)r.ReadVarUInt(),
                DefId = index.Unit((int)r.ReadVarUInt()),
                Kind = (UnitKind)r.ReadByte(),
                Team = (Team)r.ReadByte(),
                Flags = (EntityFlags)r.ReadUShort(),
                Position = r.ReadPos(),
                Facing = r.ReadAngle(),
                Height = r.ReadByte() / 10f,
                Hp = r.ReadVarUInt(),
                MaxHp = r.ReadVarUInt(),
                Mana = r.ReadVarUInt(),
                MaxMana = r.ReadVarUInt(),
                Level = r.ReadByte(),
                Action = (ActionState)r.ReadByte(),
            };
            e.ActionStartTick = -(int)r.ReadVarUInt(); // relative; fixed up by caller (tick - age)
            e.ActionAbilityId = index.Ability((int)r.ReadVarUInt());
            e.ActionTargetId = (int)r.ReadVarUInt();
            e.AttackPoint = r.ReadByte() / 100f;
            e.AttackTime = r.ReadByte() / 50f;
            e.MoveSpeed = r.ReadByte() / 20f;
            e.OwnerPlayer = r.ReadSByte();
            e.Armor = r.ReadVarInt() / 10f;
            e.Damage = r.ReadVarUInt();
            e.ModelKey = r.ReadString();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
                e.Statuses.Add(new StatusView { Id = index.Status((int)r.ReadVarUInt()), Remaining = r.ReadTenths(), Duration = r.ReadTenths(), Stacks = r.ReadByte() });
            if (e.Kind == UnitKind.Building)
            {
                e.UnderConstruction = r.ReadBool();
                e.BuildProgress = r.ReadByte() / 255f;
                int queued = r.ReadByte();
                if (queued > 0)
                {
                    e.TrainQueue = new string[queued];
                    for (int i = 0; i < queued; i++)
                    {
                        uint v = r.ReadVarUInt();
                        e.TrainQueue[i] = (v & 1) != 0 ? index.Upgrade((int)(v >> 1)) : index.Unit((int)(v >> 1));
                    }
                    e.TrainProgress = r.ReadByte() / 255f;
                }
                if (r.ReadBool()) e.Rally = r.ReadPos();
            }
            else if (e.Kind == UnitKind.Worker)
            {
                e.CarryGold = r.ReadByte();
                e.CarryLumber = r.ReadByte();
            }
            else if (e.Kind == UnitKind.Resource) e.ResourceAmount = (int)r.ReadVarUInt();
            if ((e.Flags & EntityFlags.Hero) != 0)
            {
                int ac = r.ReadByte();
                e.HeroAbilities = new List<AbilityView>(ac);
                for (int i = 0; i < ac; i++)
                    e.HeroAbilities.Add(new AbilityView { Id = index.Ability((int)r.ReadVarUInt()), Level = r.ReadByte(), Cooldown = r.ReadTenths(), CooldownTotal = r.ReadTenths() });
                int ic = r.ReadByte();
                e.HeroItems = new string[ic];
                for (int i = 0; i < ic; i++) e.HeroItems[i] = index.Item((int)r.ReadVarUInt());
            }
            return e;
        }

        private static PrivateState ReadPrivate(NetReader r, ContentIndex index)
        {
            var p = new PrivateState
            {
                Gold = (int)r.ReadVarUInt(),
                Xp = (int)r.ReadVarUInt(),
                XpLevelStart = (int)r.ReadVarUInt(),
                XpNextLevel = (int)r.ReadVarUInt(),
                AbilityPoints = r.ReadByte(),
                BuybackCost = (int)r.ReadVarUInt(),
                BuybackCooldown = r.ReadTenths(),
                AtBase = r.ReadBool(),
                NearSecretShop = r.ReadBool(),
            };
            int n = r.ReadByte();
            p.Abilities = new AbilityView[n];
            for (int i = 0; i < n; i++)
            {
                p.Abilities[i] = new AbilityView
                {
                    Id = index.Ability((int)r.ReadVarUInt()),
                    Level = r.ReadByte(),
                    Cooldown = r.ReadTenths(),
                    CooldownTotal = r.ReadTenths(),
                    Charges = r.ReadByte(),
                    Toggled = r.ReadBool(),
                    CanLevel = r.ReadBool(),
                    ManaCost = r.ReadVarUInt(),
                    HealthCost = r.ReadVarUInt(),
                };
                p.Abilities[i].Ready = p.Abilities[i].Level > 0 && p.Abilities[i].Cooldown <= 0;
            }
            for (int slot = 0; slot < 16; slot++)
            {
                var id = index.Item((int)r.ReadVarUInt());
                if (id == null) { p.Items[slot] = default; continue; }
                p.Items[slot] = new ItemView { Id = id, Charges = (int)r.ReadVarUInt(), Cooldown = r.ReadTenths(), CooldownTotal = r.ReadTenths(), SellValue = (int)r.ReadVarUInt() };
            }
            p.CourierId = (int)r.ReadVarUInt();
            if (p.CourierId != 0)
            {
                p.CourierState = r.ReadByte();
                p.CourierRespawnIn = r.ReadTenths();
                p.CourierCarried = r.ReadByte();
            }
            return p;
        }

        // ------------------------------------------------------------------ events

        /// <summary>Is this simulation event something the viewer is allowed to know about?</summary>
        public static bool EventVisible(Match m, SimEvent e, Team viewer, int viewerPlayer)
        {
            if (e.PlayerId >= 0) return e.PlayerId == viewerPlayer;
            if (viewer == Team.None) return true;
            switch (e.Type)
            {
                case SimEventType.Announcer:
                case SimEventType.KillFeed:
                case SimEventType.MatchPhase:
                case SimEventType.DayNight:
                case SimEventType.StructureDestroyed:
                case SimEventType.WaveSpawned:
                case SimEventType.VharothEvent:
                case SimEventType.PlayerConnection:
                case SimEventType.Buyback:
                case SimEventType.PlayerEliminated:
                    return true;
                case SimEventType.Ping:
                    return e.Team == viewer;
                case SimEventType.Respawn:
                    return true;
            }
            var u = m.GetUnit(e.UnitId) ?? (m.UnitById.TryGetValue(e.UnitId, out var dead) ? dead : null);
            if (u != null && (u.Team == viewer || u.VisibleTo[(int)viewer])) return true;
            var o = e.OtherId != 0 && m.UnitById.TryGetValue(e.OtherId, out var other) ? other : null;
            if (o != null && o.Team == viewer) return true;
            if (e.Point != Vector2.Zero && m.Vision.IsVisible(viewer, e.Point)) return true;
            return false;
        }

        public static byte[] Events(IList<SimEvent> events, Match m, Team viewer, int viewerPlayer)
        {
            var w = new NetWriter(256);
            w.WriteByte((byte)MsgType.Events);
            int countPos = w.Length;
            w.WriteUShort(0);
            int count = 0;
            foreach (var e in events)
            {
                if (!EventVisible(m, e, viewer, viewerPlayer)) continue;
                count++;
                w.WriteByte((byte)e.Type);
                w.WriteInt(e.Tick);
                w.WriteVarUInt((uint)e.UnitId);
                w.WriteVarUInt((uint)e.OtherId);
                w.WriteString(e.Key);
                w.WriteFloat(e.Value);
                w.WriteFloat(e.Value2);
                w.WritePos(e.Point);
                w.WritePos(e.Point2);
                w.WriteByte(e.Flags);
                w.WriteByte((byte)e.Team);
                if (count == 65535) break;
            }
            if (count == 0) return null;
            w.Buffer[countPos] = (byte)count;
            w.Buffer[countPos + 1] = (byte)(count >> 8);
            return w.ToArray();
        }

        public static List<NetEvent> ReadEvents(NetReader r)
        {
            int n = r.ReadUShort();
            var list = new List<NetEvent>(n);
            for (int i = 0; i < n; i++)
            {
                list.Add(new NetEvent
                {
                    Type = (SimEventType)r.ReadByte(),
                    Tick = r.ReadInt(),
                    UnitId = (int)r.ReadVarUInt(),
                    OtherId = (int)r.ReadVarUInt(),
                    Key = r.ReadString(),
                    Value = r.ReadFloat(),
                    Value2 = r.ReadFloat(),
                    Point = r.ReadPos(),
                    Point2 = r.ReadPos(),
                    Flags = r.ReadByte(),
                    Team = (Team)r.ReadByte(),
                });
            }
            return list;
        }

        public static byte[] ChatBroadcast(ChatMessage c)
        {
            var w = new NetWriter(64);
            w.WriteByte((byte)MsgType.ChatBroadcast);
            w.WriteVarInt(c.PlayerId);
            w.WriteString(c.Name);
            w.WriteBool(c.TeamOnly);
            w.WriteByte((byte)c.Team);
            w.WriteString(c.Text);
            return w.ToArray();
        }

        public static ChatMessage ReadChatBroadcast(NetReader r) => new ChatMessage
        {
            PlayerId = r.ReadVarInt(), Name = r.ReadString(), TeamOnly = r.ReadBool(), Team = (Team)r.ReadByte(), Text = r.ReadString(512),
        };

        public static byte[] MatchEnd(MatchResult result)
        {
            var w = new NetWriter(1024);
            w.WriteByte((byte)MsgType.MatchEnd);
            w.WriteString(JsonMapper.ToJson(result));
            return w.ToArray();
        }

        public static MatchResult ReadMatchEnd(NetReader r) => JsonMapper.FromJson<MatchResult>(r.ReadString(1 << 20));
    }
}
