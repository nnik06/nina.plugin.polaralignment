using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using FluentAssertions;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaAbsoluteEvidenceBinderTest {
        private static readonly JsonSerializer EvidenceSerializer = JsonSerializer.Create(
            new JsonSerializerSettings {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
                NullValueHandling = NullValueHandling.Include
            });

        [Test]
        public void BindsQualifiedIndependentEvidenceAndCreatesVerifiableReceipt() {
            var pair = QualifiedPair();

            var binding = Bind(pair);

            binding.EvidenceValid.Should().BeTrue();
            binding.IsQualified.Should().BeTrue();
            binding.ReceiptJson.Should().NotBeNull();
            binding.Issues.Should().BeEmpty();
            var verification = TppaFastQualificationReceiptVerifier.Verify(
                binding.ReceiptJson,
                PolicyDigest(),
                binding.SourcePolarErrorVectorDigest);
            verification.IsValid.Should().BeTrue();
        }

        [Test]
        public void RejectsMissingLoadedPluginAssemblyIdentity() {
            var pair = QualifiedPair();

            AssertInvalid(
                pair with { Run = pair.Run with { PluginAssembly = null } },
                "loaded plugin assembly evidence is missing");
        }

        [Test]
        public void RejectsPipelineDigestThatDoesNotMatchLoadedPluginHash() {
            var pair = QualifiedPair();

            AssertInvalid(
                pair with {
                    Run = pair.Run with {
                        PipelineDigest = Sha("different-loaded-plugin")
                    }
                },
                "does not match the loaded plugin assembly");
        }

        [Test]
        public void ValidButOverBudgetWitnessProducesNotQualifiedReceipt() {
            var pair = QualifiedPair();
            var uncertainty = QualifiedUncertainty(25.0);
            var witness = pair.Witness with {
                CalibrationDigest = uncertainty.EvidenceDigest,
                Uncertainty = uncertainty
            };

            var binding = Bind(pair with { Witness = witness });

            binding.EvidenceValid.Should().BeTrue();
            binding.IsQualified.Should().BeFalse();
            binding.ReceiptJson.Should().NotBeNull();
            binding.Issues.Should().Contain(issue =>
                issue.Contains("uncertainty", StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void RejectsStaleWitnessBeforeCreatingReceipt() {
            var pair = QualifiedPair();
            var final = pair.Run.Determinations.OrderBy(value => value.CompletedUtc).Last();
            var witness = pair.Witness with {
                ObservationUtc = final.CompletedUtc.AddSeconds(
                    TppaAbsoluteEvidenceBinder.MaximumWitnessDelaySeconds + 1)
            };

            AssertInvalid(
                pair with { Witness = witness },
                "stale or overlaps");
        }

        [Test]
        public void RejectsWitnessWhoseArcStartsBeforeTppaCompletion() {
            var pair = QualifiedPair();
            var final = pair.Run.Determinations.OrderBy(value => value.CompletedUtc).Last();
            var solves = pair.Witness.SourceSolves.ToArray();
            solves[0] = solves[0] with {
                ObservationUtc = final.CompletedUtc.AddMilliseconds(-1)
            };
            AssertInvalid(
                pair with { Witness = pair.Witness with { SourceSolves = solves } },
                "overlaps/precedes TPPA completion");
        }

        [Test]
        public void RejectsSharedObservationSourceBeforeCreatingReceipt() {
            var pair = QualifiedPair();
            var shared = pair.Run.Determinations[0].SourceVectorDigests[0];
            var witness = pair.Witness with {
                SourceVectorDigests = new[] { shared }
            };

            AssertInvalid(
                pair with { Witness = witness },
                "source-vector inputs overlap");
        }

        [TestCase("producer")]
        [TestCase("pipeline")]
        public void RejectsAliasedProducerOrPipeline(string alias) {
            var pair = QualifiedPair();
            var witness = alias == "producer"
                ? pair.Witness with { ProducerId = pair.Run.ProducerId }
                : pair.Witness with { PipelineDigest = pair.Run.PipelineDigest };

            AssertInvalid(
                pair with { Witness = witness },
                alias);
        }

        [TestCase("hardware")]
        [TestCase("mechanical")]
        [TestCase("site")]
        [TestCase("frame")]
        [TestCase("pole")]
        public void RejectsWrongPhysicalOrReferenceIdentity(string mismatch) {
            var pair = QualifiedPair();
            var witness = mismatch switch {
                "hardware" => pair.Witness with { HardwareConfigurationId = "other-hardware" },
                "mechanical" => pair.Witness with { MechanicalStateDigest = Sha("other-state") },
                "site" => pair.Witness with { SiteIdentity = "other-site" },
                "frame" => pair.Witness with { CoordinateFrame = "other-frame" },
                _ => pair.Witness with { PoleTarget = "apparent-pole" }
            };

            AssertInvalid(
                pair with { Witness = witness },
                "identities differ");
        }

        [TestCase("declared")]
        [TestCase("tppa-producer")]
        [TestCase("witness-producer")]
        [TestCase("witness-source")]
        public void RejectsCircularCalibrationProvenance(string circularity) {
            var pair = QualifiedPair();
            var witness = circularity switch {
                "declared" => pair.Witness with { CalibrationDerivedFromTppa = true },
                "tppa-producer" => pair.Witness with {
                    CalibrationSourceProducerId = pair.Run.ProducerId
                },
                "witness-producer" => pair.Witness with {
                    CalibrationSourceProducerId = pair.Witness.ProducerId
                },
                _ => pair.Witness with {
                    SourceVectorDigests = new[] { pair.Witness.CalibrationSourceDigest }
                }
            };

            AssertInvalid(
                pair with { Witness = witness },
                "calibration");
        }

        [Test]
        public void RejectsZeroUncertaintyTermAsInvalidEvidence() {
            var pair = QualifiedPair();
            var uncertainty = QualifiedUncertainty(0.0);
            var witness = pair.Witness with {
                CalibrationDigest = uncertainty.EvidenceDigest,
                Uncertainty = uncertainty
            };

            AssertInvalid(
                pair with { Witness = witness },
                "finite and positive");
        }

        [Test]
        public void RejectsUnknownEvidenceFieldEvenWithRecomputedDigest() {
            var pair = QualifiedPair();
            var token = JObject.Parse(Encoding.UTF8.GetString(Encode(pair.Run)));
            token["unexpectedField"] = "must-fail";
            var runBytes = EncodeToken(token);

            var binding = TppaAbsoluteEvidenceBinder.Bind(
                runBytes,
                Encode(pair.Witness),
                PolicyDigest(),
                ReceiptTime());

            binding.EvidenceValid.Should().BeFalse();
            binding.ReceiptJson.Should().BeNull();
            binding.Issues.Should().Contain(issue =>
                issue.Contains("cannot be parsed strictly", StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void RejectsDuplicateJsonProperty() {
            var pair = QualifiedPair();
            var json = Encoding.UTF8.GetString(Encode(pair.Run));
            var duplicate = json.Insert(json.LastIndexOf('}'), ",\"runId\":\"duplicate\"");

            var binding = TppaAbsoluteEvidenceBinder.Bind(
                Encoding.UTF8.GetBytes(duplicate),
                Encode(pair.Witness),
                PolicyDigest(),
                ReceiptTime());

            binding.EvidenceValid.Should().BeFalse();
            binding.ReceiptJson.Should().BeNull();
            binding.Issues.Should().Contain(issue =>
                issue.Contains("cannot be parsed strictly", StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void RejectsUnsupportedSchemaAndTamperedDigest() {
            var pair = QualifiedPair();
            AssertInvalid(
                pair with {
                    Run = pair.Run with {
                        SchemaVersion =
                            TppaAbsoluteEvidenceBinder.CurrentEvidenceSchemaVersion + 1
                    }
                },
                "schema version");

            var witnessJson = Encoding.UTF8.GetString(Encode(pair.Witness))
                .Replace("witness-runtime-1", "witness-runtime-2", StringComparison.Ordinal);
            var tampered = TppaAbsoluteEvidenceBinder.Bind(
                Encode(pair.Run),
                Encoding.UTF8.GetBytes(witnessJson),
                PolicyDigest(),
                ReceiptTime());
            tampered.EvidenceValid.Should().BeFalse();
            tampered.ReceiptJson.Should().BeNull();
            tampered.Issues.Should().Contain(issue =>
                issue.Contains("digest", StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void RejectsLegacySchemaForAbsoluteQualification() {
            var pair = QualifiedPair();

            AssertInvalid(
                pair with {
                    Run = pair.Run with { SchemaVersion = 1 },
                    Witness = pair.Witness with { SchemaVersion = 1 }
                },
                "schema version");
        }

        [Test]
        public void RejectsDeclaredAxisThatDoesNotMatchRawThreePointFit() {
            var pair = QualifiedPair();
            var determinations = pair.Run.Determinations.ToArray();
            determinations[1] = determinations[1] with {
                MountAxisVector = AxisVector(5.0)
            };

            AssertInvalid(
                pair with { Run = pair.Run with { Determinations = determinations } },
                "does not match the raw three-point fit");
        }

        [Test]
        public void RejectsStaleRawAtmosphereEvenWhenProducerClaimsFresh() {
            var pair = QualifiedPair();

            AssertInvalid(
                pair with {
                    Run = pair.Run with {
                        AtmosphereObservationUtc =
                            pair.Run.Determinations[0].StartedUtc.AddHours(-1),
                        AtmosphereFresh = true,
                        AtmosphereQualified = true
                    }
                },
                "atmosphere freshness");
        }

        [Test]
        public void RejectsRawClockUncertaintyEvenWhenProducerClaimsQualified() {
            var pair = QualifiedPair();

            AssertInvalid(
                pair with {
                    Run = pair.Run with {
                        ClockUncertaintyMilliseconds =
                            TppaAbsoluteEvidenceBinder.MaximumClockUncertaintyMilliseconds + 1,
                        SiteTimeProvenanceQualified = true
                    }
                },
                "site/time provenance");
        }

        [Test]
        public void RejectsDifferentialOnlyWitnessAsAbsoluteTruth() {
            var pair = QualifiedPair();

            AssertInvalid(
                pair with {
                    Witness = pair.Witness with {
                        EvidenceBasis =
                            TppaAbsoluteEvidenceBinder.DifferentialStabilityWitnessBasis
                    }
                },
                "not absolute true-pole");
        }

        [Test]
        public void RejectsWitnessRawSiteMismatchEvenWhenSiteIdentityMatches() {
            var pair = QualifiedPair();

            AssertInvalid(
                pair with {
                    Witness = pair.Witness with {
                        SiteElevationMeters = pair.Witness.SiteElevationMeters + 1
                    }
                },
                "identities differ");
        }

        [Test]
        public void RejectsRawArcBelowMinimumSpan() {
            var pair = QualifiedPair();
            var determinations = pair.Run.Determinations.ToArray();
            var solves = determinations[0].SourceSolves.ToArray();
            solves[1] = solves[1] with { UnitVector = solves[0].UnitVector };
            solves[2] = solves[2] with { UnitVector = solves[0].UnitVector };
            determinations[0] = determinations[0] with { SourceSolves = solves };

            AssertInvalid(
                pair with { Run = pair.Run with { Determinations = determinations } },
                "minimum-span qualification");
        }

        [Test]
        public void RejectsRawReturnedAClosureMismatch() {
            var pair = QualifiedPair();
            var determinations = pair.Run.Determinations.ToArray();
            var solves = determinations[^1].SourceSolves.ToArray();
            solves[0] = solves[0] with {
                RightAscensionDegrees =
                    (solves[0].RightAscensionDegrees + 1.0) % 360.0
            };
            determinations[^1] = determinations[^1] with { SourceSolves = solves };

            AssertInvalid(
                pair with { Run = pair.Run with { Determinations = determinations } },
                "closure qualification");
        }

        [Test]
        public void RejectsWitnessAxisThatDoesNotMatchRawRotationFit() {
            var pair = QualifiedPair();

            AssertInvalid(
                pair with {
                    Witness = pair.Witness with {
                        MountAxisVector = AxisVector(5.0)
                    }
                },
                "witness declared mount-axis vector");
        }

        [Test]
        public void RejectsWitnessReturnedAClosureMismatch() {
            var pair = QualifiedPair();
            var solves = pair.Witness.SourceSolves.ToArray();
            solves[^1] = solves[^1] with {
                RightAscensionDegrees =
                    (solves[^1].RightAscensionDegrees + 1.0) % 360.0
            };

            AssertInvalid(
                pair with {
                    Witness = pair.Witness with { SourceSolves = solves }
                },
                "witness returned-A closure");
        }

        [Test]
        public void RejectsGuidedWitnessAcquisitionAtBinderBoundary() {
            var pair = QualifiedPair();
            var acquisitions = pair.Witness.Acquisitions.ToArray();
            acquisitions[1] = acquisitions[1] with { GuideOutputEnabled = true };

            AssertInvalid(
                pair with {
                    Witness = pair.Witness with { Acquisitions = acquisitions }
                },
                "acquisition provenance");
        }

        [Test]
        public void RejectsWitnessAcquisitionSiteOrFieldOfViewMismatch() {
            var pair = QualifiedPair();
            var acquisitions = pair.Witness.Acquisitions.ToArray();
            acquisitions[1] = acquisitions[1] with {
                SiteLatitudeDegrees = acquisitions[1].SiteLatitudeDegrees + 0.01
            };
            acquisitions[2] = acquisitions[2] with {
                AstapFieldOfViewDegrees = 0.05
            };

            AssertInvalid(
                pair with {
                    Witness = pair.Witness with { Acquisitions = acquisitions }
                },
                "acquisition provenance");
        }

        [Test]
        public void RejectsMistimedFitsWitnessAcquisitionAtBinderBoundary() {
            var pair = QualifiedPair();
            var acquisitions = pair.Witness.Acquisitions.ToArray();
            acquisitions[2] = acquisitions[2] with {
                FitsDateObsUtc =
                    acquisitions[2].ObservationUtc.AddSeconds(2)
            };

            AssertInvalid(
                pair with {
                    Witness = pair.Witness with { Acquisitions = acquisitions }
                },
                "FITS timestamp");
        }

        [Test]
        public void RejectsDetachedWitnessImageDigestAtBinderBoundary() {
            var pair = QualifiedPair();
            var acquisitions = pair.Witness.Acquisitions.ToArray();
            acquisitions[0] = acquisitions[0] with {
                SourceImageSha256 = Sha("different-guider-image")
            };

            AssertInvalid(
                pair with {
                    Witness = pair.Witness with { Acquisitions = acquisitions }
                },
                "source-vector digests");
        }

        [Test]
        public void EmptyObjectsAreInvalidEvidenceRatherThanInternalErrors() {
            var empty = Encoding.UTF8.GetBytes("{}");

            var binding = TppaAbsoluteEvidenceBinder.Bind(
                empty,
                empty,
                PolicyDigest(),
                ReceiptTime());

            binding.EvidenceValid.Should().BeFalse();
            binding.IsQualified.Should().BeNull();
            binding.ReceiptJson.Should().BeNull();
            binding.Issues.Should().NotBeEmpty();
        }

        private static void AssertInvalid(EvidencePair pair, string expectedIssue) {
            var binding = Bind(pair);
            binding.EvidenceValid.Should().BeFalse();
            binding.IsQualified.Should().BeNull();
            binding.ReceiptJson.Should().BeNull();
            binding.Issues.Should().Contain(issue =>
                issue.Contains(expectedIssue, StringComparison.OrdinalIgnoreCase));
        }

        private static TppaAbsoluteEvidenceBinding Bind(EvidencePair pair) =>
            TppaAbsoluteEvidenceBinder.Bind(
                Encode(pair.Run),
                Encode(pair.Witness),
                PolicyDigest(),
                ReceiptTime());

        private static EvidencePair QualifiedPair() {
            var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
            var mechanicalState = Sha("mechanical-state-1");
            var determinations = new[] {
                Determination("determination-1", start, 0.20, mechanicalState, 0),
                Determination("determination-2", start.AddSeconds(20), 0.24, mechanicalState, 1),
                Determination("determination-3", start.AddSeconds(40), 0.22, mechanicalState, 2)
            };
            var pluginAssembly = new TppaLoadedAssemblyEvidence(
                TppaAbsoluteEvidenceBinder.PluginAssemblyName,
                "2.2.0.0",
                "2.2.6.79",
                @"C:\NINA\NINA.Plugins.PolarAlignment.dll",
                Sha("tppa-pipeline"),
                "11111111-2222-4333-8444-555555555555");
            var qualificationCoreAssembly = new TppaLoadedAssemblyEvidence(
                TppaAbsoluteEvidenceBinder.QualificationCoreAssemblyName,
                "2.2.0.0",
                "2.2.6.79",
                @"C:\NINA\NINA.Plugins.PolarAlignment.QualificationCore.dll",
                Sha("qualification-core"),
                "22222222-3333-4444-8555-666666666666");
            var run = new TppaQualificationRunEvidence(
                SchemaVersion: TppaAbsoluteEvidenceBinder.CurrentEvidenceSchemaVersion,
                EvidenceDigest: string.Empty,
                RunId: "run-1",
                SessionId: "session-1",
                ProducerId: "tppa-runtime-1",
                ProducerKind: TppaAbsoluteEvidenceBinder.TppaProducerKind,
                PipelineDigest: pluginAssembly.Sha256,
                HardwareConfigurationId: "hae29c-ota-camera-epoch-1",
                MechanicalStateDigest: mechanicalState,
                ClockDomainId: "utc-ntp-disciplined-1",
                ClockUncertaintyMilliseconds: 50,
                TppaInstrumentId: "main-camera-1",
                SolverIdentity: "astap-1",
                SiteIdentity: "dubai-balcony-pier-1",
                SiteLatitudeDegrees: 25.0,
                SiteLongitudeDegrees: 55.0,
                SiteElevationMeters: 20.0,
                CoordinateFrame: TppaFastQualificationConventions.IcrsObservationEpoch,
                MountAxisVectorFrame:
                    TppaAbsoluteEvidenceBinder.TopocentricHorizonNorthWestUp,
                PoleTarget: TppaFastQualificationConventions.TruePoleTarget,
                AtmosphereSource:
                    TppaFastQualificationConventions.QualifiedLocalWeatherStation,
                AtmosphereObservationUtc: start,
                AtmospherePressureHPa: 1010,
                AtmosphereTemperatureCelsius: 30,
                AtmosphereRelativeHumidityPercent: 50,
                RefractionAdjustmentEnabled: true,
                AtmosphereQualified: true,
                AtmosphereFresh: true,
                StationPressureQualified: true,
                AtmosphereTemperatureQualified: true,
                AtmosphereHumidityQualified: true,
                SiteTimeProvenanceQualified: true,
                CoordinateFrameQualified: true,
                Determinations: determinations,
                TargetPoleVector: AxisVector(0),
                PluginAssembly: pluginAssembly,
                QualificationCoreAssembly: qualificationCoreAssembly);

            var uncertainty = QualifiedUncertainty(1.0);
            var witnessObservationUtc =
                determinations[^1].CompletedUtc.AddSeconds(10);
            var witnessAxis = AxisVector(0.10);
            var witnessSolves = new[] {
                Solve(witnessAxis, witnessObservationUtc.AddSeconds(-9), 100, 0),
                Solve(witnessAxis, witnessObservationUtc.AddSeconds(-8), 100, 1),
                Solve(witnessAxis, witnessObservationUtc.AddSeconds(-7), 100, 2),
                Solve(witnessAxis, witnessObservationUtc, 101, 0)
            };
            var witnessPositions = new[] { "A", "B", "C", "A" };
            var witnessCommandedRa = new[] { 100.0, 125.0, 150.0, 100.0 };
            var witnessAcquisitions = witnessSolves
                .Select((solve, index) => new TppaRaRotationWitnessAcquisitionEvidence(
                    SchemaVersion:
                        TppaRaRotationWitnessPointReceipt.CurrentSchemaVersion,
                    PositionId: witnessPositions[index],
                    SequenceIndex: index,
                    MountCommandId: "mount-command-" + index,
                    MountCommandIssuedUtc: solve.ObservationUtc.AddSeconds(-3),
                    MountCommandCompletedUtc: solve.ObservationUtc.AddSeconds(-2),
                    CommandedRightAscensionDegrees: witnessCommandedRa[index],
                    CommandedDeclinationDegrees: 80,
                    TrackingEnabled: true,
                    Slewing: false,
                    Phd2AppState: "Stopped",
                    GuideOutputEnabled: false,
                    CaptureStartedUtc: solve.ObservationUtc.AddSeconds(-0.5),
                    ExposureSeconds: 1,
                    ObservationUtc: solve.ObservationUtc,
                    FitsDateObsUtc: solve.ObservationUtc.AddSeconds(-0.5),
                    FitsDateObsConvention:
                        TppaAbsoluteEvidenceBinder.FitsDateObsExposureStart,
                    FitsTimestampUncertaintyMilliseconds: 10,
                    SourceImageSha256: solve.ContentSha256,
                    SolverOutputSha256: Sha("solver-output-" + index),
                    SolverIdentity: "ASTAP-2026.1",
                    SolverBinarySha256: Sha("astap-binary"),
                    SolverHintPolicy:
                        TppaAbsoluteEvidenceBinder.BlindNoMountHintSolvePolicy,
                    SourceCoordinateFrame:
                        TppaFastQualificationConventions.IcrsObservationEpoch,
                    SiteLatitudeDegrees: run.SiteLatitudeDegrees,
                    SiteLongitudeDegrees: run.SiteLongitudeDegrees,
                    SiteElevationMeters: run.SiteElevationMeters,
                    AstapFieldOfViewDegrees: 2.0,
                    MountAzimuthDegrees: 0,
                    MountAltitudeDegrees: 45,
                    SolvedRightAscensionDegrees: solve.RightAscensionDegrees,
                    SolvedDeclinationDegrees: solve.DeclinationDegrees,
                    PositionAngleDegrees: 0,
                    PierSide: solve.PierSide))
                .ToArray();
            var witness = new TppaQualificationWitnessEvidence(
                SchemaVersion: TppaAbsoluteEvidenceBinder.CurrentEvidenceSchemaVersion,
                EvidenceDigest: string.Empty,
                BindsRunId: run.RunId,
                SessionId: run.SessionId,
                ProducerId: "witness-runtime-1",
                ProducerKind: TppaAbsoluteEvidenceBinder.WitnessProducerKind,
                PipelineDigest: Sha("witness-pipeline"),
                HardwareConfigurationId: run.HardwareConfigurationId,
                MechanicalStateDigest: run.MechanicalStateDigest,
                SiteIdentity: run.SiteIdentity,
                SiteLatitudeDegrees: run.SiteLatitudeDegrees,
                SiteLongitudeDegrees: run.SiteLongitudeDegrees,
                SiteElevationMeters: run.SiteElevationMeters,
                ClockDomainId: "utc-ntp-independent-witness",
                ClockUncertaintyMilliseconds: 50,
                CoordinateFrame: run.CoordinateFrame,
                MountAxisVectorFrame: run.MountAxisVectorFrame,
                PoleTarget: run.PoleTarget,
                EvidenceBasis:
                    TppaAbsoluteEvidenceBinder.AbsoluteTruePoleWitnessBasis,
                MeasurementMethod:
                    TppaAbsoluteEvidenceBinder.RaRotationCircleWitnessMethod,
                ObservationUtc: witnessObservationUtc,
                CorrectionSequenceNumber: 7,
                InstrumentId: "ipolar-camera-1",
                SourceVectorDigests: witnessSolves
                    .Select(value => value.ContentSha256)
                    .ToArray(),
                SourceSolves: witnessSolves,
                TrajectoryPreflightDigest: Sha("trajectory-preflight"),
                TrajectoryPreflightQualified: true,
                TrajectoryMinimumAltitudeDegrees: 42,
                TrajectoryMaximumSampleStepDegrees: 1,
                TrajectoryTotalArcDegrees: 50,
                TrajectoryDesignConditionProxy: 2.8,
                InitialPhd2AppState: "Stopped",
                FinalPhd2AppState: "Stopped",
                GuideOutputRestored: true,
                Acquisitions: witnessAcquisitions,
                CalibrationDigest: uncertainty.EvidenceDigest,
                CalibrationCurrent: true,
                CalibrationSourceProducerId: "independent-calibration-campaign-1",
                CalibrationSourceDigest: uncertainty.InputDigest,
                CalibrationDerivedFromTppa: false,
                Uncertainty: uncertainty,
                MountAxisVector: witnessAxis);
            return new(run, witness);
        }

        private static TppaQualificationDeterminationEvidence Determination(
                string id,
                DateTime startedUtc,
                double errorArcMinutes,
                string mechanicalState,
                int sourceOffset) {
            var axis = AxisVector(errorArcMinutes);
            var solves = Enumerable.Range(0, 3)
                .Select(index => Solve(
                    axis,
                    startedUtc.AddSeconds(index + 1),
                    sourceOffset,
                    index))
                .ToArray();
            return new(
                DeterminationId: id,
                StartedUtc: startedUtc,
                CompletedUtc: startedUtc.AddSeconds(5),
                MechanicalStateDigest: mechanicalState,
                CorrectionSequenceNumber: 7,
                FreshSolvesUncached: true,
                GeometryQualified: true,
                MinimumArcSpanQualified: true,
                ClosureQualified: true,
                SourceVectorDigests: solves
                    .Select(value => value.ContentSha256)
                    .ToArray(),
                SourceSolves: solves,
                MountAxisVector: axis);
        }

        private static TppaQualificationSolveEvidence Solve(
                TppaQualificationVector axis,
                DateTime observationUtc,
                int sourceOffset,
                int pointIndex) {
            var angle = pointIndex * 20.0 * Math.PI / 180.0;
            const double axialComponent = 0.25;
            var radialComponent = Math.Sqrt(1 - axialComponent * axialComponent);
            var basisU = new TppaQualificationVector(0, 1, 0);
            var basisV = new TppaQualificationVector(-axis.Z, 0, axis.X);
            var vector = new TppaQualificationVector(
                axialComponent * axis.X
                    + radialComponent * Math.Sin(angle) * basisV.X,
                axialComponent * axis.Y
                    + radialComponent * Math.Cos(angle),
                axialComponent * axis.Z
                    + radialComponent * Math.Sin(angle) * basisV.Z);
            var rightAscension = Math.Atan2(vector.Y, vector.X) * 180.0 / Math.PI;
            if (rightAscension < 0) { rightAscension += 360; }
            var declination = Math.Asin(vector.Z) * 180.0 / Math.PI;
            return new(
                observationUtc,
                Sha($"tppa-source-{sourceOffset}-{pointIndex}"),
                rightAscension,
                declination,
                "pierEast",
                vector);
        }

        private static TppaWitnessUncertaintyEvidence QualifiedUncertainty(
                double measurementStandardUncertainty) {
            var evidence = new TppaWitnessUncertaintyEvidence(
                ModelId: "ipolar-calibration-v1",
                InputDigest: Sha("independent-calibration-input"),
                EvidenceDigest: new string('0', 64),
                CalibrationSampleCount: 6,
                ClosureSampleCount: 3,
                MeasurementStandardUncertaintyArcSeconds:
                    measurementStandardUncertainty,
                CalibrationResidualArcSeconds: 1,
                OrientationModelResidualArcSeconds: 1,
                ClosureResidualArcSeconds: 1,
                FrameSystematicBoundArcSeconds: 1,
                DistortionSystematicBoundArcSeconds: 1,
                MechanicalSystematicBoundArcSeconds: 1);
            return evidence with {
                EvidenceDigest = TppaWitnessUncertainty.ComputeEvidenceDigest(evidence)
            };
        }

        private static TppaQualificationVector AxisVector(double arcMinutes) {
            const double latitudeDegrees = 25.0;
            var latitude = latitudeDegrees * Math.PI / 180.0;
            var radians = arcMinutes / 60.0 * Math.PI / 180.0;
            var poleX = Math.Cos(latitude);
            var poleZ = Math.Sin(latitude);
            return new(
                Math.Cos(radians) * poleX + Math.Sin(radians) * poleZ,
                0,
                -Math.Sin(radians) * poleX + Math.Cos(radians) * poleZ);
        }

        private static byte[] Encode(object evidence) {
            var token = JObject.FromObject(evidence, EvidenceSerializer);
            return EncodeToken(token);
        }

        private static byte[] EncodeToken(JObject token) {
            token["evidenceDigest"] = new string('0', 64);
            token["evidenceDigest"] =
                TppaAbsoluteEvidenceBinder.ComputeCanonicalEvidenceDigest(
                    token.ToString(Formatting.None));
            return Encoding.UTF8.GetBytes(token.ToString(Formatting.None));
        }

        private static DateTime ReceiptTime() =>
            new(2026, 8, 1, 0, 2, 0, DateTimeKind.Utc);

        private static string PolicyDigest() => Sha("compiled-policy-source");

        private static string Sha(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
                .ToLowerInvariant();

        private sealed record EvidencePair(
            TppaQualificationRunEvidence Run,
            TppaQualificationWitnessEvidence Witness);
    }
}
