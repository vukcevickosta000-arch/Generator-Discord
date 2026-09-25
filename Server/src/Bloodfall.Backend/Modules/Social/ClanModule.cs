using System;
using System.Linq;
using System.Threading.Tasks;
using Bloodfall.Backend.Infrastructure;
using Bloodfall.Backend.Infrastructure.Data;
using Bloodfall.Backend.Infrastructure.Realtime;
using Bloodfall.Backend.Infrastructure.Security;
using Bloodfall.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Bloodfall.Backend.Modules.Social
{
    public sealed class ClanService
    {
        private readonly BloodfallDb _db;
        private readonly PresenceService _presence;
        private readonly ChatService _chat;
        private readonly RealtimeHub _hub;

        public ClanService(BloodfallDb db, PresenceService presence, ChatService chat, RealtimeHub hub) { _db = db; _presence = presence; _chat = chat; _hub = hub; }

        public async Task<ClanView> View(Guid clanId)
        {
            var c = await _db.Clans.FindAsync(clanId);
            if (c == null) return null;
            var members = await (from m in _db.ClanMembers join a in _db.Accounts on m.AccountId equals a.Id where m.ClanId == clanId select new { m, a }).ToListAsync();
            return new ClanView
            {
                ClanId = c.Id.ToString(), Name = c.Name, Tag = c.Tag, EmblemId = c.EmblemId, Description = c.Description, CreatedAt = c.CreatedAt,
                Rating = c.Rating, Wins = c.Wins, Losses = c.Losses,
                Members = members.OrderByDescending(x => x.m.Rank).ThenBy(x => x.a.DisplayName).Select(x => new ClanMemberView
                {
                    AccountId = x.a.Id.ToString(), DisplayName = x.a.DisplayName, Rank = x.m.Rank.ToString(), JoinedAt = x.m.JoinedAt, Status = _presence.Get(x.a.Id), Level = x.a.Level,
                }).ToList(),
            };
        }

        public async Task<IResult> Create(Guid me, CreateClanRequest r)
        {
            string err;
            if ((err = Validation.ClanName(r?.Name)) != null) return Api.BadRequest("invalid_name", err, "name");
            if ((err = Validation.ClanTag(r?.Tag)) != null) return Api.BadRequest("invalid_tag", err, "tag");
            var a = await _db.Accounts.FindAsync(me);
            if (a.ClanId != null) return Api.Conflict("already_in_clan", "Leave your current clan first.");
            var nn = Validation.Normalize(r.Name); var tn = Validation.Normalize(r.Tag);
            if (await _db.Clans.AnyAsync(c => c.NameNormalized == nn)) return Api.Conflict("name_taken", "A clan with that name already exists.", "name");
            if (await _db.Clans.AnyAsync(c => c.TagNormalized == tn)) return Api.Conflict("tag_taken", "That clan tag is already taken.", "tag");
            var clan = new Clan { Id = Guid.NewGuid(), Name = r.Name.Trim(), NameNormalized = nn, Tag = r.Tag.Trim().ToUpperInvariant(), TagNormalized = tn, Description = Validation.CleanText(r.Description, 300), EmblemId = string.IsNullOrEmpty(r.EmblemId) ? "emblem_default" : Validation.CleanText(r.EmblemId, 40), CreatedAt = DateTime.UtcNow };
            _db.Clans.Add(clan);
            _db.ClanMembers.Add(new ClanMember { ClanId = clan.Id, AccountId = me, Rank = ClanRank.Leader, JoinedAt = DateTime.UtcNow });
            a.ClanId = clan.Id;
            await _db.SaveChangesAsync();
            _presence.ClanChanged(me, clan.Id.ToString());
            _chat.Join(me, "clan:" + clan.Id);
            return Api.Ok(await View(clan.Id));
        }

        private async Task<ClanMember> Membership(Guid me) => await _db.ClanMembers.FirstOrDefaultAsync(m => m.AccountId == me);

        public async Task<IResult> Invite(Guid me, Guid target)
        {
            var mine = await Membership(me);
            if (mine == null || mine.Rank < ClanRank.Officer) return Api.Forbidden("Only officers and the leader can invite.");
            var t = await _db.Accounts.FindAsync(target);
            if (t == null) return Api.NotFound("Player not found.");
            if (t.ClanId != null) return Api.Conflict("in_clan", "That player is already in a clan.");
            if (await _db.ClanInvites.AnyAsync(i => i.ClanId == mine.ClanId && i.AccountId == target)) return Api.Conflict("already_invited", "Already invited.");
            var inv = new ClanInvite { Id = Guid.NewGuid(), ClanId = mine.ClanId, AccountId = target, InvitedBy = me, CreatedAt = DateTime.UtcNow };
            _db.ClanInvites.Add(inv);
            await _db.SaveChangesAsync();
            var clan = await _db.Clans.FindAsync(mine.ClanId);
            _hub.Send(target, RealtimeTypes.ClanInvite, new ClanInviteView { InviteId = inv.Id.ToString(), ClanId = clan.Id.ToString(), ClanName = clan.Name, ClanTag = clan.Tag, FromName = _presence.Name(me) });
            return Api.Ok(new { invited = true });
        }

        public async Task<IResult> Invites(Guid me)
        {
            var list = await (from i in _db.ClanInvites join c in _db.Clans on i.ClanId equals c.Id where i.AccountId == me select new ClanInviteView { InviteId = i.Id.ToString(), ClanId = c.Id.ToString(), ClanName = c.Name, ClanTag = c.Tag }).ToListAsync();
            return Api.Ok(list);
        }

        public async Task<IResult> Accept(Guid me, Guid inviteId)
        {
            var inv = await _db.ClanInvites.FindAsync(inviteId);
            if (inv == null || inv.AccountId != me) return Api.NotFound("Invitation not found.");
            var a = await _db.Accounts.FindAsync(me);
            if (a.ClanId != null) return Api.Conflict("already_in_clan", "Leave your current clan first.");
            _db.ClanMembers.Add(new ClanMember { ClanId = inv.ClanId, AccountId = me, Rank = ClanRank.Recruit, JoinedAt = DateTime.UtcNow });
            a.ClanId = inv.ClanId;
            _db.ClanInvites.RemoveRange(_db.ClanInvites.Where(i => i.AccountId == me));
            await _db.SaveChangesAsync();
            _presence.ClanChanged(me, inv.ClanId.ToString());
            _chat.Join(me, "clan:" + inv.ClanId);
            _chat.System("clan:" + inv.ClanId, $"{a.DisplayName} joined the clan.");
            return Api.Ok(await View(inv.ClanId));
        }

        public async Task<IResult> Leave(Guid me)
        {
            var m = await Membership(me);
            if (m == null) return Results.NoContent();
            var a = await _db.Accounts.FindAsync(me);
            _db.ClanMembers.Remove(m);
            a.ClanId = null;
            await _db.SaveChangesAsync();
            var remaining = await _db.ClanMembers.Where(x => x.ClanId == m.ClanId).OrderByDescending(x => x.Rank).ThenBy(x => x.JoinedAt).ToListAsync();
            if (remaining.Count == 0) { var c = await _db.Clans.FindAsync(m.ClanId); _db.Clans.Remove(c); }
            else if (m.Rank == ClanRank.Leader) remaining[0].Rank = ClanRank.Leader;
            await _db.SaveChangesAsync();
            _presence.ClanChanged(me, null);
            _chat.Leave(me, "clan:" + m.ClanId);
            _chat.System("clan:" + m.ClanId, $"{a.DisplayName} left the clan.");
            return Results.NoContent();
        }

        public async Task<IResult> SetRank(Guid me, ClanRankRequest r)
        {
            var mine = await Membership(me);
            if (mine == null || mine.Rank != ClanRank.Leader) return Api.Forbidden("Only the clan leader can change ranks.");
            if (!Guid.TryParse(r?.AccountId, out var target) || !Enum.TryParse<ClanRank>(r.Rank, true, out var rank)) return Api.BadRequest("invalid", "Invalid request.");
            var m = await _db.ClanMembers.FirstOrDefaultAsync(x => x.ClanId == mine.ClanId && x.AccountId == target);
            if (m == null) return Api.NotFound("Member not found.");
            if (rank == ClanRank.Leader) mine.Rank = ClanRank.Officer;
            m.Rank = rank;
            await _db.SaveChangesAsync();
            return Api.Ok(await View(mine.ClanId));
        }

        public async Task<IResult> Kick(Guid me, Guid target)
        {
            var mine = await Membership(me);
            var m = await _db.ClanMembers.FirstOrDefaultAsync(x => x.AccountId == target);
            if (mine == null || m == null || m.ClanId != mine.ClanId || mine.Rank < ClanRank.Officer || m.Rank >= mine.Rank) return Api.Forbidden("You cannot remove that member.");
            var a = await _db.Accounts.FindAsync(target);
            _db.ClanMembers.Remove(m);
            a.ClanId = null;
            await _db.SaveChangesAsync();
            _presence.ClanChanged(target, null);
            _chat.Leave(target, "clan:" + mine.ClanId);
            return Results.NoContent();
        }
    }

    public static class ClanEndpoints
    {
        public static void MapClans(this IEndpointRouteBuilder app)
        {
            var g = app.MapGroup("/api/clans").RequireAuthorization();
            g.MapGet("/mine", async (ClanService s, BloodfallDb db, HttpContext c) =>
            {
                var a = await db.Accounts.FindAsync(c.User.AccountId());
                return a?.ClanId == null ? Api.Ok<ClanView>(null) : Api.Ok(await s.View(a.ClanId.Value));
            });
            g.MapGet("/{id:guid}", async (Guid id, ClanService s) => (await s.View(id)) is ClanView v ? Api.Ok(v) : Api.NotFound("Clan not found."));
            g.MapPost("/", (CreateClanRequest r, ClanService s, HttpContext c) => s.Create(c.User.AccountId(), r));
            g.MapPost("/invite", (AccountRef r, ClanService s, HttpContext c) => Guid.TryParse(r?.AccountId, out var t) ? s.Invite(c.User.AccountId(), t) : Task.FromResult(Api.BadRequest("invalid", "Invalid account.")));
            g.MapGet("/invites", (ClanService s, HttpContext c) => s.Invites(c.User.AccountId()));
            g.MapPost("/invites/{id:guid}/accept", (Guid id, ClanService s, HttpContext c) => s.Accept(c.User.AccountId(), id));
            g.MapPost("/leave", (ClanService s, HttpContext c) => s.Leave(c.User.AccountId()));
            g.MapPost("/rank", (ClanRankRequest r, ClanService s, HttpContext c) => s.SetRank(c.User.AccountId(), r));
            g.MapPost("/kick", (AccountRef r, ClanService s, HttpContext c) => Guid.TryParse(r?.AccountId, out var t) ? s.Kick(c.User.AccountId(), t) : Task.FromResult(Api.BadRequest("invalid", "Invalid account.")));
        }
    }
}
