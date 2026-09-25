using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Bloodfall.Data;
using Bloodfall.Protocol;
using Bloodfall.Simulation;
using Xunit;

namespace Bloodfall.Tests
{
    public class ProtocolTests
    {
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("test-signing-key-0123456789abcdef");

        [Fact]
        public void NetBuffer_RoundTrip()
        {
            var w = new NetWriter(4);
            w.WriteVarUInt(300); w.WriteVarInt(-12345); w.WriteString("Blood Tyrant ✝"); w.WritePos(new Vector2(12.34f, 191.5f)); w.WriteFloat(3.25f); w.WriteAngle(1.0f);
            var r = new NetReader(w.ToArray());
            Assert.Equal(300u, r.ReadVarUInt());
            Assert.Equal(-12345, r.ReadVarInt());
            Assert.Equal("Blood Tyrant ✝", r.ReadString());
            var p = r.ReadPos();
            Assert.InRange(p.X, 12.33f, 12.35f);
            Assert.InRange(p.Y, 191.49f, 191.51f);
            Assert.Equal(3.25f, r.ReadFloat());
            Assert.InRange(r.ReadAngle(), 0.98f, 1.02f);
            Assert.True(r.AtEnd);
        }

        [Fact]
        public void Tickets_SignAndValidate()
        {
            var t = MatchTickets.Issue(new TicketPayload { MatchId = "m1", AccountId = "a1", Team = "Dawn", Exp = MatchTickets.UnixNow() + 60 }, Key);
            var ok = MatchTickets.Validate(t, Key, MatchTickets.UnixNow(), out var err);
            Assert.NotNull(ok);
            Assert.Equal("a1", ok.AccountId);
            Assert.Null(MatchTickets.Validate(t.Substring(0, t.Length - 2) + "xx", Key, MatchTickets.UnixNow(), out err));
            Assert.Null(MatchTickets.Validate(t, Encoding.UTF8.GetBytes("other-key-other-key-other-key-xx"), MatchTickets.UnixNow(), out err));
            Assert.Null(MatchTickets.Validate(t, Key, MatchTickets.UnixNow() + 120, out err));
            Assert.Equal("Ticket expired.", err);
        }

        [Fact]
        public void Offline_Loopback_FullFlow_HeroSelect_Snapshots_Commands()
        {
            var data = TestUtil.Data;
            var cfg = new MatchConfig { Seed = 5, PreGameTimeOverride = 2f, HeroSelectTimeOverride = 20f };
            cfg.Players.Add(new PlayerSetup { AccountId = OfflineSession.LocalAccountId, Name = "Me", Team = Team.Dawn, Slot = 0 });
            cfg.Players.Add(new PlayerSetup { Name = "Bot", Team = Team.Dusk, Slot = 0, IsBot = true });
            var host = OfflineSession.CreateHost(data, cfg);
            var link = new LoopbackConnection(host);
            var client = new GameClient(data, link, new HelloInfo { Ticket = MatchTickets.OfflinePrefix + "Me", ClientVersion = "test" });
            client.Connect();
            Pump(host, client, 0.2f);
            Assert.Equal(ClientConnectionState.Connected, client.State);
            Assert.Equal(0, client.LocalPlayerId);
            Assert.Equal(MatchPhase.HeroSelect, client.MatchState.Phase);

            client.PickHero("hero_vorak");
            client.SendLoadProgress(1f);
            Pump(host, client, 1f);
            Assert.True(host.Match.Phase == MatchPhase.PreGame || host.Match.Phase == MatchPhase.Playing);
            Assert.NotNull(client.Latest);
            var me = client.Latest.Entities.FirstOrDefault(e => e.DefId == "hero_vorak" && e.Team == Team.Dawn);
            Assert.NotNull(me);
            Assert.NotNull(client.Latest.Me);
            Assert.Equal(600, client.Latest.Me.Gold);
            // Fog: the enemy hero at its fountain is not visible to us.
            Assert.DoesNotContain(client.Latest.Entities, e => e.Kind == UnitKind.Hero && e.Team == Team.Dusk);

            // Move command round trip.
            var start = me.Position;
            client.SendOrder(Order.MoveTo(me.Id, start + new Vector2(8, 8)));
            Pump(host, client, 1.5f);
            var moved = client.Latest.Entities.First(e => e.Id == me.Id);
            Assert.True(Vector2.Distance(moved.Position, start) > 3f);

            // Buy an item through the network.
            client.SendOrder(Order.Buy(me.Id, "item_rusted_blade"));
            Pump(host, client, 0.3f);
            Assert.Equal("item_rusted_blade", client.Latest.Me.Items[0].Id);
            Assert.InRange(client.Latest.Me.Gold, 300, 310); // passive income may tick meanwhile
        }

        [Fact]
        public void Commands_CarryUnitIdsForTrainAndBuild_AndGroups()
        {
            var index = new ContentIndex(TestUtil.Data);
            var train = new Order { Type = OrderType.Train, UnitId = 7, ItemId = "rts_dg_squire" };
            var back = Codec.ReadCommand(Skip1(Codec.Command(train, index)), index);
            Assert.Equal("rts_dg_squire", back.ItemId);
            var buy = Codec.ReadCommand(Skip1(Codec.Command(Order.Buy(3, "item_rusted_blade"), index)), index);
            Assert.Equal("item_rusted_blade", buy.ItemId);
            var group = new Order { Type = OrderType.AttackMove, UnitId = 1, Point = new Vector2(10, 20), Group = Enumerable.Range(2, 100).ToArray() };
            var g = Codec.ReadCommand(Skip1(Codec.Command(group, index)), index);
            Assert.Equal(Order.MaxGroup - 1, g.Group.Length); // capped
            Assert.Equal(2, g.Group[0]);
        }

        private static NetReader Skip1(byte[] msg) { var r = new NetReader(msg); r.ReadByte(); return r; }

        [Fact]
        public void Rts_Loopback_StreamsEconomy_BuildingState_AndFog()
        {
            var data = TestUtil.Data;
            var cfg = new MatchConfig { ModeId = "rts_1v1", MapId = "map_rts_ashfields", Seed = 5, PreGameTimeOverride = 0.5f };
            cfg.Players.Add(new PlayerSetup { AccountId = OfflineSession.LocalAccountId, Name = "Me", Team = Team.Dawn, RtsFaction = "dawnguard" });
            cfg.Players.Add(new PlayerSetup { Name = "Bot", Team = Team.Dusk, IsBot = true, RtsFaction = "ashen_legion" });
            var host = OfflineSession.CreateHost(data, cfg);
            var link = new LoopbackConnection(host);
            var client = new GameClient(data, link, new HelloInfo { Ticket = MatchTickets.OfflinePrefix + "Me", ClientVersion = "test" });
            client.Connect();
            Pump(host, client, 0.2f);
            Assert.Equal(MatchPhase.Loading, client.MatchState.Phase); // no hero draft in the RTS
            client.SendLoadProgress(1f);
            Pump(host, client, 1.5f);
            var f = client.Latest;
            Assert.NotNull(f.Rts);
            Assert.Null(f.Me); // no hero
            Assert.Equal(10, f.Rts.SupplyCap);
            var hall = f.Entities.Single(e => e.DefId == "rts_dg_citadel");
            Assert.False(hall.UnderConstruction);
            Assert.Equal(1f, hall.BuildProgress, 2);
            Assert.Contains(f.Entities, e => e.Kind == UnitKind.Resource && e.ResourceAmount > 0);
            Assert.DoesNotContain(f.Entities, e => e.DefId == "rts_al_necropolis"); // enemy base unscouted
            Assert.Equal("dawnguard", f.Players.Single(p => p.Id == client.LocalPlayerId).RtsFaction);

            // Train over the network; the queue is visible to its owner.
            var workers = f.Entities.Where(e => e.Kind == UnitKind.Worker && e.OwnerPlayer == client.LocalPlayerId).ToList();
            client.SendOrder(new Order { Type = OrderType.Train, UnitId = hall.Id, ItemId = "rts_dg_squire" });
            client.SendOrder(new Order { Type = OrderType.SetRally, UnitId = hall.Id, Point = hall.Position + new Vector2(6, 6) });
            client.SendOrder(new Order { Type = OrderType.Move, UnitId = workers[0].Id, Point = hall.Position + new Vector2(8, 8), Group = workers.Skip(1).Select(w => w.Id).ToArray() });
            Pump(host, client, 0.5f);
            var h = client.Latest.Entities.Single(e => e.Id == hall.Id);
            Assert.Equal(new[] { "rts_dg_squire" }, h.TrainQueue);
            Assert.True(h.Rally.HasValue);
            Assert.Equal(425, client.Latest.Rts.Gold);
            Assert.All(workers, w => Assert.NotEqual(OrderType.None, host.Match.GetUnit(w.Id).CurrentOrder.Type));
        }

        [Fact]
        public void Reconnect_RestoresPlayer_AndGraceAbandons()
        {
            var data = TestUtil.Data;
            var cfg = new MatchConfig { Seed = 5, SkipHeroSelect = true, PreGameTimeOverride = 1f };
            cfg.Players.Add(new PlayerSetup { AccountId = OfflineSession.LocalAccountId, Name = "Me", Team = Team.Dawn, Slot = 0, HeroId = "hero_ilyra" });
            cfg.Players.Add(new PlayerSetup { Name = "Bot", Team = Team.Dusk, Slot = 0, IsBot = true, HeroId = "hero_vorak" });
            var host = OfflineSession.CreateHost(data, cfg);
            var link = new LoopbackConnection(host);
            var client = new GameClient(data, link, new HelloInfo { Ticket = MatchTickets.OfflinePrefix + "Me" });
            client.Connect();
            Pump(host, client, 2f);
            host.Match.Players[0].Gold = 1234;
            link.Disconnect();
            Pump(host, client, 1f);
            Assert.Equal(PlayerConnection.Disconnected, host.Match.Players[0].Connection);

            var link2 = new LoopbackConnection(host);
            var client2 = new GameClient(data, link2, new HelloInfo { Ticket = MatchTickets.OfflinePrefix + "Me" });
            client2.Connect();
            Pump(host, client2, 0.5f);
            Assert.True(client2.Welcome.Reconnected);
            Assert.Equal(PlayerConnection.Connected, host.Match.Players[0].Connection);
            Assert.True(client2.Latest.Me.Gold >= 1234);

            // Grace expiry -> abandoned, hero handed to a bot.
            link2.Disconnect();
            host.Match.Players[0].DisconnectedAt = host.Match.Time - host.Match.Rules.ReconnectGraceSeconds - 1;
            Pump(host, client2, 0.2f);
            Assert.Equal(PlayerConnection.Abandoned, host.Match.Players[0].Connection);
            Assert.NotNull(host.Match.Players[0].Hero.Brain);
        }

        [Fact]
        public void Host_RejectsWrongContentHash_AndUnknownPlayers()
        {
            var data = TestUtil.Data;
            var cfg = new MatchConfig { SkipHeroSelect = true };
            cfg.Players.Add(new PlayerSetup { AccountId = "acc-1", Name = "A", Team = Team.Dawn, HeroId = "hero_vorak" });
            var match = new Match(data, cfg);
            var host = new MatchHost(match) { TicketValidator = t => MatchTickets.Validate(t, Key, MatchTickets.UnixNow(), out var e) is TicketPayload p ? (p, null) : (null, e) };

            var bad = new LoopbackConnection(host);
            var c1 = new GameClient(data, bad, new HelloInfo { Ticket = "garbage" });
            c1.Connect();
            Pump(host, c1, 0.1f);
            Assert.Equal(ClientConnectionState.Rejected, c1.State);

            var stranger = MatchTickets.Issue(new TicketPayload { MatchId = cfg.MatchId, AccountId = "acc-2", Exp = MatchTickets.UnixNow() + 60 }, Key);
            var c2 = new GameClient(data, new LoopbackConnection(host), new HelloInfo { Ticket = stranger });
            c2.Connect();
            Pump(host, c2, 0.1f);
            Assert.Equal("You are not a member of this match.", c2.RejectReason);

            var legit = MatchTickets.Issue(new TicketPayload { MatchId = cfg.MatchId, AccountId = "acc-1", Exp = MatchTickets.UnixNow() + 60 }, Key);
            var c3 = new GameClient(data, new LoopbackConnection(host), new HelloInfo { Ticket = legit });
            c3.Connect();
            Pump(host, c3, 0.1f);
            Assert.Equal(ClientConnectionState.Connected, c3.State);
        }

        private static void Pump(MatchHost host, GameClient client, float seconds)
        {
            float dt = 1f / 60f;
            for (float t = 0; t < seconds; t += dt)
            {
                host.Update(dt);
                client.Update(dt);
            }
        }
    }
}
