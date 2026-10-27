using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CricketVR.Tests
{
    /// <summary>
    /// The pitch measurement in the real Main scene (its pitch, floor and net colliders), not the test rig: the
    /// T076 swing-check balls on device reported 1.4-1.5 cm at 140 km/h (E-051).
    /// </summary>
    public class MainScenePitchTests
    {
        const string ScenePath = "Assets/CricketVR/Scenes/Main.unity";

        /// <summary>Leave an empty scene behind: the other PlayMode tests build their rigs at the origin, where Main's pitch is.</summary>
        [UnityTearDown]
        public IEnumerator UnloadMain()
        {
            var empty = UnityEngine.SceneManagement.SceneManager.CreateScene("Empty" + Time.frameCount);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(empty);
            for (int i = UnityEngine.SceneManagement.SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene != empty)
                    yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
            }
        }

        /// <summary>Before the solver aimed at <see cref="FieldDimensions.PitchSurfaceHeight"/> these balls met the pitch
        /// 0.75 cm (120 km/h) to 1.02 cm (140 km/h) early (E-051, E-052).</summary>
        [UnityTest]
        public IEnumerator SwingCheckBallsPitchWithinTheStockToleranceInTheMainScene()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
            yield return null;
            var machine = Object.FindAnyObjectByType<BowlingMachine>();
            var ball = Object.FindAnyObjectByType<BallController>();
            var session = Object.FindAnyObjectByType<SessionManager>();
            Assert.IsNotNull(machine, "BowlingMachine in Main");
            if (session != null) session.enabled = false; // bowl directly; the menu is not needed
            var config = Resources.FindObjectsOfTypeAll<GameConfig>()[0];
            var types = config.delivery.TypeCatalogue();

            var pitches = new Dictionary<int, PitchMeasurement>();
            ball.Pitched += (id, p, t) => pitches[id] = p;
            float worst = 0f;
            for (int i = 0; i < 12; i++)
            {
                var request = DeliveryPlanner.SwingCheckRequest(i, Handedness.Right, types);
                yield return new WaitForFixedUpdate();
                int id = machine.Bowl(request);
                Assert.AreNotEqual(0, id, machine.LastError);
                var path = new List<string>();
                float until = Time.time + 3f;
                while (!pitches.ContainsKey(id) && Time.time < until)
                {
                    yield return new WaitForFixedUpdate();
                    var b = ball.Body;
                    path.Add($"({b.position.y:F4}, {b.position.z:F3}, vy {b.linearVelocity.y:F2})");
                }
                Assert.IsTrue(pitches.ContainsKey(id), $"{request}: never pitched");
                var world = request.ForBatter();
                var p = pitches[id];
                float error = Vector2.Distance(new Vector2(p.Point.x, p.Point.z), new Vector2(world.line, world.length));
                Debug.Log($"[CricketVR] Main scene {request}: pitch ({p.Point.y:F4}, {p.Point.z:F4}) error {error * 100f:F2} cm, " +
                          $"rebound {p.ReboundOffset * 100f:+0.0;-0.0} cm; last samples {string.Join(" ", path.GetRange(Mathf.Max(0, path.Count - 5), Mathf.Min(5, path.Count)))}");
                worst = Mathf.Max(worst, error);
                ball.Cancel();
            }
            Debug.Log($"[CricketVR] Main scene swing check: worst pitch error {worst * 100f:F2} cm");
            Assert.LessOrEqual(worst, BowlingPlayModeTests.StockTolerance);
        }
    }
}
