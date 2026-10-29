using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FD = CricketVR.FieldDimensions;

namespace CricketVR.Tests
{
    /// <summary>
    /// Wicket reaction (T086, T087; AC-32, AC-33 automated): the real BowlingMachine, BallController, ScoreManager,
    /// SessionManager and WicketReaction on a pitch with a back net and stumps built like the scene builder's (one
    /// detection box, collider-free stump visuals, bails as kinematic rigidbodies on WicketDebris). Both modes.
    /// </summary>
    public class WicketReactionPlayModeTests
    {
        GameObject _root;
        GameConfig _config;
        BallController _ball;
        BowlingMachine _machine;
        ScoreManager _score;
        SessionManager _session;
        WicketReaction _wicket;
        float _timeScale;
        readonly List<Object> _owned = new List<Object>();
        readonly List<(int id, float time)> _results = new List<(int, float)>();
        readonly List<(int id, float time)> _reactions = new List<(int, float)>();
        int _ballDebrisContacts;
        Material _ledMaterial;

        [SetUp]
        public void SetUp()
        {
            _owned.Clear();
            _results.Clear();
            _reactions.Clear();
            _ballDebrisContacts = 0;
            _timeScale = Time.timeScale;
            Time.timeScale = 4f;
            _config = ScriptableObject.CreateInstance<GameConfig>();
            _config.session.timings = new SessionTimings { firstCountdown = 1f, countdown = 1f, betweenBalls = 1f };
            _config.session.logBallsCsv = false;
            _root = new GameObject("WicketTestRig");

            PhysicsMaterial pitch = null;
#if UNITY_EDITOR
            pitch = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/CricketVR/Settings/Physics/PitchSurface.physicMaterial"));
#endif
            _owned.Add(pitch);
            Box("Ground", "Pitch", new Vector3(0f, FD.PitchSurfaceHeight - 0.5f, 10f), new Vector3(20f, 1f, 50f)).GetComponent<Collider>().sharedMaterial = pitch;
            Box("RearWall", "Boundary", new Vector3(0f, 2f, FD.NetRearZ - 0.05f), new Vector3(3.6f, 4f, 0.1f)).AddComponent<NetWall>().side = WallSide.Rear;

            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.SetActive(false);
            ballGo.name = "Ball";
            ballGo.layer = LayerMask.NameToLayer("Ball");
            ballGo.transform.SetParent(_root.transform);
            ballGo.transform.position = FD.DefaultReleasePoint;
            ballGo.AddComponent<Rigidbody>().isKinematic = true;
            _ball = ballGo.AddComponent<BallController>();
            _ball.Configure(_config, null, ballGo.GetComponent<Renderer>());
            ballGo.AddComponent<BallFlight>();

            var gameplay = new GameObject("Gameplay");
            gameplay.SetActive(false);
            gameplay.transform.SetParent(_root.transform);
            _machine = gameplay.AddComponent<BowlingMachine>();
            _machine.Configure(_config, _ball, null, null, null);
            _score = gameplay.AddComponent<ScoreManager>();
            _score.Configure(_config, _ball, null, _machine);
            var ui = gameplay.AddComponent<SessionUI>();
            _session = gameplay.AddComponent<SessionManager>();
            _session.Configure(_config, _machine, _ball, _score, null, null, null, null, null, null, ui, null, null, null);

            _wicket = BuildStumps();
            _session.ConfigureWicket(_wicket);

            ballGo.SetActive(true);
            gameplay.SetActive(true);
            _wicket.gameObject.SetActive(true);
            _score.DeliveryFinalized += r => _results.Add((r.DeliveryId, Time.fixedTime));
            _wicket.Reacted += (id, plan) => _reactions.Add((id, Time.fixedTime));
            int debris = LayerMask.NameToLayer("WicketDebris");
            _ball.Collided += (id, c) => { if (c.collider.gameObject.layer == debris) _ballDebrisContacts++; };
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _timeScale;
            Object.Destroy(_root);
            Object.Destroy(_config);
            foreach (var o in _owned) Object.Destroy(o);
        }

        GameObject Box(string name, string layer, Vector3 centre, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = LayerMask.NameToLayer(layer);
            go.transform.SetParent(_root.transform);
            go.transform.position = centre;
            go.transform.localScale = size;
            return go;
        }

        /// <summary>The striker's stumps as PracticeNetBuilder and BowlingBuilder make them.</summary>
        WicketReaction BuildStumps()
        {
            var root = new GameObject("Stumps");
            root.SetActive(false);
            root.layer = LayerMask.NameToLayer("Stumps");
            root.transform.SetParent(_root.transform);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, FD.StumpHeight * 0.5f, 0f);
            box.size = new Vector3(FD.StumpSetWidth, FD.StumpHeight, FD.StumpDiameter);
            float spacing = (FD.StumpSetWidth - FD.StumpDiameter) * 0.5f;
            var stumps = new Transform[3];
            int i = 0;
            foreach (var (label, x) in new[] { ("OffStump", spacing), ("MiddleStump", 0f), ("LegStump", -spacing) })
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                s.name = label;
                Object.DestroyImmediate(s.GetComponent<Collider>());
                s.transform.SetParent(root.transform, false);
                s.transform.localPosition = new Vector3(x, FD.StumpHeight * 0.5f, 0f);
                s.transform.localScale = new Vector3(FD.StumpDiameter, FD.StumpHeight * 0.5f, FD.StumpDiameter);
                stumps[i++] = s.transform;
            }
            var bails = new Rigidbody[2];
            float bailY = FD.StumpHeight + FD.BailHeightAboveStumps * 0.5f;
            i = 0;
            foreach (var (label, x) in new[] { ("OffBail", spacing * 0.5f), ("LegBail", -spacing * 0.5f) })
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                b.name = label;
                Object.DestroyImmediate(b.GetComponent<Collider>());
                b.layer = LayerMask.NameToLayer("WicketDebris");
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = new Vector3(x, bailY, 0f);
                b.transform.localScale = new Vector3(0.012f, FD.BailLength * 0.5f, 0.012f);
                b.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var capsule = b.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                var body = b.AddComponent<Rigidbody>();
                body.mass = 0.02f;
                body.isKinematic = true;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                bails[i++] = body;
            }
            var audio = root.AddComponent<AudioSource>();
            var reaction = root.AddComponent<WicketReaction>();
            var leds = new List<Renderer>();
            foreach (var s in stumps) leds.Add(s.GetComponent<Renderer>());
            foreach (var b in bails) leds.Add(b.GetComponent<Renderer>());
            _ledMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.red };
            _owned.Add(_ledMaterial);
            reaction.Configure(_config, _score, _machine, _ball, stumps, bails, box, audio, new AudioClip[0], leds.ToArray(), _ledMaterial);
            return reaction;
        }

        // ---------- helpers ----------

        static DeliveryRequest AtTheStumps(float kmh, float length) =>
            DeliveryRequest.FromConfig(DeliveryConfig.Create($"at the stumps {kmh:F0}", kmh, 0f, length));

        IEnumerator BowlUntilResult(DeliveryRequest request)
        {
            yield return new WaitForFixedUpdate();
            int id = _machine.Bowl(request);
            Assert.AreNotEqual(0, id, _machine.LastError);
            float until = Time.time + 6f;
            while (Time.time < until && !_results.Exists(r => r.id == id))
                yield return new WaitForFixedUpdate();
            Assert.IsTrue(_results.Exists(r => r.id == id), $"{request}: no result");
        }

        void AssertWicketWhole(string when)
        {
            Assert.IsFalse(_wicket.Broken, $"{when}: still broken");
            float spacing = (FD.StumpSetWidth - FD.StumpDiameter) * 0.5f;
            var stumpX = new[] { spacing, 0f, -spacing };
            for (int i = 0; i < 3; i++)
            {
                var t = _wicket.Stumps[i];
                Assert.Less(Vector3.Distance(t.localPosition, new Vector3(stumpX[i], FD.StumpHeight * 0.5f, 0f)), 1e-4f, $"{when}: stump {i} position");
                Assert.Less(Quaternion.Angle(t.localRotation, Quaternion.identity), 0.1f, $"{when}: stump {i} rotation");
            }
            var bailX = new[] { spacing * 0.5f, -spacing * 0.5f };
            float bailY = FD.StumpHeight + FD.BailHeightAboveStumps * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                var b = _wicket.Bails[i];
                Assert.IsTrue(b.isKinematic, $"{when}: bail {i} physics stopped");
                Assert.Less(Vector3.Distance(b.transform.localPosition, new Vector3(bailX[i], bailY, 0f)), 1e-4f, $"{when}: bail {i} position");
                Assert.Less(Quaternion.Angle(b.transform.localRotation, Quaternion.Euler(0f, 0f, 90f)), 0.1f, $"{when}: bail {i} rotation");
                Assert.Less(Vector3.Distance(b.position, b.transform.position), 1e-4f, $"{when}: bail {i} body where its transform is");
            }
        }

        /// <summary>Breaks the wicket as a decided Bowled would, for the reset tests.</summary>
        void Break(int id) => _wicket.React(id, new Vector3(0f, 0.4f, 0.5f), new Vector3(0f, -1f, -30f));

        // ---------- AC-32 ----------

        [UnityTest]
        public IEnumerator BallAtTheStumpsBreaksTheWicketWithinOneStepOfTheResultInBothModes()
        {
            var degrees = new Dictionary<float, float>();
            foreach (var mode in new[] { EnvironmentMode.NetMode, EnvironmentMode.GroundMode })
            foreach (var (kmh, length) in new[] { (80f, 3.0f), (140f, 1.6f) })
            {
                _score.SetEnvironment(mode);
                _wicket.SetMode(mode);
                _results.Clear();
                _reactions.Clear();
                yield return BowlUntilResult(AtTheStumps(kmh, length));
                var result = _score.Results[_score.Results.Count - 1];
                Assert.IsTrue(result.Bowled, $"{mode} {kmh} km/h: the test ball should be bowled ({result})");
                Assert.AreEqual(1, _reactions.Count, $"{mode} {kmh}: one reaction");
                Assert.AreEqual(result.DeliveryId, _reactions[0].id);
                Assert.AreEqual(_results[0].time, _reactions[0].time, 1e-6f, "reacted in the step the wicket was decided");
                yield return new WaitForFixedUpdate();
                bool flying = false;
                foreach (var b in _wicket.Bails)
                    flying |= !b.isKinematic && b.linearVelocity.magnitude > 0.5f;
                Assert.IsTrue(flying, $"{mode} {kmh}: a bail is off within one physics step");
                float most = 0f;
                foreach (var s in _wicket.LastPlan.Stumps) most = Mathf.Max(most, s.Degrees);
                degrees[kmh] = Mathf.Max(degrees.TryGetValue(kmh, out float d) ? d : 0f, most);

                // The bails bounce about for a while: still exactly one result, and they never touch the ball.
                float until = Time.time + 2.5f;
                while (Time.time < until) yield return new WaitForFixedUpdate();
                Assert.AreEqual(1, _results.Count, $"{mode} {kmh}: exactly one result with the bails bouncing");
                foreach (var b in _wicket.Bails)
                    Assert.Greater(b.position.y, FD.PitchSurfaceHeight - 0.01f, "bails land on the pitch, not through it");
                Assert.IsTrue(_wicket.Broken, "still broken while the result shows");

                // The next launch starts from a whole wicket.
                yield return new WaitForFixedUpdate();
                _machine.Bowl(AtTheStumps(80f, 6f));
                AssertWicketWhole($"{mode} {kmh}: next ball");
                _ball.Cancel();
            }
            Assert.AreEqual(0, _ballDebrisContacts, "the ball never touches a bail");
            Debug.Log($"[CricketVR] Wicket reaction: most knock-back 80 km/h {degrees[80f]:F1} deg, 140 km/h {degrees[140f]:F1} deg");
            Assert.Greater(degrees[140f], degrees[80f], "the fast yorker knocks the stumps back further");
        }

        [UnityTest]
        public IEnumerator PlayedOnBreaksTheWicketThroughPhysicsInBothModes()
        {
            foreach (var mode in new[] { EnvironmentMode.NetMode, EnvironmentMode.GroundMode })
            {
                _score.SetEnvironment(mode);
                _wicket.SetMode(mode);
                _results.Clear();
                _reactions.Clear();
                yield return new WaitForFixedUpdate();
                // Off the bat at the crease and back onto the stumps (an inside edge).
                int id = mode == EnvironmentMode.NetMode ? 9001 : 9002;
                var from = new Vector3(0.05f, 0.35f, 1.0f);
                var v = new Vector3(-0.05f, 0f, -6f);
                _ball.Launch(id, from, v);
                yield return new WaitForFixedUpdate();
                _score.RecordBatContact(new BatContactReport { DeliveryId = id, Point = from, BallVelocityIn = -v, BallVelocityOut = v, HeightFromToe = 0.3f });
                float until = Time.time + 3f;
                while (Time.time < until && _results.Count == 0) yield return new WaitForFixedUpdate();
                Assert.AreEqual(1, _results.Count, $"{mode}: one result");
                var r = _score.Results[_score.Results.Count - 1];
                Assert.IsTrue(r.Bowled && r.BatContact, $"{mode}: played on ({r})");
                Assert.AreEqual(1, _reactions.Count, $"{mode}: the reaction fired");
                Assert.AreEqual(id, _reactions[0].id);
                _ball.Cancel();
                _wicket.ResetWicket();
            }
        }

        [UnityTest]
        public IEnumerator BallPathUpToTheStumpsIsTheSameWithTheReactionOnOrOff()
        {
            var paths = new List<List<Vector3>>();
            foreach (bool on in new[] { true, false })
            {
                _config.wicket.reaction = on;
                _results.Clear();
                var path = new List<Vector3>();
                bool atStumps = false;
                int stumps = LayerMask.NameToLayer("Stumps");
                System.Action<int, Collision> hit = (id, c) => { if (c.collider.gameObject.layer == stumps) atStumps = true; };
                _ball.Collided += hit;
                yield return new WaitForFixedUpdate();
                int bowled = _machine.Bowl(AtTheStumps(120f, 2.5f));
                float until = Time.time + 4f;
                while (!atStumps && Time.time < until)
                {
                    yield return new WaitForFixedUpdate();
                    path.Add(_ball.Body.position);
                }
                _ball.Collided -= hit;
                Assert.IsTrue(atStumps, "reached the stumps");
                paths.Add(path);
                while (_results.Count == 0 && Time.time < until) yield return new WaitForFixedUpdate();
                Assert.AreEqual(on, _wicket.Broken, on ? "reaction on" : "reaction off: nothing moves");
                _ball.Cancel();
                _wicket.ResetWicket();
            }
            Assert.AreEqual(paths[0].Count, paths[1].Count, "same number of steps to the stumps");
            for (int i = 0; i < paths[0].Count; i++)
                Assert.AreEqual(paths[0][i], paths[1][i], $"step {i}");
        }

        /// <summary>The T088 headset build's sequence: every ball bowled, and the stumps that move are the labelled ones.</summary>
        [UnityTest]
        public IEnumerator WicketCheckBallsHitTheStumpsTheyAreLabelledFor()
        {
            var types = DeliveryTypeData.All;
            var table = new System.Text.StringBuilder("[CricketVR] Wicket check sequence (right-handed):");
            for (int i = 0; i < DeliveryPlanner.WicketCheckBalls.Length; i++)
            {
                var spec = DeliveryPlanner.WicketCheckBalls[i];
                _results.Clear();
                _reactions.Clear();
                yield return BowlUntilResult(DeliveryPlanner.WicketCheckRequest(i, Handedness.Right, types));
                var r = _score.Results[_score.Results.Count - 1];
                Assert.IsTrue(r.Bowled, $"ball {i + 1} ({spec.label}): bowled ({r})");
                var plan = _wicket.LastPlan;
                var struck = new List<string>();
                for (int k = 0; k < 3; k++)
                    if (plan.Stumps[k].Struck) struck.Add($"{WicketImpact.NameOf(k, Handedness.Right)} {plan.Stumps[k].Degrees:F0} deg");
                table.Append($"\n  {i + 1,2}. {spec.label}: {string.Join(", ", struck)}; bails {(plan.Bails[0].Dislodged ? "+X " : "")}{(plan.Bails[1].Dislodged ? "-X" : "")}");
                bool Moved(int k) => plan.Stumps[k].Struck && plan.Stumps[k].Degrees > 3f;
                if (spec.label.Contains("middle") && !spec.label.Contains("between")) Assert.IsTrue(Moved(1), $"{spec.label}: middle moves");
                if (spec.label.Contains("on off stump") || spec.label.Contains("hitting off")) Assert.IsTrue(Moved(0), $"{spec.label}: off stump moves");
                if (spec.label.Contains("on leg stump")) Assert.IsTrue(Moved(2), $"{spec.label}: leg stump moves");
                if (spec.label.Contains("between")) Assert.IsTrue(plan.Stumps[0].Struck && plan.Stumps[1].Struck, $"{spec.label}: both");
                if (spec.label.Contains("grazing")) Assert.Less(Mathf.Max(plan.Stumps[0].Degrees, plan.Stumps[2].Degrees), 12f, $"{spec.label}: barely moves");
                Assert.IsTrue(plan.Bails[0].Dislodged || plan.Bails[1].Dislodged, $"{spec.label}: a bail comes off");
                _ball.Cancel();
                _wicket.ResetWicket();
            }
            Debug.Log(table.ToString());
        }

        /// <summary>
        /// REQ-052: the LEDs flash in Match Simulation (by default) and not in Net Practice, and the stumps' own material
        /// is back after the flash and after a reset. (The first headset build never showed the flash: E-069.)
        /// </summary>
        [UnityTest]
        public IEnumerator LedsFlashInMatchSimulationOnlyAndRestoreTheStumpMaterial()
        {
            var stumpRenderer = _wicket.Stumps[1].GetComponent<Renderer>();
            var wood = stumpRenderer.sharedMaterial;
            foreach (var (mode, expectLit) in new[] { (EnvironmentMode.NetMode, false), (EnvironmentMode.GroundMode, true) })
            {
                _wicket.SetMode(mode);
                Break(100 + (int)mode);
                bool sawLit = false, sawUnlitDuring = false;
                float until = Time.time + _config.wicket.ledSeconds;
                while (Time.time < until)
                {
                    yield return null;
                    if (stumpRenderer.sharedMaterial == _ledMaterial) sawLit = true;
                    else if (sawLit) sawUnlitDuring = true;
                }
                Assert.AreEqual(expectLit, sawLit, $"{mode}: LEDs lit");
                if (expectLit) Assert.IsTrue(sawUnlitDuring, "they flash (on and off), not just turn on");
                yield return new WaitForSeconds(0.3f);
                Assert.AreSame(wood, stumpRenderer.sharedMaterial, $"{mode}: the stump's own material after the flash");
                _wicket.ResetWicket();
            }
            // Reset during a flash puts the wood back at once.
            _wicket.SetMode(EnvironmentMode.GroundMode);
            Break(200);
            float t0 = Time.time;
            while (stumpRenderer.sharedMaterial != _ledMaterial && Time.time < t0 + 0.5f) yield return null;
            Assert.AreSame(_ledMaterial, stumpRenderer.sharedMaterial, "lit");
            _wicket.ResetWicket();
            Assert.AreSame(wood, stumpRenderer.sharedMaterial, "reset restores the wood immediately");
            Assert.IsFalse(_wicket.LedLit);
        }

        // ---------- AC-33 ----------

        IEnumerator StartAndWaitForABall(string mode = "NetPractice")
        {
            if (_session.Phase != SessionPhase.Menu) _session.Press("Menu");
            yield return null;
            _session.Press(mode);
            _session.Press("Overs1");
            _session.Press("Start");
            float until = Time.time + 10f;
            while (Time.time < until && !(_session.Phase == SessionPhase.Delivering && _ball.InFlight))
                yield return null;
            Assert.AreEqual(SessionPhase.Delivering, _session.Phase);
        }

        [UnityTest]
        public IEnumerator EveryWayOutOfADeliveryResetsTheWicket()
        {
            yield return null; // the menu appears
            foreach (var way in new[] { "PlayAgain", "Menu", "Pause", "ModeSwitch" })
            {
                yield return StartAndWaitForABall();
                Break(_ball.ActiveDeliveryId);
                Assert.IsTrue(_wicket.Broken, $"{way}: broken");
                yield return new WaitForSeconds(0.2f); // bails in the air, stumps tilted
                switch (way)
                {
                    case "PlayAgain": _session.Press("PlayAgain"); break;
                    case "Menu": _session.Press("Menu"); break;
                    case "Pause": _session.SimulateFocusLoss(); break;
                    case "ModeSwitch":
                        _wicket.SetMode(EnvironmentMode.GroundMode); // the switch itself resets it
                        AssertWicketWhole("mode switch");
                        _session.Press("Menu");
                        Break(-5);
                        _session.Press("MatchSim");
                        _session.Press("Start"); // and starting the other mode does too
                        break;
                }
                AssertWicketWhole(way);
                yield return new WaitForSeconds(0.3f);
                AssertWicketWhole($"{way}, a moment later");
                if (way == "Pause")
                {
                    _session.Press("Resume");
                    AssertWicketWhole("after resume");
                }
            }
        }

        [UnityTest]
        public IEnumerator BrokenWicketHoldsThroughTheResultThenResetsForTheNextBall()
        {
            yield return null;
            yield return StartAndWaitForABall();
            int first = _ball.ActiveDeliveryId;
            Break(first);
            // The next ball is called at once: the wicket waits out the hold, then resets before the launch.
            _wicket.ResetAfterHold();
            yield return new WaitForSeconds(1.0f);
            Assert.IsTrue(_wicket.Broken, "still broken 1 s after the wicket");
            yield return new WaitForSeconds(0.7f);
            AssertWicketWhole("after the 1.5 s hold");

            // Through the session: break the wicket on a ball, then the session's next ball finds it whole.
            Break(_ball.ActiveDeliveryId);
            float until = Time.time + 20f;
            int launchesBefore = 0;
            _machine.Bowled += (id, c, s) => launchesBefore++;
            while (Time.time < until && launchesBefore == 0) yield return null;
            Assert.Greater(launchesBefore, 0, "the session bowled the next ball");
            AssertWicketWhole("next ball of the session");
        }
    }
}
