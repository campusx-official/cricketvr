using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace CricketVR.Tests
{
    /// <summary>M7.5 T071 / AC-25: delivery types, the one request path, and Stock unchanged.</summary>
    public class DeliveryTypeTests
    {
        static readonly System.Func<DeliveryConfig, bool> Solver = DeliveryPlanner.SolverCheck(9.81f, 1f / 144f, 0.036f);

        static IEnumerable<DeliveryPlanSettings> StockPlans()
        {
            yield return DeliveryPlanSettings.NetPractice;
            yield return DeliveryPlanSettings.Easy;
            yield return DeliveryPlanSettings.Medium;
            yield return DeliveryPlanSettings.Hard;
        }

        [Test]
        public void StartingTypesAreValidAndInKindOrder()
        {
            var all = DeliveryTypeData.All;
            CollectionAssert.AreEqual(System.Enum.GetValues(typeof(DeliveryKind)), all.Select(t => t.kind).ToArray());
            foreach (var t in all)
                CollectionAssert.IsEmpty(t.Validate(), t.displayName);
            Assert.IsTrue(DeliveryTypeData.Stock.IsStill, "Stock has no movement");
            Assert.IsTrue(all.Skip(1).All(t => !t.IsStill), "every other type moves");
        }

        [Test]
        public void ShippedTypeAssetsAreInRange()
        {
#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets("t:DeliveryType", new[] { "Assets/CricketVR/Settings/DeliveryTypes" });
            var assets = guids.Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<DeliveryType>(UnityEditor.AssetDatabase.GUIDToAssetPath(g))).ToList();
            Assert.AreEqual(DeliveryTypeData.All.Length, assets.Count, "one asset per type; run the scene builder");
            CollectionAssert.AreEquivalent(System.Enum.GetValues(typeof(DeliveryKind)), assets.Select(a => a.data.kind).ToArray());
            foreach (var a in assets)
                CollectionAssert.IsEmpty(a.data.Validate(), a.name);
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/CricketVR/Settings/GameConfig.asset");
            Assert.AreEqual(DeliveryTypeData.All.Length, config.delivery.deliveryTypes.Count(t => t != null), "GameConfig references every type");
#endif
        }

        [Test]
        public void ValidationCatchesOutOfRangeValues()
        {
            var t = DeliveryTypeData.LegSpin;
            t.turnMax = 20f;
            t.visualRevPerSecond = 25f;
            t.driftMin = 0.2f; // above driftMax
            var errors = t.Validate();
            Assert.IsTrue(errors.Any(e => e.Contains("turnMax")));
            Assert.IsTrue(errors.Any(e => e.Contains("visualRevPerSecond")));
            Assert.IsTrue(errors.Any(e => e.Contains("driftMin > driftMax")));
        }

        [Test]
        public void StockRequestIsTheExistingConfig()
        {
            var config = DeliveryConfig.Create("Good length", 80f, 0.1f, 6f);
            var request = DeliveryRequest.FromConfig(config);
            Assert.IsTrue(request.type.IsStill);
            Assert.AreEqual(0f, request.movement);
            Assert.AreEqual(Handedness.Right, request.handedness);
            var back = request.ForBatter().ToConfig();
            Assert.AreEqual(config.label, back.label);
            Assert.AreEqual(config.speedKmh, back.speedKmh);
            Assert.AreEqual(config.releasePosition, back.releasePosition);
            Assert.AreEqual(config.line, back.line);
            Assert.AreEqual(config.length, back.length);
            Assert.AreEqual(config.ToString(), request.ToString());

            var a = DeliverySolver.Solve(config, 9.81f, 1f / 144f, 0.036f);
            var b = DeliverySolver.Solve(back, 9.81f, 1f / 144f, 0.036f);
            Assert.AreEqual(a.Velocity, b.Velocity, "same launch");
        }

        [Test]
        public void LeftHandedRequestsMirrorLineAndMovementExactly()
        {
            foreach (var type in DeliveryTypeData.All)
            {
                var right = new DeliveryRequest
                {
                    label = type.displayName, type = type, speedKmh = 85f, releasePosition = FieldDimensions.DefaultReleasePoint,
                    line = 0.17f, length = 5.5f, movement = 0.4f, seed = 99, handedness = Handedness.Right,
                };
                var left = right;
                left.handedness = Handedness.Left;
                var r = right.ForBatter();
                var l = left.ForBatter();
                Assert.AreEqual(right.line, r.line, "right-hander: world = batter frame");
                Assert.AreEqual(-r.line, l.line, type.displayName);
                Assert.AreEqual(-r.type.swingDirection, l.type.swingDirection, type.displayName);
                Assert.AreEqual(-r.type.turnDirection, l.type.turnDirection, type.displayName);
                Assert.AreEqual(r.type.swingMax, l.type.swingMax);
                Assert.AreEqual(r.type.turnMax, l.type.turnMax);
                Assert.AreEqual(r.type.dip, l.type.dip);
                Assert.AreEqual(r.speedKmh, l.speedKmh);
                Assert.AreEqual(r.length, l.length);
                Assert.AreEqual(r.movement, l.movement);
                Assert.AreEqual(r.seed, l.seed);
                var twice = l.ForBatter();
                Assert.AreEqual(l.line, twice.line, "mirroring is applied once");
                Assert.AreEqual(l.type.turnDirection, twice.type.turnDirection, "mirroring is applied once");
            }
        }

        /// <summary>The pre-M7.5 draw (M7, E-036), reproduced verbatim, so Stock-only plans are proven unchanged.</summary>
        static DeliveryConfig LegacyDraw(in DeliveryPlanSettings plan, System.Random rng, bool leftHanded)
        {
            float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            float u = (float)rng.NextDouble();
            string kind;
            float length;
            if (u < plan.yorkerShare) { kind = "Yorker"; length = Range(plan.yorkerMinLength, plan.yorkerMaxLength); }
            else if (u < plan.yorkerShare + plan.shortShare) { kind = "Short"; length = Range(plan.shortMinLength, plan.shortMaxLength); }
            else
            {
                length = Range(plan.minLength, plan.maxLength);
                kind = length < 5f ? "Full" : length < 7.5f ? "Good length" : "Back of a length";
            }
            float speed = Mathf.Round(Range(plan.minSpeedKmh, plan.maxSpeedKmh));
            float line = Range(plan.minLine, plan.maxLine);
            if (leftHanded) line = -line;
            return DeliveryConfig.Create($"{plan.label}: {kind}", speed, line, length);
        }

        [Test]
        public void StockOnlyPlansDrawExactlyThePreM75Sequence()
        {
            foreach (var plan in StockPlans())
            foreach (var hand in new[] { Handedness.Right, Handedness.Left })
            {
                var legacy = new System.Random(5);
                var current = new System.Random(5);
                for (int i = 0; i < 1000; i++)
                {
                    var expected = LegacyDraw(plan, legacy, hand == Handedness.Left);
                    var request = DeliveryPlanner.DrawRequest(plan, current, hand, null, null, out _);
                    Assert.IsTrue(request.type.IsStill);
                    Assert.AreEqual(hand, request.handedness);
                    var got = request.ForBatter().ToConfig();
                    Assert.AreEqual(expected.ToString(), got.ToString(), $"{plan.label} {hand} draw {i}");
                    Assert.AreEqual(expected.line, got.line, $"{plan.label} {hand} draw {i}");
                    Assert.AreEqual(expected.length, got.length);
                }
            }
        }

        [Test]
        public void TypeMixDrawsEachTypeWithItsOwnSpeedsAndThePaceCap()
        {
            var plan = DeliveryPlanSettings.NetPractice;
            plan.typeMix = DeliveryTypeData.All.Select(t => new TypeWeight { kind = t.kind, weight = 1f }).ToArray();
            var rng = new System.Random(21);
            var counts = new Dictionary<DeliveryKind, int>();
            int draws = 1000 * DeliveryTypeData.All.Length;
            for (int i = 0; i < draws; i++)
            {
                var r = DeliveryPlanner.DrawRequest(plan, rng, Handedness.Left, null, null, out _);
                counts[r.type.kind] = counts.TryGetValue(r.type.kind, out int n) ? n + 1 : 1;
                Assert.That(r.line, Is.InRange(plan.minLine, plan.maxLine), "requests stay in the batter's frame");
                Assert.AreEqual(Handedness.Left, r.handedness);
                if (r.type.UsesPlanSpeed)
                    Assert.That(r.speedKmh, Is.InRange(plan.minSpeedKmh, plan.maxSpeedKmh), r.type.displayName);
                else if (r.type.style == BowlingStyle.Pace)
                    Assert.That(r.speedKmh, Is.InRange(r.type.minSpeedKmh, DeliveryPlanSettings.DefaultPaceCapKmh), $"{r.type.displayName} capped (DT-5)");
                else
                    Assert.That(r.speedKmh, r.flighted ? Is.InRange(r.type.flightedMinKmh, r.type.flightedMaxKmh) : Is.InRange(r.type.minSpeedKmh, r.type.maxSpeedKmh), r.type.displayName);
                if (!r.type.IsStill)
                    Assert.That(r.movement, Is.InRange(0f, 1f));
            }
            foreach (DeliveryKind kind in System.Enum.GetValues(typeof(DeliveryKind)))
                Assert.AreEqual(1000f, counts[kind], 100f, $"{kind} share of an equal mix");

            var a = DeliveryPlanner.DrawRequest(plan, new System.Random(8), Handedness.Right, null, null, out _);
            var b = DeliveryPlanner.DrawRequest(plan, new System.Random(8), Handedness.Right, null, null, out _);
            Assert.AreEqual(a.ToString(), b.ToString(), "same seed, same request");
            Assert.AreEqual(a.seed, b.seed);
        }

        [Test]
        public void StockOnlyPlansStayReachable()
        {
            foreach (var plan in StockPlans())
            {
                var rng = new System.Random(2);
                for (int i = 0; i < 200; i++)
                {
                    var r = DeliveryPlanner.DrawRequest(plan, rng, Handedness.Right, null, x => Solver(x.ForBatter().ToConfig()), out _);
                    Assert.IsTrue(Solver(r.ForBatter().ToConfig()), r.ToString());
                }
            }
        }

        /// <summary>
        /// AC-25: no code outside the delivery-type module branches on a specific type. Behaviour must come from the
        /// type's numbers, so a new or re-tuned type needs no code change elsewhere.
        /// </summary>
        [Test]
        public void NoCodeOutsideTheModuleBranchesOnADeliveryType()
        {
            var module = new HashSet<string> { "DeliveryTypes.cs", "DeliveryType.cs", "DeliveryFlightModel.cs", "BallFlight.cs" };
            var branching = new Regex(@"(==|!=)\s*DeliveryKind\.|DeliveryKind\.\w+\s*(==|!=)|case\s+DeliveryKind\.|is\s+DeliveryKind\.");
            var offenders = new List<string>();
            foreach (var path in Directory.GetFiles("Assets/CricketVR/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                if (module.Contains(Path.GetFileName(path))) continue;
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    if (branching.IsMatch(lines[i]))
                        offenders.Add($"{path}:{i + 1}: {lines[i].Trim()}");
            }
            CollectionAssert.IsEmpty(offenders, "type-specific code outside the module");
        }

        /// <summary>Owner decision 2026-10-27: Easy and Medium swing about 0.15-0.3 m, Hard up to 0.5 m, nothing over 0.5 m.</summary>
        [Test]
        public void DifficultySwingRangesFollowTheOwnersSizes()
        {
            foreach (var (preset, lo, hi) in new[]
                     {
                         (DeliveryPlanSettings.Easy, 0.15f, 0.30f), (DeliveryPlanSettings.Medium, 0.15f, 0.30f),
                         (DeliveryPlanSettings.Hard, 0.20f, 0.50f),
                     })
            {
                var plan = preset;
                plan.typeMix = new[]
                {
                    new TypeWeight { kind = DeliveryKind.Inswinger, weight = 1f }, new TypeWeight { kind = DeliveryKind.Outswinger, weight = 1f },
                };
                var rng = new System.Random(5);
                float min = float.MaxValue, max = 0f;
                for (int i = 0; i < 500; i++)
                {
                    var request = DeliveryPlanner.DrawRequest(plan, rng, i % 2 == 0 ? Handedness.Right : Handedness.Left, null, null, out _);
                    var nominal = request;
                    nominal.type.variation = 0f;
                    float swing = DeliveryMovement.For(nominal.ForBatter()).Swing;
                    Assert.That(swing, Is.InRange(lo - 1e-5f, hi + 1e-5f), $"{plan.label}: nominal swing");
                    Assert.LessOrEqual(DeliveryMovement.For(request.ForBatter()).Swing, DeliveryTypeData.MaxSwing, $"{plan.label}: varied swing");
                    min = Mathf.Min(min, swing);
                    max = Mathf.Max(max, swing);
                }
                Assert.Less(min, lo + 0.01f, $"{plan.label} uses its whole range");
                Assert.Greater(max, hi - 0.01f, $"{plan.label} uses its whole range");
            }
        }

        // ---------- T072a natural variation (AC-31) ----------

        static DeliveryRequest Moving(DeliveryTypeData type, int seed, float movement = 0.5f, Handedness hand = Handedness.Right) => new DeliveryRequest
        {
            label = type.displayName, type = type, speedKmh = 85f, releasePosition = FieldDimensions.DefaultReleasePoint,
            line = 0.1f, length = 5f, movement = movement, seed = seed, handedness = hand,
        };

        [Test]
        public void SameSeedGivesTheSameMovementAndFactors()
        {
            foreach (var type in DeliveryTypeData.All)
            {
                var a = DeliveryMovement.For(Moving(type, 12345).ForBatter());
                var b = DeliveryMovement.For(Moving(type, 12345).ForBatter());
                Assert.AreEqual(a.VarSwing, b.VarSwing);
                Assert.AreEqual(a.VarDrift, b.VarDrift);
                Assert.AreEqual(a.VarTurn, b.VarTurn);
                Assert.AreEqual(a.Swing, b.Swing);
                Assert.AreEqual(a.Drift, b.Drift);
                Assert.AreEqual(a.TurnDegrees, b.TurnDegrees);
            }
            var c = DeliveryMovement.For(Moving(DeliveryTypeData.LegSpin, 12346).ForBatter());
            var d = DeliveryMovement.For(Moving(DeliveryTypeData.LegSpin, 12345).ForBatter());
            Assert.AreNotEqual(c.VarTurn, d.VarTurn, "a different seed varies the ball");
        }

        [Test]
        public void FactorsStayWithinTheVariationAndSpreadAcrossIt()
        {
            const float v = 0.15f;
            var buckets = new int[3, 4];
            for (int seed = 0; seed < 1000; seed++)
            {
                var f = DeliveryMovement.VariationFactors(seed * 7919 + 1, v);
                for (int k = 0; k < 3; k++)
                {
                    Assert.That(f[k], Is.InRange(1f - v, 1f + v), $"seed {seed} factor {k}");
                    buckets[k, Mathf.Clamp((int)((f[k] - (1f - v)) / (2f * v) * 4f), 0, 3)]++;
                }
            }
            for (int k = 0; k < 3; k++)
            for (int q = 0; q < 4; q++)
                Assert.Greater(buckets[k, q], 180, $"factor {k} quarter {q} of the range: {buckets[k, q]} of 1000");
        }

        [Test]
        public void ZeroVariationGivesExactlyTheNominalMovement()
        {
            foreach (var type in DeliveryTypeData.All)
            {
                var t = type;
                t.variation = 0f;
                for (float m = 0f; m <= 1f; m += 0.25f)
                {
                    var mv = DeliveryMovement.For(Moving(t, 999, m).ForBatter());
                    Assert.AreEqual(1f, mv.VarSwing);
                    Assert.AreEqual(1f, mv.VarDrift);
                    Assert.AreEqual(1f, mv.VarTurn);
                    Assert.AreEqual(t.swingDirection == 0 ? 0f : t.swingMin + m * (t.swingMax - t.swingMin), mv.Swing, 1e-6f, t.displayName);
                    Assert.AreEqual(t.turnDirection == 0 ? 0f : t.driftMin + m * (t.driftMax - t.driftMin), mv.Drift, 1e-6f, t.displayName);
                    Assert.AreEqual(t.turnDirection == 0 ? 0f : t.turnMin + m * (t.turnMax - t.turnMin), mv.TurnDegrees, 1e-6f, t.displayName);
                }
            }
            Assert.IsTrue(DeliveryMovement.For(DeliveryRequest.FromConfig(DeliveryConfig.Create("s", 80f, 0f, 6f)).ForBatter()).IsStill, "Stock does not move");
        }

        [Test]
        public void VariationScalesOnlySwingDriftAndTurnAndKeepsTheirSigns()
        {
            var t = DeliveryTypeData.Googly;
            var nominal = t; nominal.variation = 0f;
            for (int seed = 0; seed < 50; seed++)
            {
                var left = DeliveryMovement.For(Moving(t, seed, 0.5f, Handedness.Left).ForBatter());
                var right = DeliveryMovement.For(Moving(t, seed, 0.5f).ForBatter());
                var n = DeliveryMovement.For(Moving(nominal, seed).ForBatter());
                Assert.AreEqual(n.Drift * right.VarDrift, right.Drift, 1e-6f);
                Assert.AreEqual(n.TurnDegrees * right.VarTurn, right.TurnDegrees, 1e-6f);
                Assert.AreEqual(n.Dip, right.Dip, "dip is not varied");
                Assert.AreEqual(n.BounceFactor, right.BounceFactor, "bounce factor is not varied");
                Assert.AreEqual(right.TurnDegrees, left.TurnDegrees, "the same ball for either hand");
                Assert.AreEqual(-right.TurnDirection, left.TurnDirection, "mirrored direction");
                Assert.Greater(right.TurnDegrees, 0f);
            }
        }
    }
}
