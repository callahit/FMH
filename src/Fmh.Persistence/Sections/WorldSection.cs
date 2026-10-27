using System.Text.Json;
using Fmh.Core.Data;
using Fmh.Core.Model;
using Microsoft.Data.Sqlite;
using static Fmh.Core.Persistence.Sql;

namespace Fmh.Core.Persistence.Sections;

/// <summary>Kern: Länder, Schiedsrichter, Manager, Vereine, Ligen, Posteingang und Saisonchronik.</summary>
public sealed class WorldSection : SaveSection
{
    public override string ModuleId => "world";

    /// <summary>Version 6: Mannschaften (Tabelle teams). Version 7: zweite Mannschaften und Regionen (Ligapyramiden, AP 3).</summary>
    public override int Version => 7;

    private const string TeamsTable = """
        CREATE TABLE teams (id INTEGER PRIMARY KEY, club_id INTEGER NOT NULL, world_id TEXT NOT NULL, ord INTEGER NOT NULL, data TEXT);
        """;

    private const string ClubColumnsV7 = """
        ALTER TABLE clubs ADD COLUMN reserve_of INTEGER;
        ALTER TABLE clubs ADD COLUMN region TEXT;
        ALTER TABLE clubs ADD COLUMN latitude REAL;
        ALTER TABLE clubs ADD COLUMN longitude REAL;
        """;

    /// <summary>
    /// 5 → 6: jeder Verein bekommt seine erste Mannschaft (Männer) mit der Kennung des Vereins.
    /// 6 → 7: Spalten für zweite Mannschaften, Region und Koordinaten.
    /// </summary>
    public override void Migrate(SqliteConnection c, SqliteTransaction tx, int fromVersion)
    {
        if (fromVersion < BaseVersion || fromVersion > Version) base.Migrate(c, tx, fromVersion);
        if (fromVersion <= 5)
        {
            Exec(c, tx, TeamsTable);
            Exec(c, tx, "INSERT INTO teams (id, club_id, world_id, ord, data) SELECT id, id, 'men', 0, NULL FROM clubs");
        }
        if (fromVersion <= 6) Exec(c, tx, ClubColumnsV7);
    }

    protected override string Schema => """
        CREATE TABLE countries (code TEXT PRIMARY KEY, config TEXT NOT NULL);
        CREATE TABLE referees (id INTEGER PRIMARY KEY, name TEXT NOT NULL, strictness INTEGER NOT NULL);
        CREATE TABLE managers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, human INTEGER NOT NULL, club_id INTEGER,
            reputation INTEGER NOT NULL, profile TEXT NOT NULL, career TEXT);
        CREATE TABLE clubs (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE, name TEXT NOT NULL, short_name TEXT NOT NULL,
            city TEXT NOT NULL, country TEXT NOT NULL, reputation INTEGER NOT NULL, primary_color TEXT NOT NULL,
            secondary_color TEXT NOT NULL, stadium TEXT NOT NULL, capacity INTEGER NOT NULL, league_id TEXT NOT NULL,
            manager_id INTEGER, tactic TEXT NOT NULL, balance INTEGER NOT NULL DEFAULT 0, transfer_budget INTEGER NOT NULL DEFAULT 0,
            wage_budget INTEGER NOT NULL DEFAULT 0, coach_quality INTEGER NOT NULL DEFAULT 10, youth_facilities INTEGER NOT NULL DEFAULT 10,
            youth_recruitment INTEGER NOT NULL DEFAULT 10, saved_tactics TEXT NOT NULL DEFAULT '[]', familiarity TEXT NOT NULL DEFAULT '{}',
            training TEXT, camp TEXT, youth_ids TEXT NOT NULL DEFAULT '[]', shortlist TEXT NOT NULL DEFAULT '[]',
            player_ids TEXT NOT NULL DEFAULT '[]', finance TEXT, stadium_data TEXT, grounds TEXT, sponsors TEXT NOT NULL DEFAULT '[]',
            sponsor_offers TEXT NOT NULL DEFAULT '[]', board TEXT, fans TEXT, rivals TEXT NOT NULL DEFAULT '[]', academy TEXT, license TEXT,
            reserve_of INTEGER, region TEXT, latitude REAL, longitude REAL);
        CREATE TABLE leagues (id TEXT PRIMARY KEY, ord INTEGER NOT NULL, name TEXT NOT NULL, country TEXT NOT NULL,
            level INTEGER NOT NULL, format TEXT NOT NULL, deductions TEXT NOT NULL DEFAULT '{}');
        CREATE TABLE league_clubs (league_id TEXT NOT NULL, club_id INTEGER NOT NULL, ord INTEGER NOT NULL);
        CREATE TABLE messages (id INTEGER PRIMARY KEY, manager_id INTEGER NOT NULL, date TEXT NOT NULL, type TEXT NOT NULL,
            sender TEXT NOT NULL, subject TEXT NOT NULL, body TEXT NOT NULL, read INTEGER NOT NULL, action TEXT NOT NULL DEFAULT 'None',
            action_ref INTEGER, required INTEGER NOT NULL DEFAULT 0, resolved INTEGER NOT NULL DEFAULT 0, sender_key TEXT,
            subject_ref TEXT, body_ref TEXT);
        CREATE TABLE history (ord INTEGER NOT NULL, season_year INTEGER NOT NULL, league_id TEXT NOT NULL, league_name TEXT NOT NULL,
            champion_id INTEGER NOT NULL, champion_name TEXT NOT NULL, final_order TEXT NOT NULL);
        """ + TeamsTable;

    public override void Save(SqliteConnection con, SqliteTransaction tx, World w)
    {
        using (var cmd = Insert(con, tx, "countries", 2))
            foreach (var c in w.Countries) Run(cmd, c.Code, JsonSerializer.Serialize(c, Json.Compact));

        using (var cmd = Insert(con, tx, "referees", 3))
            foreach (var r in w.Referees) Run(cmd, r.Id, r.Name, r.Strictness);

        using (var cmd = Insert(con, tx, "managers", 7))
            foreach (var m in w.Managers) Run(cmd, m.Id, m.Name, m.Human, m.ClubId, m.Reputation, JsonSerializer.Serialize(m.Profile, Json.Compact), J(m.Career));

        using (var cmd = Insert(con, tx, "clubs", 41))
            foreach (var c in w.Clubs.Values)
                Run(cmd, c.Id, c.Key, c.Name, c.ShortName, c.City, c.CountryCode, c.Reputation, c.PrimaryColor, c.SecondaryColor,
                    c.StadiumName, c.StadiumCapacity, c.LeagueId, c.ManagerId, J(c.Tactic),
                    c.Budget.Balance, c.Budget.TransferBudget, c.Budget.WageBudget, c.CoachQuality, c.YouthFacilities, c.YouthRecruitment,
                    J(c.SavedTactics), J(c.Familiarity), J(c.Training), c.Camp is null ? null : J(c.Camp), J(c.YouthIds), J(c.Shortlist), J(c.PlayerIds),
                    J(c.Finance), J(c.Stadium), J(c.Grounds), J(c.Sponsors), J(c.SponsorOffers), J(c.Board), J(c.Fans), J(c.Rivals),
                    J(c.Academy), J(c.License), c.ReserveOf, c.Region, c.Latitude, c.Longitude);

        // Die erste Mannschaft steht in den Vereinsspalten, weitere Mannschaften vollständig als JSON.
        using (var cmd = Insert(con, tx, "teams", 5))
            foreach (var c in w.Clubs.Values)
                for (var i = 0; i < c.Teams.Count; i++)
                    Run(cmd, c.Teams[i].Id, c.Id, c.Teams[i].WorldId, i, i == 0 ? null : J(c.Teams[i]));

        using (var cmd = Insert(con, tx, "leagues", 7))
            for (var i = 0; i < w.Leagues.Count; i++)
            {
                var l = w.Leagues[i];
                Run(cmd, l.Id, i, l.Name, l.CountryCode, l.Level, JsonSerializer.Serialize(l.Format, Json.Compact), J(l.Deductions));
            }
        using (var cmd = Insert(con, tx, "league_clubs", 3))
            foreach (var l in w.Leagues)
                for (var i = 0; i < l.ClubIds.Count; i++) Run(cmd, l.Id, l.ClubIds[i], i);

        using (var cmd = Insert(con, tx, "messages", 15))
            foreach (var m in w.Messages)
                Run(cmd, m.Id, m.ManagerId, D(m.Date), m.Type.ToString(), m.Sender, m.Subject, m.Body, m.Read,
                    m.Action.ToString(), m.ActionRefId, m.Required, m.Resolved,
                    m.SenderKey, Ref(m.SubjectRef), Ref(m.BodyRef));

        using (var cmd = Insert(con, tx, "history", 7))
            for (var i = 0; i < w.History.Count; i++)
            {
                var h = w.History[i];
                Run(cmd, i, h.SeasonYear, h.LeagueId, h.LeagueName, h.ChampionId, h.ChampionName, JsonSerializer.Serialize(h.FinalOrder));
            }
    }

    public override void Load(SqliteConnection con, World w)
    {
        foreach (var r in Rows(con, "SELECT config FROM countries ORDER BY code"))
            w.Countries.Add(JsonSerializer.Deserialize<CountryConfig>(r.GetString(0), Json.Compact)!.Normalize());

        foreach (var r in Rows(con, "SELECT id, name, strictness FROM referees ORDER BY id"))
            w.Referees.Add(new Referee { Id = r.GetInt32(0), Name = r.GetString(1), Strictness = r.GetInt32(2) });

        foreach (var r in Rows(con, "SELECT id, name, human, club_id, reputation, profile, career FROM managers ORDER BY id"))
            w.Managers.Add(new Manager
            {
                Id = r.GetInt32(0), Name = r.GetString(1), Human = r.GetInt32(2) == 1, ClubId = NInt(r, 3), Reputation = r.GetInt32(4),
                Profile = JsonSerializer.Deserialize<ManagerProfile>(r.GetString(5), Json.Compact) ?? new ManagerProfile(),
                Career = r.IsDBNull(6) ? new ManagerCareer() : PJ<ManagerCareer>(r.GetString(6)) ?? new(),
            });

        foreach (var r in Rows(con, "SELECT * FROM clubs ORDER BY id"))
        {
            var c = ReadClub(r);
            w.Clubs[c.Id] = c;
        }
        foreach (var r in Rows(con, "SELECT club_id, ord, id, world_id, data FROM teams ORDER BY club_id, ord"))
        {
            var club = w.Clubs[r.GetInt32(0)];
            if (r.GetInt32(1) == 0)
            {
                club.FirstTeam.Id = r.GetInt32(2);
                club.FirstTeam.WorldId = r.GetString(3);
            }
            else club.Teams.Add(PJ<Team>(r.GetString(4))!);
        }

        foreach (var r in Rows(con, "SELECT id, name, country, level, format, deductions FROM leagues ORDER BY ord"))
            w.Leagues.Add(new League
            {
                Id = r.GetString(0), Name = r.GetString(1), CountryCode = r.GetString(2), Level = r.GetInt32(3),
                Format = JsonSerializer.Deserialize<DivisionConfig>(r.GetString(4), Json.Compact)!.Normalize(),
                Deductions = PJ<Dictionary<int, int>>(r.GetString(5)) ?? new(),
            });
        var leagues = w.Leagues.ToDictionary(l => l.Id);
        foreach (var r in Rows(con, "SELECT league_id, club_id FROM league_clubs ORDER BY league_id, ord"))
            leagues[r.GetString(0)].ClubIds.Add(r.GetInt32(1));

        foreach (var r in Rows(con, "SELECT * FROM messages ORDER BY id"))
            w.Messages.Add(new Message
            {
                Id = r.GetInt32(0), ManagerId = r.GetInt32(1), Date = PD(r.GetString(2)), Type = Enum.Parse<MessageType>(r.GetString(3)),
                Sender = r.GetString(4), Subject = r.GetString(5), Body = r.GetString(6), Read = r.GetInt32(7) == 1,
                Action = Enum.Parse<MessageAction>(r.GetString(8)), ActionRefId = NInt(r, 9),
                Required = r.GetInt32(10) == 1, Resolved = r.GetInt32(11) == 1,
                SenderKey = NStr(r, 12), SubjectRef = PRef(r, 13), BodyRef = PRef(r, 14),
            });

        foreach (var r in Rows(con, "SELECT season_year, league_id, league_name, champion_id, champion_name, final_order FROM history ORDER BY ord"))
            w.History.Add(new SeasonRecord
            {
                SeasonYear = r.GetInt32(0), LeagueId = r.GetString(1), LeagueName = r.GetString(2), ChampionId = r.GetInt32(3),
                ChampionName = r.GetString(4), FinalOrder = JsonSerializer.Deserialize<List<int>>(r.GetString(5)) ?? new List<int>(),
            });
    }

    private static Club ReadClub(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0), Key = r.GetString(1), Name = r.GetString(2), ShortName = r.GetString(3), City = r.GetString(4),
        CountryCode = r.GetString(5), Reputation = r.GetInt32(6), PrimaryColor = r.GetString(7), SecondaryColor = r.GetString(8),
        StadiumName = r.GetString(9), StadiumCapacity = r.GetInt32(10), LeagueId = r.GetString(11), ManagerId = NInt(r, 12),
        Tactic = JsonSerializer.Deserialize<Tactic>(r.GetString(13), Json.Compact) ?? new Tactic(),
        Budget = new ClubBudget { Balance = r.GetInt64(14), TransferBudget = r.GetInt64(15), WageBudget = r.GetInt64(16) },
        CoachQuality = r.GetInt32(17), YouthFacilities = r.GetInt32(18), YouthRecruitment = r.GetInt32(19),
        SavedTactics = PJ<List<Tactic>>(r.GetString(20)) ?? new(),
        Familiarity = PJ<Dictionary<string, double>>(r.GetString(21)) ?? new(),
        Training = r.IsDBNull(22) ? new TrainingPlan() : PJ<TrainingPlan>(r.GetString(22)) ?? new(),
        Camp = r.IsDBNull(23) ? null : PJ<TrainingCamp>(r.GetString(23)),
        YouthIds = PJ<List<int>>(r.GetString(24)) ?? new(),
        Shortlist = PJ<List<int>>(r.GetString(25)) ?? new(),
        PlayerIds = PJ<List<int>>(r.GetString(26)) ?? new(),
        Finance = Opt<FinanceState>(r, 27) ?? new(),
        Stadium = Opt<Stadium>(r, 28) ?? new(),
        Grounds = Opt<Grounds>(r, 29) ?? new(),
        Sponsors = Opt<List<SponsorDeal>>(r, 30) ?? new(),
        SponsorOffers = Opt<List<SponsorDeal>>(r, 31) ?? new(),
        Board = Opt<BoardState>(r, 32) ?? new(),
        Fans = Opt<FanState>(r, 33) ?? new(),
        Rivals = Opt<List<int>>(r, 34) ?? new(),
        Academy = Opt<YouthAcademy>(r, 35) ?? new(),
        License = Opt<LicenseFile>(r, 36) ?? new(),
        ReserveOf = NInt(r, 37), Region = NStr(r, 38),
        Latitude = r.IsDBNull(39) ? null : r.GetDouble(39), Longitude = r.IsDBNull(40) ? null : r.GetDouble(40),
    };
}
