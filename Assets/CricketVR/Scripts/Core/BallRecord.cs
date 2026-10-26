using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace CricketVR
{
    /// <summary>
    /// One finalized ball, for both modes (REQ-027, plan.md §16). Unknown or not-yet-measured values are NaN
    /// (written as empty CSV cells). Later milestones fill the Shot Confidence (M8) and virtual-field (M9/M10)
    /// columns; the CSV header lists every column from the start so logs from all milestones share one format.
    /// </summary>
    public sealed class BallRecord
    {
        // Session
        public string SessionId = "";
        public SessionMode Mode;
        /// <summary>0-based index within the session (set by the state machine when accepted).</summary>
        public int BallIndex;
        public int DeliveryId;
        /// <summary>Seconds since the session started, at finalization.</summary>
        public float Time;

        // Delivery
        /// <summary>World-frame launch fields (line mirrored for a left-hander).</summary>
        public DeliveryConfig Delivery;
        /// <summary>The session's random seed.</summary>
        public int Seed;

        // Delivery type (M7.5, REQ-045). Unknown or not yet measured = NaN.
        public string DeliveryType = "";
        public float Movement = float.NaN;
        public Handedness Hand;
        public LineReference LineRef;
        /// <summary>The request's own seed (natural variation, REQ-048).</summary>
        public int BallSeed;
        /// <summary>Natural-variation factors applied to swing, drift and turn (REQ-048).</summary>
        public float VarSwing = float.NaN, VarDrift = float.NaN, VarTurn = float.NaN;
        /// <summary>Swing deviation (m): largest sideways distance from the straight release-to-bounce line (DT-13).</summary>
        public float PredictedCurve = float.NaN, MeasuredCurve = float.NaN;
        /// <summary>Measured change of horizontal direction at the bounce (degrees, + = toward +X).</summary>
        public float TurnDegrees = float.NaN;
        /// <summary>Ball x where it reached the striker's popping crease (world, m).</summary>
        public float ArrivalLine = float.NaN;
        /// <summary>Where the delivery pitched (T070), and its distance from the requested bounce point.</summary>
        public Vector3 Bounce = NaN3;
        public float BounceError = float.NaN;
        /// <summary>Where PhysX started the rebound relative to the pitch point (m along the travel, + = past it).</summary>
        public float ReboundOffset = float.NaN;

        // Physical result
        public DeliveryResult Result;

        // Contact (NaN when unhit)
        public Vector3 ContactPoint = NaN3, ContactNormal = NaN3, BallVelocityIn = NaN3, BallVelocityOut = NaN3, BatPointVelocity = NaN3;
        public float ApproachSpeed = float.NaN, SweetFactor = float.NaN, EdgeFactor = float.NaN, HeightFromToe = float.NaN;

        // Swing and near-miss (M8 fills the swing values)
        public float SwingPeakSpeed = float.NaN;
        /// <summary>Contact time minus swing peak time (s): negative = early, positive = late.</summary>
        public float SwingTimingOffset = float.NaN;
        public float TrackingResetAge = float.NaN;
        /// <summary>Closest bat-ball gap during the delivery (m), unhit balls.</summary>
        public float ClosestGap = float.NaN;

        // Shot Confidence (M8)
        public float Confidence = float.NaN, ConfMiddle = float.NaN, ConfTiming = float.NaN, ConfControl = float.NaN, ConfPower = float.NaN;
        public string ShotClass = "";

        // Virtual field (M9/M10)
        public string SimOutcome = "";
        public int SimRuns = -1;
        public float SimCarry = float.NaN;

        public static readonly Vector3 NaN3 = new Vector3(float.NaN, float.NaN, float.NaN);

        public int Over => BallIndex / 6 + 1;
        public int BallInOver => BallIndex % 6 + 1;
        public bool BatContact => Result.BatContact;

        // ---------- CSV ----------

        struct Column
        {
            public string Name;
            public Func<BallRecord, string> Get;
            public Action<BallRecord, string> Set;
        }

        static readonly Column[] Columns = BuildColumns();

        public static string CsvHeader
        {
            get
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Columns.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Columns[i].Name);
                }
                return sb.ToString();
            }
        }

        public string ToCsvRow()
        {
            var sb = new StringBuilder(512);
            for (int i = 0; i < Columns.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(Columns[i].Get(this)));
            }
            return sb.ToString();
        }

        /// <summary>Parses a row written by <see cref="ToCsvRow"/> under <see cref="CsvHeader"/> (the calibration tool reads logs back).</summary>
        public static BallRecord FromCsv(string header, string row)
        {
            var names = SplitCsv(header);
            var cells = SplitCsv(row);
            var r = new BallRecord();
            var index = new Dictionary<string, int>();
            for (int i = 0; i < names.Count; i++) index[names[i]] = i;
            foreach (var c in Columns)
                if (index.TryGetValue(c.Name, out int i) && i < cells.Count)
                    c.Set(r, cells[i]);
            return r;
        }

        static Column[] BuildColumns()
        {
            var cols = new List<Column>();
            void Add(string name, Func<BallRecord, string> get, Action<BallRecord, string> set) => cols.Add(new Column { Name = name, Get = get, Set = set });
            void AddF(string name, Func<BallRecord, float> get, Action<BallRecord, float> set) => Add(name, r => F(get(r)), (r, s) => set(r, PF(s)));
            void AddV(string name, Func<BallRecord, Vector3> get, Action<BallRecord, Vector3> set)
            {
                AddF(name + "_x", r => get(r).x, (r, v) => { var p = get(r); p.x = v; set(r, p); });
                AddF(name + "_y", r => get(r).y, (r, v) => { var p = get(r); p.y = v; set(r, p); });
                AddF(name + "_z", r => get(r).z, (r, v) => { var p = get(r); p.z = v; set(r, p); });
            }
            // Result is a readonly struct: columns rebuild it field by field through a mutable copy.
            void SetResult(BallRecord r, Func<ResultFields, ResultFields> change) => r.Result = change(ResultFields.From(r.Result)).ToResult();

            Add("session", r => r.SessionId, (r, s) => r.SessionId = s);
            Add("mode", r => r.Mode.ToString(), (r, s) => r.Mode = Enum.TryParse(s, out SessionMode m) ? m : default);
            Add("ball", r => (r.BallIndex + 1).ToString(CultureInfo.InvariantCulture), (r, s) => r.BallIndex = PI(s) - 1);
            Add("over", r => r.Over.ToString(CultureInfo.InvariantCulture), (r, s) => { });
            Add("ball_in_over", r => r.BallInOver.ToString(CultureInfo.InvariantCulture), (r, s) => { });
            Add("delivery_id", r => r.DeliveryId.ToString(CultureInfo.InvariantCulture), (r, s) => r.DeliveryId = PI(s));
            AddF("time_s", r => r.Time, (r, v) => r.Time = v);
            Add("delivery", r => r.Delivery.label ?? "", (r, s) => r.Delivery.label = s);
            AddF("speed_kmh", r => r.Delivery.speedKmh, (r, v) => r.Delivery.speedKmh = v);
            AddF("line_m", r => r.Delivery.line, (r, v) => r.Delivery.line = v);
            AddF("length_m", r => r.Delivery.length, (r, v) => r.Delivery.length = v);
            Add("seed", r => r.Seed.ToString(CultureInfo.InvariantCulture), (r, s) => r.Seed = PI(s));
            Add("type", r => r.DeliveryType, (r, s) => r.DeliveryType = s);
            AddF("movement", r => r.Movement, (r, v) => r.Movement = v);
            Add("hand", r => r.Hand.ToString(), (r, s) => r.Hand = Enum.TryParse(s, out Handedness h) ? h : default);
            Add("line_ref", r => r.LineRef.ToString(), (r, s) => r.LineRef = Enum.TryParse(s, out LineReference l) ? l : default);
            Add("ball_seed", r => r.BallSeed.ToString(CultureInfo.InvariantCulture), (r, s) => r.BallSeed = PI(s));
            AddF("var_swing", r => r.VarSwing, (r, v) => r.VarSwing = v);
            AddF("var_drift", r => r.VarDrift, (r, v) => r.VarDrift = v);
            AddF("var_turn", r => r.VarTurn, (r, v) => r.VarTurn = v);
            AddF("pred_curve_m", r => r.PredictedCurve, (r, v) => r.PredictedCurve = v);
            AddF("curve_m", r => r.MeasuredCurve, (r, v) => r.MeasuredCurve = v);
            AddF("turn_deg", r => r.TurnDegrees, (r, v) => r.TurnDegrees = v);
            AddF("arrival_line_m", r => r.ArrivalLine, (r, v) => r.ArrivalLine = v);
            AddV("bounce", r => r.Bounce, (r, v) => r.Bounce = v);
            AddF("bounce_error_m", r => r.BounceError, (r, v) => r.BounceError = v);
            AddF("rebound_offset_m", r => r.ReboundOffset, (r, v) => r.ReboundOffset = v);
            Add("outcome", r => r.Result.Outcome.ToString(), (r, s) => SetResult(r, f => { f.Outcome = Enum.TryParse(s, out DeliveryOutcome o) ? o : default; return f; }));
            Add("end", r => r.Result.End.ToString(), (r, s) => SetResult(r, f => { f.End = Enum.TryParse(s, out DeliveryEnd e) ? e : default; return f; }));
            Add("zone", r => r.Result.Zone.ToString(), (r, s) => SetResult(r, f => { f.Zone = Enum.TryParse(s, out ScoringZone z) ? z : default; return f; }));
            Add("bat_contact", r => r.Result.BatContact ? "1" : "0", (r, s) => SetResult(r, f => { f.BatContact = s == "1"; return f; }));
            Add("bowled", r => r.Result.Bowled ? "1" : "0", (r, s) => SetResult(r, f => { f.Bowled = s == "1"; return f; }));
            Add("carry_state", r => r.Result.Carry.ToString(), (r, s) => SetResult(r, f => { f.Carry = Enum.TryParse(s, out CarryState c) ? c : default; return f; }));
            AddF("carry_m", r => r.Result.Carry == CarryState.Measured ? r.Result.CarryMetres : float.NaN,
                (r, v) => SetResult(r, f => { f.CarryMetres = float.IsNaN(v) ? 0f : v; return f; }));
            AddV("contact", r => r.ContactPoint, (r, v) => r.ContactPoint = v);
            AddV("normal", r => r.ContactNormal, (r, v) => r.ContactNormal = v);
            AddV("v_in", r => r.BallVelocityIn, (r, v) => r.BallVelocityIn = v);
            AddV("v_out", r => r.BallVelocityOut, (r, v) => r.BallVelocityOut = v);
            AddV("v_bat", r => r.BatPointVelocity, (r, v) => r.BatPointVelocity = v);
            AddF("approach_ms", r => r.ApproachSpeed, (r, v) => r.ApproachSpeed = v);
            AddF("sweet", r => r.SweetFactor, (r, v) => r.SweetFactor = v);
            AddF("edge", r => r.EdgeFactor, (r, v) => r.EdgeFactor = v);
            AddF("height_from_toe_m", r => r.HeightFromToe, (r, v) => r.HeightFromToe = v);
            AddF("swing_peak_ms", r => r.SwingPeakSpeed, (r, v) => r.SwingPeakSpeed = v);
            AddF("swing_timing_offset_s", r => r.SwingTimingOffset, (r, v) => r.SwingTimingOffset = v);
            AddF("tracking_reset_age_s", r => r.TrackingResetAge, (r, v) => r.TrackingResetAge = v);
            AddF("closest_gap_m", r => r.ClosestGap, (r, v) => r.ClosestGap = v);
            AddF("confidence", r => r.Confidence, (r, v) => r.Confidence = v);
            AddF("conf_middle", r => r.ConfMiddle, (r, v) => r.ConfMiddle = v);
            AddF("conf_timing", r => r.ConfTiming, (r, v) => r.ConfTiming = v);
            AddF("conf_control", r => r.ConfControl, (r, v) => r.ConfControl = v);
            AddF("conf_power", r => r.ConfPower, (r, v) => r.ConfPower = v);
            Add("shot_class", r => r.ShotClass, (r, s) => r.ShotClass = s);
            Add("sim_outcome", r => r.SimOutcome, (r, s) => r.SimOutcome = s);
            Add("sim_runs", r => r.SimRuns < 0 ? "" : r.SimRuns.ToString(CultureInfo.InvariantCulture), (r, s) => r.SimRuns = s.Length == 0 ? -1 : PI(s));
            AddF("sim_carry_m", r => r.SimCarry, (r, v) => r.SimCarry = v);
            return cols.ToArray();
        }

        /// <summary>Mutable copy of <see cref="DeliveryResult"/> used while parsing.</summary>
        struct ResultFields
        {
            public int DeliveryId;
            public DeliveryOutcome Outcome;
            public DeliveryEnd End;
            public ScoringZone Zone;
            public bool BatContact, Bowled;
            public CarryState Carry;
            public float CarryMetres;
            public ShotData Shot;
            public DeliveryConfig Config;

            public static ResultFields From(DeliveryResult r) => new ResultFields
            {
                DeliveryId = r.DeliveryId, Outcome = r.Outcome, End = r.End, Zone = r.Zone, BatContact = r.BatContact,
                Bowled = r.Bowled, Carry = r.Carry, CarryMetres = r.CarryMetres, Shot = r.Shot, Config = r.Config,
            };

            public DeliveryResult ToResult() =>
                new DeliveryResult(DeliveryId, Outcome, End, Zone, BatContact, Bowled, Carry, CarryMetres, Shot, Config);
        }

        static string F(float v) => float.IsNaN(v) ? "" : v.ToString("G9", CultureInfo.InvariantCulture);
        static float PF(string s) => s.Length == 0 ? float.NaN : float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        static int PI(string s) => s.Length == 0 ? 0 : int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }
    }
}
