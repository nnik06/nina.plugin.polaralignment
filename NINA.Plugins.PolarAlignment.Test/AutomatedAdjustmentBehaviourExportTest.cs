using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NINA.Plugins.PolarAlignment.Test;

/// <summary>Offline behavior handoff. Invokes the existing controller, never an actuator.</summary>
[NonParallelizable]
public class AutomatedAdjustmentBehaviourExportTest {
    private const string FixtureName = "controller-stage1.json";
    private const string ExportVariable = "TPPA_BEHAVIOUR_EXPORT_PATH";
    private sealed record Scenario(string Id, string SourceTest, string Kind,
        string Notes, Action<Trace> Run, bool UseUpas = true);

    private static Scenario[] Scenarios() {
        var cases = new List<Scenario> {
            new("july17-zero-crossing", "WorseningZeroCrossingDoesNotReplaceTrustedGain", "recorded",
                "Recorded 2026-07-17 02:36 regression trace as transcribed in the existing test; original raw log not independently re-read. Outputs are current-controller replay, not historical motor commands.", t => {
                    t.Call("SeedUpasAzimuthResponseMemory", 1.4379 / 60);
                    t.Observe(-10, 0); t.Plan(); t.Execute();
                    t.Observe(-6, 0); t.Plan(); t.Execute();
                    t.Observe(6.05, 0); t.Plan();
                }),
            Synthetic("a7-inconclusive", "ContinuesSameXDirectionBeforeClearanceWhenItWorsensDominantAzimuth", t => {
                t.Observe(35, 0); t.Plan(); t.Execute(); t.Observe(35.1, 0);
                t.Plan(); t.Execute(); t.Observe(40, 0); t.Plan();
            }),
            Synthetic("a7-early-reversal", "ReversesEarlyOnlyAfterConsecutiveStrongWorsening", t => {
                t.Observe(35, 0); t.Plan(); t.Execute(); t.Observe(41, 0);
                t.Plan(); t.Execute(); t.Observe(47, 0); t.Plan();
            }),
            Synthetic("a7-correlated-weak", "DoesNotReverseOnTwoCorrelatedSubThresholdWorsenings", t => {
                t.Observe(35, 0); t.Plan(); t.Execute(); t.Observe(37.4, 0);
                t.Plan(); t.Execute(); t.Observe(39.8, 0); t.Plan();
            }),
            Synthetic("a7-weak-resets-streak", "WeakObservationResetsEarlyWorseningStreak", t => {
                t.Observe(35, 0);
                t.Call("NoteSuccessfulExecution", new AutomatedAdjustmentPlan(4, 0, true, "Synthetic first engagement move"));
                t.Observe(41, 0);
                t.Call("NoteSuccessfulExecution", new AutomatedAdjustmentPlan(4, 0, true, "Synthetic weak-evidence move"));
                t.Observe(43, 0);
                t.Call("NoteSuccessfulExecution", new AutomatedAdjustmentPlan(4, 0, true, "Synthetic second strong move"));
                t.Observe(49, 0); t.Plan();
            }),
            Synthetic("a7-one-reversal-only", "PermitsOnlyOneEarlyReversalPerAcquisition", t => {
                t.Observe(35, 0);
                foreach (var az in new[] { 41.0, 47, 53, 59, 65 }) {
                    t.Plan(); t.Execute(); t.Observe(az, 0);
                }
                t.Plan();
            }),
            Synthetic("a7-confirmed-engagement", "RequiresConsecutiveStrongImprovementBeforeEarlyEngagementConfirmation", t => {
                t.Observe(40, 0); t.Plan(); t.Execute(); t.Observe(36, 0);
                t.Plan(); t.Execute(); t.Observe(32, 0); t.Plan();
            }),
            Synthetic("confirmed-proportional", "UsesProportionalXCorrectionAfterDominantAzimuthIsConfirmed", ConfirmedX),
            Synthetic("near-target-reversal", "RaisesTinyConfirmedXReversalToProbeMagnitude", t => {
                ConfirmedX(t, false); t.Observe(-1.5, 0); t.Plan(); t.Plan();
            }),
            Synthetic("remembered-outlier", "RobustMemoryDoesNotFollowOneDirectionalOutlier", t => {
                t.Call("SeedUpasAzimuthResponseMemory", -0.25 / 60);
                t.Observe(40, 0);
                t.Call("NoteSuccessfulExecution", new AutomatedAdjustmentPlan(24, 0, true, "Synthetic trusted azimuth probe"));
                t.Observe(28, 0); t.Plan();
            }),
            Synthetic("failed-execution", "DoesNotLearnFromFailedMove", t => {
                t.Observe(30, -18); var plan = t.Plan();
                t.Call("NoteFailedExecution", plan); t.Observe(30, -18); t.Plan();
            }, false),
            Synthetic("sub-noise", "RejectsSubNoiseSamples", t => {
                t.Observe(30, -18); t.Plan(); t.Execute(); t.Observe(30.1, -18); t.Plan();
            }, false),
            Synthetic("two-axis-bootstrap", "ExportsOnlyAQualifiedTwoAxisFirstRunResponseModel", t => {
                t.Call("ConfigureFirstRunTwoAxisBootstrap", true); t.Call("SeedXSeating", 1);
                t.Observe(60, 60); t.Plan(); t.Execute(); t.Observe(48, 60);
                t.Plan(); t.Execute(); t.Observe(48, 48); t.Plan();
            }),
            Synthetic("weak-y-bootstrap", "DoesNotPromoteWeakBootstrapYResponseToSessionLocalCoarseCorrection", t => {
                ConfigureIncomplete(t); t.Observe(300, -300); t.Plan(); t.Execute(); t.Observe(300, -297); t.Plan();
            }),
            Synthetic("collinear-y-bootstrap", "DoesNotPromoteCollinearBootstrapYResponseToSessionLocalCoarseCorrection", t => {
                ConfigureIncomplete(t); t.Observe(300, -300); t.Plan(); t.Execute(); t.Observe(330, -300); t.Plan();
            }),
            Synthetic("weak-coarse-feedback", "LatchesOffAllMotionAfterWeakSessionLocalCoarseFeedback", t => {
                ConfigureIncomplete(t); t.Observe(300, -300); t.Plan(); t.Execute(); t.Observe(300, -270);
                t.Plan(); t.Execute(); t.Observe(294, -258); t.Plan();
                t.Call("Reset"); t.Observe(300, -300); t.Plan();
            }),
            Synthetic("regressed-y-bootstrap", "LatchesOffMotionAfterRegressedBoundedYBootstrapProbe", t => {
                ConfigureIncomplete(t); t.Observe(300, -300); t.Plan(); t.Execute();
                t.Call("AbortBoundedYBootstrapProbeAfterRegression", 5.5, -5.0); t.Plan();
            }),
            Synthetic("partial-diagonal", "PartialDiagonalLatchesOffWithoutInverse", t => {
                t.Observe(60, 60);
                t.Call("AbortAfterPartialDiagonalExecution", 4.25, -3.5, "Y transport denied"); t.Plan();
            }),
            Synthetic("cross-coupled-plant", "LearnsPoorCalibrationAndAxisCrossCoupling", t => {
                Simulate(t, 54, 36, new[,] { { -0.18, -0.03 }, { 0.04, -0.11 } }, 18, 1.2);
            }, false),
            new("recorded-start-simulated-plant", "FirstRunBootstrapConvergesLastFieldVectorWithinMoveCeiling", "synthetic",
                "Only the starting vector (-34.502364573021396, +38.22095542503256 arcmin) is recorded. All subsequent observations use a synthetic plant; not a recorded convergence run.", t => {
                    ConfigureFullBootstrap(t);
                    Simulate(t, -34.502364573021396, 38.22095542503256,
                        new[,] { { -0.025, 0.0 }, { 0.0, -0.022 } }, 12, 3);
                })
        };
        foreach (var az in new[] { 1.0, -1.0, 0.6 }) {
            var value = az;
            cases.Add(Synthetic($"damped-memory-{az.ToString(CultureInfo.InvariantCulture)}",
                "UsesDampedRememberedResponseNearTolerance", t => {
                    t.Call("SeedUpasAzimuthResponseMemory", -0.25 / 60); t.Observe(value, 0); t.Plan();
                }));
        }
        foreach (var az in new[] { -54.0, 54.0 }) {
            foreach (var alt in new[] { -54.0, 54.0 }) {
                var a = az; var h = alt;
                cases.Add(new Scenario($"synthetic-corner-{a}-{h}", "FirstRunConvergenceMatrixStaysBounded", "synthetic",
                    "Synthetic sub-degree corner using the legacy logical-unit plant; not measured mechanics or current travel limits.", t => {
                        ConfigureFullBootstrap(t);
                        Simulate(t, a, h, new[,] { { -0.025, 0.0 }, { 0.0, -0.022 } }, 12, 3);
                    }));
            }
        }
        return cases.ToArray();
    }

    private static Scenario Synthetic(string id, string sourceTest, Action<Trace> run, bool upas = true) =>
        new(id, sourceTest, "synthetic", "Replay derived from an existing deterministic regression test, not a field capture.", run, upas);

    private static void ConfirmedX(Trace t) => ConfirmedX(t, true);
    private static void ConfirmedX(Trace t, bool finalPlan) {
        t.Observe(40, 0);
        foreach (var az in new[] { 45.0, 45, 39 }) { t.Plan(); t.Execute(); t.Observe(az, 0); }
        if (finalPlan) t.Plan();
    }

    private static void ConfigureIncomplete(Trace t) => t.Call("ConfigureCalibratedDirectFullTravelRoute",
        true, true, 0.0, -5.4, 5.4, 0.0, -5.4, 5.4, 0.05, 0.0, 0.0, 0.0, 81.0, 81.0, 0.05, 0.05);

    private static void ConfigureFullBootstrap(Trace t) {
        t.Call("ConfigureCalibratedDirectFullTravelRoute",
            true, true, 0.0, -5.4, 5.4, 0.0, -5.4, 5.4, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.025, 0.022);
        t.Call("SeedXSeating", 1); t.Call("ConfigureFirstRunTwoAxisBootstrap", true);
    }

    private static void Simulate(Trace t, double az, double alt, double[,] plant, int limit, double target) {
        t.Simulation = JObject.FromObject(new { initialAzErrArcmin = az, initialAltErrArcmin = alt,
            responseDegreesPerHistoricalLogicalUnit = plant, maximumIterations = limit, targetArcmin = target });
        t.Observe(az, alt);
        for (var i = 0; i < limit && Math.Sqrt(az * az + alt * alt) > target; i++) {
            var p = t.Plan();
            if (!p.HasMovement) {
                if (p.Reason.Contains("Debouncing")) { t.Observe(az, alt); continue; }
                break;
            }
            t.Execute();
            az += 60 * (plant[0, 0] * p.XMagnitude + plant[0, 1] * p.YMagnitude);
            alt += 60 * (plant[1, 0] * p.XMagnitude + plant[1, 1] * p.YMagnitude);
            t.Observe(az, alt);
        }
        t.Simulation["finalTotalArcmin"] = Math.Sqrt(az * az + alt * alt);
    }

    /// <summary>Reflection records actual argument names/defaults without adding hooks to production.</summary>
    private sealed class Trace(bool upas) {
        private readonly AutomatedAdjustmentController controller = new(useUpasEngagementController: upas);
        private AutomatedAdjustmentPlan? lastPlan;
        internal JArray Events { get; } = new();
        internal JObject? Simulation { get; set; }
        internal void Observe(double azArcmin, double altArcmin) => Call("UpdateObservation", azArcmin / 60, altArcmin / 60);
        internal AutomatedAdjustmentPlan Plan() => lastPlan = (AutomatedAdjustmentPlan)Call("CreatePlan")!;
        internal void Execute() => Call("NoteSuccessfulExecution", lastPlan ?? throw new InvalidOperationException("No plan"));

        internal object? Call(string name, params object[] supplied) {
            var method = typeof(AutomatedAdjustmentController).GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException(name);
            var parameters = method.GetParameters();
            var args = parameters.Select((p, i) => i < supplied.Length ? supplied[i] : p.DefaultValue).ToArray();
            var namedArguments = new JObject();
            for (var i = 0; i < parameters.Length; i++) namedArguments[parameters[i].Name!] = Json(args[i]);
            var result = method.Invoke(controller, args);
            var hasTrusted = controller.TryGetTrustedXAzimuthResponse(out var trusted);
            var hasBootstrap = controller.TryGetFirstRunResponseModel(out var bootstrap);
            Events.Add(new JObject {
                ["input"] = new JObject { ["method"] = name, ["arguments"] = namedArguments },
                ["output"] = Json(result),
                ["stateAfter"] = JObject.FromObject(new {
                    controller.SampleCount, controller.HasResponseModel, controller.MotionAuthorityAborted,
                    controller.MotionAuthorityAbortReason,
                    trustedAzimuthDegreesPerHistoricalLogicalUnit = hasTrusted ? (double?)trusted : null,
                    firstRunResponseModel = hasBootstrap ? bootstrap : null
                })
            });
            return result;
        }
    }

    // Compare the wire representation: JSON has no numeric Infinity token.
    private static JToken Json(object? value) => JToken.Parse(JsonConvert.SerializeObject(value));
    private static JObject Run(Scenario scenario) {
        var culture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var trace = new Trace(scenario.UseUpas);
            scenario.Run(trace);
            return new JObject {
                ["id"] = scenario.Id, ["classification"] = scenario.Kind,
                ["sourceTest"] = "NINA.Plugins.PolarAlignment.Test.AutomatedAdjustmentControllerTest.AutomatedAdjustmentController_" + scenario.SourceTest,
                ["notes"] = scenario.Notes,
                ["constructor"] = new JObject { ["useUpasEngagementController"] = scenario.UseUpas },
                ["simulation"] = trace.Simulation,
                ["events"] = trace.Events
            };
        } finally { CultureInfo.CurrentCulture = culture; }
    }

    public static IEnumerable<string> ScenarioIds() => Scenarios().Select(s => s.Id);

    [TestCaseSource(nameof(ScenarioIds))]
    public void ReplaysFrozenBehaviour(string id) {
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "BehaviourFixtures", FixtureName)));
        var expected = fixture["scenarios"]!.Single(s => s["id"]!.Value<string>() == id);
        Assert.That(JToken.DeepEquals(Run(Scenarios().Single(s => s.Id == id)), expected), Is.True, id);
    }

    [Test]
    public void ExportOrVerifyBehaviourCorpus() {
        var scenarios = Scenarios();
        var corpus = new JObject {
            ["schemaVersion"] = 1,
            ["purpose"] = "Offline behavior transfer, not motion authority, current calibration, or a deployment artifact.",
            ["sourceCheckpoint"] = "4ce4fcc592d4aed2bbaf94aef051533fd9988685",
            ["controllerGitBlob"] = "a93976f78b935dce2485a95a10ffc2e7eec932c0",
            ["units"] = new JObject {
                ["observations"] = "UpdateObservation arguments are degrees of signed pole coordinate residual, NOT great-circle distance.",
                ["plans"] = "XMagnitude/YMagnitude are HISTORICAL LOGICAL UNITS, NOT raw GRBL counts, steps, or encoder degrees.",
                ["responses"] = "Degrees of signed coordinate residual per historical logical unit.",
                ["historicalLimits"] = "The +/-5.4 values replay legacy test configuration, not the current owner's +/-5 soft limit.",
                ["nonfiniteDefaults"] = "Infinity is a JSON string denoting the legacy unbounded default, never a numeric calibrated uncertainty."
            },
            ["poleTarget"] = "Controller receives numbers only. The recorded trace's target setting is unknown. Synthetic cases do not establish a physical pole target. Future automated contract A requests target the true pole explicitly.",
            ["scenarios"] = new JArray(scenarios.Select(Run))
        };
        var output = Environment.GetEnvironmentVariable(ExportVariable);
        if (!string.IsNullOrEmpty(output)) {
            Assert.That(Path.IsPathFullyQualified(output), Is.True);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, corpus.ToString(Formatting.Indented) + "\n");
            TestContext.WriteLine($"Exported {scenarios.Length} offline scenarios to {output}");
        } else {
            var frozen = JObject.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "BehaviourFixtures", FixtureName)));
            Assert.That(JToken.DeepEquals(corpus, frozen), Is.True);
        }
    }
}
