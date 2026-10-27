using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FD = CricketVR.FieldDimensions;

namespace CricketVR.Tests
{
    /// <summary>
    /// M7 session framework with real physics (AC-07/08/09 automated part): the real BowlingMachine, BallController,
    /// ScoreManager and SessionManager (no XR, no UI objects) on a pitch with a back net. Unhit balls end as a Miss
    /// at the back net. Time runs 5x faster; the physics step is unchanged.
    /// </summary>
    public class SessionPlayModeTests
    {
        GameObject _root;
        GameConfig _config;
        BallController _ball;
        SessionManager _session;
        float _timeScale;
        readonly List<int> _launches = new List<int>();
        readonly List<(int id, BallEndReason reason)> _ends = new List<(int, BallEndReason)>();
        readonly List<Object> _owned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _launches.Clear();
            _ends.Clear();
            _owned.Clear();
            _timeScale = Time.timeScale;
            Time.timeScale = 5f;
            _config = ScriptableObject.CreateInstance<GameConfig>();
            _config.session.timings = new SessionTimings { firstCountdown = 1f, countdown = 1f, betweenBalls = 1f };
            _config.session.logBallsCsv = false;
            _root = new GameObject("SessionTestRig");

            PhysicsMaterial pitch = null;
#if UNITY_EDITOR
            pitch = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/CricketVR/Settings/Physics/PitchSurface.physicMaterial"));
#endif
            _owned.Add(pitch);
            Box("Ground", "Pitch", new Vector3(0f, FD.PitchSurfaceHeight - 0.5f, 10f), new Vector3(20f, 1f, 40f)).GetComponent<Collider>().sharedMaterial = pitch;
            var rear = Box("RearWall", "Boundary", new Vector3(0f, 2f, FD.NetRearZ - 0.05f), new Vector3(3.6f, 4f, 0.1f));
            rear.AddComponent<NetWall>().side = WallSide.Rear;

            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.SetActive(false);
            ballGo.name = "Ball";
            ballGo.layer = LayerMask.NameToLayer("Ball");
            ballGo.transform.SetParent(_root.transform);
            ballGo.transform.position = FD.DefaultReleasePoint;
            ballGo.AddComponent<Rigidbody>().isKinematic = true;
            _ball = ballGo.AddComponent<BallController>();
            _ball.Configure(_config, null, ballGo.GetComponent<Renderer>());
            ballGo.AddComponent<BallFlight>(); // as in the Main scene: measures the arrival line and crease height

            var gameplay = new GameObject("Gameplay");
            gameplay.SetActive(false);
            gameplay.transform.SetParent(_root.transform);
            var machine = gameplay.AddComponent<BowlingMachine>();
            machine.Configure(_config, _ball, null, null, null);
            var score = gameplay.AddComponent<ScoreManager>();
            score.Configure(_config, _ball, null, machine);
            var ui = gameplay.AddComponent<SessionUI>();
            _session = gameplay.AddComponent<SessionManager>();
            _session.Configure(_config, machine, _ball, score, null, null, null, null, null, null, ui, null, null, null);

            ballGo.SetActive(true);
            gameplay.SetActive(true);
            machine.Bowled += (id, c, s) => _launches.Add(id);
            _ball.DeliveryEnded += (id, r) => _ends.Add((id, r));
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

        IEnumerator WaitFor(System.Func<bool> condition, float maxGameSeconds)
        {
            float until = Time.time + maxGameSeconds;
            while (Time.time < until && !condition())
                yield return null;
        }

        /// <summary>Owner request 2026-10-27: Net Practice can bowl off-spin or leg-spin for the whole session.</summary>
        [UnityTest]
        public IEnumerator NetPracticeBowlsTheChosenSpinner()
        {
            foreach (var (button, type, turn) in new[] { ("BowlerOffSpin", "Off-spin", -1), ("BowlerLegSpin", "Leg-spin", +1) })
            {
                yield return null;
                if (_session.Phase != SessionPhase.Menu) _session.Press("Menu");
                yield return null;
                _session.Press("NetPractice");
                _session.Press(button);
                _session.Press("Overs1");
                _session.Press("Start");
                yield return WaitFor(() => _session.Phase == SessionPhase.SessionEnd, 90f);
                Assert.AreEqual(SessionPhase.SessionEnd, _session.Phase, $"{type}: stuck in {_session.Phase}");
                Assert.AreEqual(6, _session.Session.Records.Count);
                foreach (var r in _session.Session.Records)
                {
                    Assert.AreEqual(type, r.DeliveryType);
                    Assert.AreEqual(turn, (int)Mathf.Sign(r.TurnDegrees), $"{type} turns toward {turn} for a right-hander ({r.TurnDegrees:F1} deg)");
                    Assert.That(r.CreaseHeight, Is.InRange(0.1f, 1.5f), $"{type}: crease height");
                    Assert.AreEqual(r.Delivery.length, r.Bounce.z, BowlingPlayModeTests.StockTolerance, $"{type}: pitched on its length");
                }
            }
        }

        [UnityTest]
        public IEnumerator OneOverBowlsExactlySixBallsThenShowsTheEnd()
        {
            yield return null; // Start() shows the menu
            Assert.AreEqual(SessionPhase.Menu, _session.Phase);
            _session.Press("Overs1");
            _session.Press("Start");
            yield return WaitFor(() => _session.Phase == SessionPhase.SessionEnd, 90f);
            Assert.AreEqual(SessionPhase.SessionEnd, _session.Phase, $"stuck in {_session.Phase} after {_session.Session.Records.Count} balls");
            Assert.AreEqual(6, _session.Session.Records.Count);
            Assert.AreEqual(6, _launches.Count, "exactly six launches");
            yield return WaitFor(() => false, 5f);
            Assert.AreEqual(6, _launches.Count, "no seventh ball after the end");
            var ids = new HashSet<int>();
            foreach (var r in _session.Session.Records)
            {
                Assert.IsTrue(ids.Add(r.DeliveryId), "one record per delivery");
                Assert.AreEqual(DeliveryOutcome.Miss, r.Result.Outcome, "nobody batting: every ball is a miss");
                Assert.AreEqual(SessionMode.NetPractice, r.Mode);
                Assert.IsFalse(string.IsNullOrEmpty(r.Delivery.label));
                // T072: the delivery-type columns are filled from the request and the measured bounce.
                Assert.AreEqual("Stock", r.DeliveryType);
                Assert.AreEqual(0f, r.Movement);
                Assert.AreEqual(r.Delivery.length, r.Bounce.z, BowlingPlayModeTests.StockTolerance, "measured bounce recorded");
                Assert.That(r.CreaseHeight, Is.InRange(0.1f, 2f), "crease height recorded for an unhit ball");
                Assert.LessOrEqual(r.BounceError, BowlingPlayModeTests.StockTolerance, $"{r.Delivery} pitched within the Stock tolerance");
            }
        }

        /// <summary>Default behaviour: each ball comes only when the batter presses B (owner decision 2026-10-26).</summary>
        [UnityTest]
        public IEnumerator EachBallWaitsForTheReadyPress()
        {
            _config.session.timings = new SessionTimings { waitForReady = true, readyDelay = 0f, betweenBalls = 1f };
            var gameplay = _session.gameObject;
            gameplay.SetActive(false); // re-create the session with the new timings
            Object.DestroyImmediate(_session);
            _session = gameplay.AddComponent<SessionManager>();
            _session.Configure(_config, gameplay.GetComponent<BowlingMachine>(), _ball, gameplay.GetComponent<ScoreManager>(),
                null, null, null, null, null, null, gameplay.GetComponent<SessionUI>(), null, null, null);
            gameplay.SetActive(true);
            yield return null;
            _session.Press("Overs1");
            _session.Press("Start");
            for (int ball = 0; ball < 6; ball++)
            {
                yield return WaitFor(() => false, 4f);
                Assert.AreEqual(ball, _launches.Count, $"ball {ball + 1} must wait for the press");
                Assert.AreEqual(SessionPhase.AwaitingReady, _session.Phase);
                _session.PressReady();
                yield return WaitFor(() => _launches.Count == ball + 1, 1f);
                Assert.AreEqual(ball + 1, _launches.Count, $"ball {ball + 1} bowled straight after the press");
                yield return WaitFor(() => _session.Session.Records.Count == ball + 1, 15f);
            }
            _session.PressReady();
            yield return WaitFor(() => _session.Phase == SessionPhase.SessionEnd, 5f);
            Assert.AreEqual(SessionPhase.SessionEnd, _session.Phase);
            Assert.AreEqual(6, _launches.Count, "a press after the last ball bowls nothing");
        }

        [UnityTest]
        public IEnumerator FocusLossMidDeliveryCancelsItAndTheBallIsBowledAgainAfterResume()
        {
            yield return null;
            _session.Press("Overs1");
            _session.Press("Start");
            yield return WaitFor(() => _session.Phase == SessionPhase.Delivering && _ball.InFlight, 10f);
            int interrupted = _ball.ActiveDeliveryId;
            _session.SimulateFocusLoss();
            Assert.AreEqual(SessionPhase.Paused, _session.Phase);
            Assert.IsFalse(_ball.InFlight, "the ball in flight is removed");
            Assert.Contains((interrupted, BallEndReason.Cancelled), _ends);
            yield return WaitFor(() => false, 5f);
            Assert.AreEqual(SessionPhase.Paused, _session.Phase, "no automatic resume");
            Assert.AreEqual(1, _launches.Count, "nothing bowled while paused");
            Assert.AreEqual(0, _session.Session.Records.Count, "the interrupted ball was not consumed");

            _session.Press("Resume");
            yield return WaitFor(() => _session.Session.Records.Count == 1, 15f);
            Assert.AreEqual(1, _session.Session.Records.Count);
            Assert.AreNotEqual(interrupted, _session.Session.Records[0].DeliveryId, "a new delivery for ball 1");
        }

        [UnityTest]
        public IEnumerator PlayAgainAndMenuLeaveNoStaleBallOrCallback()
        {
            yield return null;
            _session.Press("Overs1");
            for (int round = 0; round < 5; round++)
            {
                _session.Press("Start");
                yield return WaitFor(() => _session.Phase == SessionPhase.Delivering && _ball.InFlight, 10f);
                int stale = _ball.ActiveDeliveryId;
                _session.Press(round % 2 == 0 ? "PlayAgain" : "Menu");
                Assert.IsFalse(_ball.InFlight, $"round {round}: ball removed");
                Assert.AreEqual(0, _session.Session.Records.Count, $"round {round}: records cleared");
                yield return WaitFor(() => false, 1.5f);
                foreach (var r in _session.Session.Records)
                    Assert.AreNotEqual(stale, r.DeliveryId, $"round {round}: stale result recorded");
                if (_session.Phase != SessionPhase.Menu)
                    _session.Press("Menu");
                Assert.AreEqual(SessionPhase.Menu, _session.Phase);
            }
        }
    }
}
