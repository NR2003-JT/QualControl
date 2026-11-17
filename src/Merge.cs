using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace QualControl
{
    // QualControl stage 4: merging saved qualifying results (usually two sessions with two different rosters, 80+ cars)
    // into one finishing order, saved as a results file that Reapply writes in the race weekend's qualifying session.
    // Rules (agreed with the user 2026-11-16):
    //   - ranked by the round reached first (knockout / Indy road; single car runs and the Indy oval are all round 1),
    //     then by the qualifying time (single car best / Indy oval average / the deciding time of the round), no time last;
    //   - a driver in more than one session keeps the best of them (by the same ranking);
    //   - ties go to the earlier session (the order of the list), then the place within that session;
    //   - a driver is its .car file (files saved before 0.4.0 have none: car number + name instead).
    // The times written stay the saved ones; only where the merged order needs it (a later round's car slower than an
    // earlier round's, two equal times) a car's time is raised to 1 ms behind the car ahead, as the knockout grid does.
    public static class ResultsMerge
    {
        public class Entry
        {
            public Qualifying.SavedCar Car; public int Session; public string Key;
            public int Written;                                 // the time it is written with (ms)
            public bool Raised;                                 // raised so the merged order holds
            public List<int> Others = new List<int>();          // the other sessions this driver was in (dropped)
        }

        public static string Key(Qualifying.SavedCar c)
        {
            if (c.IsPlayer) return "player";                    // the player's car, whatever it was saved as
            return c.File.Length > 0 ? "file:" + c.File.ToLowerInvariant() : "num:" + c.Num + "|" + c.Name.Trim().ToLowerInvariant();
        }

        // the merged order, best first; sessions in their tie-break order (the first wins a tie)
        public static List<Entry> Merge(IList<Qualifying.SavedResults> sessions)
        {
            var all = new List<Entry>();
            for (int s = 0; s < sessions.Count; s++)
                foreach (var c in sessions[s].Cars) all.Add(new Entry { Car = c, Session = s, Key = Key(c) });
            var ranked = all.OrderByDescending(e => e.Car.Round).ThenBy(e => e.Car.Time.HasValue ? 0 : 1).ThenBy(e => e.Car.Time ?? int.MaxValue)
                .ThenBy(e => e.Session).ThenBy(e => e.Car.Pos).ToList();
            var kept = new List<Entry>(); var byKey = new Dictionary<string, Entry>();
            foreach (var e in ranked)
            {
                Entry k;
                if (byKey.TryGetValue(e.Key, out k)) { k.Others.Add(e.Session); continue; }
                byKey[e.Key] = e; kept.Add(e);
            }
            int last = 0;
            foreach (var e in kept)
            {
                int want = e.Car.Time ?? last + 100;
                e.Written = Math.Max(want, last + 1);
                e.Raised = e.Car.Time.HasValue && e.Written != e.Car.Time.Value;
                last = e.Written;
            }
            return kept;
        }

        // the merged results file: the laps of a car whose time stands are its saved laps (their best / average is its
        // time), else one lap of the time it is written with
        public static Qualifying.SavedResults ToResults(IList<Qualifying.SavedResults> sessions, List<Entry> merged, HashSet<Entry> field = null)
        {
            var types = sessions.Select(s => s.Type).Distinct().ToList();
            var r = new Qualifying.SavedResults
            {
                Track = string.Join(" + ", sessions.Select(s => s.Track).Distinct().ToArray()),
                Type = "merged " + string.Join("+", types.ToArray()),
                Series = string.Join(" + ", sessions.Select(s => s.Series).Where(x => x.Length > 0).Distinct().ToArray()),
                Saved = DateTime.UtcNow, Pace = 1,
                Sources = sessions.Select(s => Path.GetFileName(s.Path)).ToList()
            };
            int pos = 0;
            foreach (var e in merged)
            {
                var c = e.Car;
                bool lapsOk = !e.Raised && c.Time.HasValue && c.Laps.Count > 0 && (c.Laps.Min() == c.Time.Value || c.Laps.All(x => x == c.Time.Value));
                r.Cars.Add(new Qualifying.SavedCar
                {
                    Pos = ++pos, Num = c.Num, Name = c.Name, File = c.File, Round = c.Round, Time = c.Time, Source = e.Session, IsPlayer = c.IsPlayer,
                    Laps = lapsOk ? c.Laps.ToList() : new List<int> { e.Written }
                });
            }
            // the cars outside the field: DNQ (the AI ones are not in the race roster; the player is told)
            if (field != null)
            {
                for (int i = 0; i < merged.Count; i++) r.Cars[i].IsDnq = !field.Contains(merged[i]);
                r.Dnq = r.Cars.Where(c => c.IsDnq).Select(c => c.Num).ToList();
            }
            return r;
        }

        // The race field: the top `cars` of the merged order (the player's car counts as one of them). With
        // playerAlwaysIn the player's car makes it from any place (the last AI car inside the cut drops out).
        public static HashSet<Entry> Field(List<Entry> merged, int cars, bool playerAlwaysIn)
        {
            var f = new HashSet<Entry>(merged.Take(Math.Max(0, cars)));
            var me = merged.FirstOrDefault(e => e.Car.IsPlayer);
            if (playerAlwaysIn && me != null && !f.Contains(me) && cars > 0)
            {
                var lastAi = merged.Take(cars).LastOrDefault(e => !e.Car.IsPlayer);
                if (lastAi != null) f.Remove(lastAi);
                f.Add(me);
            }
            return f;
        }

        // The race roster: a copy of a roster .lst (every line kept, in order, same line ends) with only the + / - marks
        // changed: + for the field's AI cars (by .car file), - for every other line. A field car the roster doesn't list is
        // added at the end with +. The player's car is never in a roster. Returns the new file's text.
        public static string Roster(string baseText, IEnumerable<string> fieldFiles, out List<string> added, out int marked)
        {
            string nl = baseText.Contains("\r\n") ? "\r\n" : "\n";
            bool endNl = baseText.EndsWith("\n");
            var want = new HashSet<string>(fieldFiles.Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = baseText.Replace("\r\n", "\n").Split('\n').ToList();
            if (endNl) lines.RemoveAt(lines.Count - 1);
            marked = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string l = lines[i];
                if (l.Length < 2 || (l[0] != '+' && l[0] != '-')) continue;           // (anything else is kept as it is)
                string name = l.Substring(1);
                bool inField = want.Contains(name.Trim());
                if (inField) { seen.Add(name.Trim()); marked++; }
                lines[i] = (inField ? "+" : "-") + name;
            }
            added = want.Where(x => !seen.Contains(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string a in added) lines.Add("+" + a);
            return string.Join(nl, lines.ToArray()) + (endNl ? nl : "");
        }

        // one line per car for the merge window / the log
        public static string Line(Entry e, int pos, IList<Qualifying.SavedResults> sessions, bool showRound)
        {
            var c = e.Car;
            return string.Format(CultureInfo.InvariantCulture, "{0,3}. #{1,-4} {2,-22} {3,9}{4} S{5}{6}{7}", pos, c.Num, Trim(c.IsPlayer ? c.Name + " (you)" : c.Name, 22),
                c.Time.HasValue ? LapFmt.Time(c.Time.Value) : "no time", e.Raised ? "*" : " ", e.Session + 1,
                showRound ? " R" + c.Round : "", e.Others.Count > 0 ? "  (also S" + string.Join(", S", e.Others.Select(s => (s + 1).ToString()).ToArray()) + ")" : "");
        }
        static string Trim(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "."; }
    }
}
