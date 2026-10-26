using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CricketVR.Tests
{
    /// <summary>Moves the stand-in controller each frame before BatController samples it.</summary>
    [DefaultExecutionOrder(-100)]
    public class ScriptedBatDriver : MonoBehaviour
    {
        public Func<float, Pose> Motion;
        public float StartTime;
        void Update()
        {
            if (Motion == null) return;
            var p = Motion(Time.time - StartTime);
            transform.SetPositionAndRotation(p.position, p.rotation);
        }
    }

    /// <summary>
    /// AC-05 scripted contact matrix (REQ-006, REQ-007): real BatController + BallController + resolver at 144 Hz
    /// physics and 72 Hz frames, with the controller driven by a script. Ten runs per case with small variations.
    /// Every run must produce exactly one bat contact (no tunnelling, no duplicate impulse) and a finite result.
    /// </summary>
    public class ContactMatrixPlayModeTests
    {
        const int Runs = 10;
        const float ContactTime = 0.35f; // seconds after the bat starts moving (history and suppression settled)

        GameObject _root;
        GameConfig _config;
        Transform _controller;
        ScriptedBatDriver _driver;
        BatController _bat;
        BallController _ball;
        BallFlight _flight;
        BatBallContactResolver _resolver;
        readonly List<BatContactReport> _contacts = new List<BatContactReport>();
        int _frameRate;

        static Quaternion GripToBat => BatFrame.GripRotation(BatHand.Right, 0f, 0f, 0f, false, 0f);

        [SetUp]
        public void SetUp()
        {
            _contacts.Clear();
            _frameRate = Application.targetFrameRate;
            Application.targetFrameRate = 72;
            _config = ScriptableObject.CreateInstance<GameConfig>();
            _config.contact.showContactMarker = false;
            _root = new GameObject("ContactRig");

            var left = new GameObject("Left Grip").transform;
            left.SetParent(_root.transform);
            _controller = new GameObject("Right Grip").transform;
            _controller.SetParent(_root.transform);
            _driver = _controller.gameObject.AddComponent<ScriptedBatDriver>();

            var batGo = new GameObject("Bat");
            batGo.SetActive(false);
            batGo.transform.SetParent(_root.transform);
            var visual = new GameObject("BatVisual").transform;
            visual.SetParent(batGo.transform);
            var proxyGo = new GameObject("BatPhysicsProxy");
            proxyGo.transform.SetParent(batGo.transform);
            var proxy = proxyGo.AddComponent<Rigidbody>();
            var box = proxyGo.AddComponent<BoxCollider>();
            _bat = batGo.AddComponent<BatController>();
            _bat.Configure(_config, left, _controller, visual, null, proxy, box, requireTracked: false);

            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.SetActive(false);
            ballGo.layer = LayerMask.NameToLayer("Ball");
            ballGo.transform.SetParent(_root.transform);
            ballGo.AddComponent<Rigidbody>().isKinematic = true;
            _ball = ballGo.AddComponent<BallController>();
            _ball.Configure(_config, null, null);
            _flight = ballGo.AddComponent<BallFlight>(); // idle unless a run starts a swinging flight

            var resolverGo = new GameObject("Resolver");
            resolverGo.SetActive(false);
            resolverGo.transform.SetParent(_root.transform);
            _resolver = resolverGo.AddComponent<BatBallContactResolver>();
            _resolver.Configure(_config, _bat, _ball, null);

            batGo.SetActive(true);
            ballGo.SetActive(true);
            resolverGo.SetActive(true);
            _resolver.BatContact += r => _contacts.Add(r);
        }

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = _frameRate;
            UnityEngine.Object.Destroy(_root);
            UnityEngine.Object.Destroy(_config);
        }

        /// <summary>Controller pose that puts the bat frame at <paramref name="batPosition"/>/<paramref name="batRotation"/>.</summary>
        static Pose ControllerFor(Vector3 batPosition, Quaternion batRotation) =>
            new Pose(batPosition, batRotation * Quaternion.Inverse(GripToBat));

        /// <summary>
        /// Drives the bat with <paramref name="batMotion"/> (bat-frame pose over time), launches a ball so its centre
        /// passes through <paramref name="aim"/> at <see cref="ContactTime"/>, and returns the contacts for that delivery.
        /// </summary>
        /// <summary>
        /// True if the bat's motion tracker saw a frame gap (> 0.1 s) since <paramref name="since"/>. A stalled
        /// batch-mode Editor frame makes the tracker suppress contacts, by design, so that run says nothing about
        /// contact detection and is repeated (at most <see cref="MaxStallRetries"/> times, each one logged).
        /// </summary>
        bool StalledSince(double since) =>
            _bat.Motion.LastResetReason == MotionResetReason.TimeGap && _bat.Motion.LastResetTime >= since;

        const int MaxStallRetries = 2;

        IEnumerator Run(Func<float, Pose> batMotion, Vector3 aim, float ballSpeed, List<BatContactReport> results, Action<string> fail,
            FlightPlan swing = default)
        {
            for (int attempt = 0; ; attempt++)
            {
                double started = Time.unscaledTimeAsDouble;
                string failure = null;
                yield return RunOnce(batMotion, aim, ballSpeed, results, f => failure = f, swing);
                if (failure == null)
                    yield break;
                if (attempt < MaxStallRetries && StalledSince(started))
                {
                    Debug.Log($"[CricketVR] AC-05 run repeated after an Editor frame stall: {failure}");
                    continue;
                }
                fail(failure);
                yield break;
            }
        }

        IEnumerator RunOnce(Func<float, Pose> batMotion, Vector3 aim, float ballSpeed, List<BatContactReport> results, Action<string> fail,
            FlightPlan swing)
        {
            int before = _contacts.Count;
            _driver.Motion = t => { var b = batMotion(t); return ControllerFor(b.position, b.rotation); };
            _driver.StartTime = Time.time;

            // Launch the ball 1.2 m out, timed to arrive at the aim point at ContactTime (gravity included).
            const float lead = 1.2f;
            float travel = lead / ballSpeed;
            while (Time.time - _driver.StartTime < ContactTime - travel)
                yield return null;
            float remaining = ContactTime - (Time.time - _driver.StartTime);
            var start = aim + new Vector3(0f, 0f, ballSpeed * remaining);
            var velocity = new Vector3(0f, 0.5f * -Physics.gravity.y * remaining, -ballSpeed);
            if (!swing.IsStill)
            {
                // Constant sideways acceleration (lateness 0) all the way to the bat: start off the aim by the stepped
                // drift, A·dt²·n(n+1)/2, so the ball still arrives at the aim point.
                float dt = Time.fixedDeltaTime, n = Mathf.Round(remaining / dt);
                start.x -= swing.SwingDirection * swing.SwingAcceleration * dt * dt * n * (n + 1f) * 0.5f;
            }
            _ball.Launch(_ball.ActiveDeliveryId + 1, start, velocity);
            int id = _ball.ActiveDeliveryId;
            if (!swing.IsStill)
                _flight.Begin(id, start, velocity, swing, DeliveryTypeData.Outswinger);

            // Watch until well past the bat.
            float watchUntil = Time.time + 0.4f;
            Vector3 last = start;
            while (Time.time < watchUntil && _ball.InFlight)
            {
                yield return new WaitForFixedUpdate();
                last = _ball.Body.position;
            }
            var mine = _contacts.GetRange(before, _contacts.Count - before);
            if (mine.Count != 1)
                fail($"delivery {id}: {mine.Count} contacts (expected exactly 1); ball ended at {last}");
            else
            {
                var v = mine[0].BallVelocityOut;
                if (!(float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)))
                    fail($"delivery {id}: non-finite exit velocity");
                results.Add(mine[0]);
            }
            _ball.Cancel();
            _driver.Motion = null;
            yield return null;
        }

        static Vector3 SweetPointWorld(Pose bat, BatGeometry g, float fromToe = 0.16f) =>
            bat.position + bat.rotation * new Vector3(0f, g.Toe.y + fromToe, g.bladeThickness * 0.5f);

        static float Mean(List<BatContactReport> r, Func<BatContactReport, float> f)
        {
            float s = 0f;
            foreach (var x in r) s += f(x);
            return r.Count > 0 ? s / r.Count : float.NaN;
        }

        static float Launch(Vector3 v) => Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;

        /// <summary>
        /// Straight bat, face toward the bowler, translating along Z at <paramref name="batSpeedZ"/>. Ball speeds
        /// 20/22/24 m/s unless <paramref name="ballSpeed"/> is given; <paramref name="swing"/> makes the ball swing.
        /// </summary>
        IEnumerator TranslatingCase(float batSpeedZ, Quaternion face, float fromToe, List<BatContactReport> results, List<string> failures,
            float ballSpeed = 0f, FlightPlan swing = default)
        {
            var g = _config.bat.geometry;
            for (int i = 0; i < Runs; i++)
            {
                float dx = (i % 5 - 2) * 0.01f, dy = (i / 5 - 0.5f) * 0.04f, speed = ballSpeed > 0f ? ballSpeed : 20f + (i % 3) * 2f;
                var grip = new Vector3(dx, 1.0f, 0.6f);
                Func<float, Pose> motion = t => new Pose(grip + new Vector3(0f, 0f, batSpeedZ * (t - ContactTime)), face);
                var aim = SweetPointWorld(motion(ContactTime), g, fromToe) + new Vector3(0f, dy, 0f) + face * Vector3.forward * 0.036f;
                aim.x = grip.x;
                yield return Run(motion, aim, speed, results, failures.Add, swing);
            }
        }

        /// <summary>Bat swinging in the vertical plane about the grip at 25 rad/s (see the rotation-dominated test).</summary>
        IEnumerator RotationCase(List<BatContactReport> results, List<string> failures, float ballSpeed = 0f, FlightPlan swing = default)
        {
            var g = _config.bat.geometry;
            for (int i = 0; i < Runs; i++)
            {
                const float omega = 25f;
                var grip = new Vector3((i % 5 - 2) * 0.01f, 1.05f, 0.6f);
                Func<float, Pose> motion = t => new Pose(grip, Quaternion.Euler(-omega * (t - ContactTime) * Mathf.Rad2Deg, 0f, 0f));
                var aim = SweetPointWorld(motion(ContactTime), g, 0.10f + (i / 5) * 0.04f) + Vector3.forward * 0.036f;
                aim.x = grip.x;
                yield return Run(motion, aim, ballSpeed > 0f ? ballSpeed : 20f + (i % 3) * 2f, results, failures.Add, swing);
            }
        }

        void AssertNoFailures(string label, List<string> failures, List<BatContactReport> results)
        {
            Debug.Log($"[CricketVR] AC-05 {label}: {results.Count}/{Runs} single contacts; mean exit {Mean(results, r => r.BallVelocityOut.magnitude):F1} m/s, " +
                      $"mean launch {Mean(results, r => Launch(r.BallVelocityOut)):F1} deg, mean sweet {Mean(results, r => r.Response.SweetFactor):F2}" +
                      (failures.Count > 0 ? $"; failures: {string.Join(" | ", failures)}" : ""));
            CollectionAssert.IsEmpty(failures, label);
        }

        [UnityTest]
        public IEnumerator StationaryBlockDeflectsBack()
        {
            var results = new List<BatContactReport>(); var failures = new List<string>();
            yield return TranslatingCase(0f, Quaternion.identity, 0.16f, results, failures);
            AssertNoFailures("stationary block", failures, results);
            foreach (var r in results)
                Assert.Greater(r.BallVelocityOut.z, 0f, "ball goes back toward the bowler");
        }

        [UnityTest]
        public IEnumerator SoftDefenceIsSlowerThanAnAttackingDrive()
        {
            var soft = new List<BatContactReport>(); var drive = new List<BatContactReport>(); var failures = new List<string>();
            yield return TranslatingCase(-2.5f, Quaternion.identity, 0.16f, soft, failures);
            yield return TranslatingCase(14f, Quaternion.identity, 0.16f, drive, failures);
            AssertNoFailures("soft defence (-2.5 m/s) + drive (14 m/s)", failures, soft);
            float softSpeed = Mean(soft, r => r.BallVelocityOut.magnitude), driveSpeed = Mean(drive, r => r.BallVelocityOut.magnitude);
            Debug.Log($"[CricketVR] AC-05 soft defence {softSpeed:F1} m/s vs drive {driveSpeed:F1} m/s");
            Assert.Less(softSpeed, driveSpeed * 0.5f, "a faster aligned attack sends the ball much faster than soft defence");
        }

        [UnityTest]
        public IEnumerator LoftedDriveLaunchesUpward()
        {
            var results = new List<BatContactReport>(); var failures = new List<string>();
            // Face opened 25 degrees upward.
            yield return TranslatingCase(12f, Quaternion.Euler(-25f, 0f, 0f), 0.16f, results, failures);
            AssertNoFailures("lofted drive", failures, results);
            Assert.Greater(Mean(results, r => Launch(r.BallVelocityOut)), 15f);
        }

        [UnityTest]
        public IEnumerator OffCentreToeContactIsWeakerThanTheMiddle()
        {
            var toe = new List<BatContactReport>(); var middle = new List<BatContactReport>(); var failures = new List<string>();
            yield return TranslatingCase(12f, Quaternion.identity, 0.03f, toe, failures);
            yield return TranslatingCase(12f, Quaternion.identity, 0.16f, middle, failures);
            AssertNoFailures("off-centre toe + middle", failures, toe);
            Assert.Less(Mean(toe, r => r.BallVelocityOut.magnitude), Mean(middle, r => r.BallVelocityOut.magnitude));
            Assert.Less(Mean(toe, r => r.Response.SweetFactor), 0.6f);
        }

        [UnityTest]
        public IEnumerator RotationDominatedSwingNeverTunnelsThroughTheBall()
        {
            // The bat swings in the vertical plane about the grip at 25 rad/s (toe ~16 m/s, ~20 degrees per frame),
            // passing vertical at ContactTime. The ball is aimed low on the blade, where rotation dominates.
            var results = new List<BatContactReport>(); var failures = new List<string>();
            yield return RotationCase(results, failures);
            AssertNoFailures("rotation-dominated swing (25 rad/s)", failures, results);
            Assert.Greater(Mean(results, r => r.BatPointVelocity.magnitude), 8f, "contact-point velocity includes rotation");
        }

        // ---------- AC-29 (M7.5 T075): pace up to 140 km/h, straight and swinging ----------

        /// <summary>The largest swing acceleration the solver produced over the T073 matrix (E-045), constant to the bat.</summary>
        static FlightPlan HardSwing(int direction) => new FlightPlan
        {
            SwingDirection = direction, SwingAcceleration = 150f, SwingLateness = 0f, PlannedTime = 1f,
        };

        [UnityTest]
        public IEnumerator FastBallsUpTo140KmhAreHitExactlyOnce()
        {
            foreach (var kmh in new[] { 120f, 140f })
            {
                float speed = Units.KmhToMs(kmh);
                var block = new List<BatContactReport>(); var drive = new List<BatContactReport>(); var rotation = new List<BatContactReport>();
                var failures = new List<string>();
                yield return TranslatingCase(0f, Quaternion.identity, 0.16f, block, failures, speed);
                yield return TranslatingCase(14f, Quaternion.identity, 0.16f, drive, failures, speed);
                yield return RotationCase(rotation, failures, speed);
                AssertNoFailures($"{kmh:F0} km/h straight: block {block.Count}, drive {drive.Count}, rotation {rotation.Count}", failures, drive);
                Assert.AreEqual(Runs, block.Count);
                Assert.AreEqual(Runs, rotation.Count);
                foreach (var r in block)
                    Assert.Greater(r.BallVelocityOut.z, 0f, $"{kmh:F0} km/h block goes back toward the bowler");
            }
        }

        [UnityTest]
        public IEnumerator SwingingBallsAt140KmhAreHitExactlyOnce()
        {
            float speed = Units.KmhToMs(140f);
            foreach (var direction in new[] { -1, 1 })
            {
                var drive = new List<BatContactReport>(); var rotation = new List<BatContactReport>(); var failures = new List<string>();
                yield return TranslatingCase(14f, Quaternion.identity, 0.16f, drive, failures, speed, HardSwing(direction));
                yield return RotationCase(rotation, failures, speed, HardSwing(direction));
                AssertNoFailures($"140 km/h swinging {(direction < 0 ? "in" : "out")} at 150 m/s^2: drive {drive.Count}, rotation {rotation.Count}", failures, drive);
                Assert.AreEqual(Runs, rotation.Count);
                Debug.Log($"[CricketVR] AC-29 swing {direction:+0;-0}: sideways ball speed at contact {Mean(drive, r => Mathf.Abs(r.BallVelocityIn.x)):F1} m/s");
                Assert.Greater(Mean(drive, r => Mathf.Abs(r.BallVelocityIn.x)), 1f, "the ball was still swinging at contact");
            }
        }

        [UnityTest]
        public IEnumerator BallStartingInsideTheBladeIsResolvedOnce()
        {
            var results = new List<BatContactReport>(); var failures = new List<string>();
            var g = _config.bat.geometry;
            int stallRetries = 0;
            for (int i = 0; i < Runs; i++)
            {
                double started = Time.unscaledTimeAsDouble;
                var grip = new Vector3((i % 5 - 2) * 0.01f, 1.0f, 0.6f);
                Func<float, Pose> motion = t => new Pose(grip, Quaternion.identity);
                _driver.Motion = t => ControllerFor(motion(t).position, motion(t).rotation);
                _driver.StartTime = Time.time;
                while (Time.time - _driver.StartTime < ContactTime)
                    yield return null;
                int before = _contacts.Count;
                var inside = SweetPointWorld(motion(0f), g) - new Vector3(0f, 0f, 0.01f); // 1 cm inside the face
                _ball.Launch(_ball.ActiveDeliveryId + 1, inside, new Vector3(0f, 0f, -20f));
                for (int k = 0; k < 20; k++) yield return new WaitForFixedUpdate();
                int count = _contacts.Count - before;
                if (count != 1 && stallRetries < MaxStallRetries && StalledSince(started))
                {
                    Debug.Log($"[CricketVR] AC-05 initial overlap run {i} repeated after an Editor frame stall ({count} contacts)");
                    stallRetries++;
                    _ball.Cancel();
                    i--;
                    continue;
                }
                if (count != 1) failures.Add($"run {i}: {count} contacts");
                else
                {
                    results.Add(_contacts[_contacts.Count - 1]);
                    if (!_contacts[_contacts.Count - 1].Sweep.InitialOverlap) failures.Add($"run {i}: overlap not reported");
                    if (_ball.Body.position.z < inside.z) failures.Add($"run {i}: ball ended behind the face");
                }
                _ball.Cancel();
            }
            AssertNoFailures("initial overlap", failures, results);
        }

        [UnityTest]
        public IEnumerator ContactIsIgnoredWhileBatMotionIsSuppressed()
        {
            // A recentre (origin change) just before the ball arrives: contact must not be applied.
            var g = _config.bat.geometry;
            var grip = new Vector3(0f, 1.0f, 0.6f);
            var still = new Pose(grip, Quaternion.identity);
            var aim = SweetPointWorld(still, g) + Vector3.forward * 0.036f;
            bool reset = false;
            Func<float, Pose> motion = t =>
            {
                if (!reset && t > ContactTime - 0.03f)
                {
                    reset = true;
                    _bat.ResetMotion(MotionResetReason.OriginChanged);
                }
                return still;
            };
            int before = _contacts.Count;
            yield return Run(motion, aim, 22f, new List<BatContactReport>(), _ => { });
            Assert.AreEqual(before, _contacts.Count, "no bat contact may be applied while motion is suppressed");
            Assert.IsTrue(reset);
        }
    }
}
