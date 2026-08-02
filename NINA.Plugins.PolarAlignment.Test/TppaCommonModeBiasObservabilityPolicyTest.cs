using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaCommonModeBiasObservabilityPolicyTest {
        private const string TppaDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string MechanicalDigest = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        private static readonly DateTime StartUtc = new(2026, 8, 2, 20, 0, 0, DateTimeKind.Utc);
        private static readonly TppaQualificationVector WitnessAxis =
            Unit(new TppaQualificationVector(0.905, 0, 0.425));

        [Test]
        public void QualifiedBalancedCampaignRecoversBiasWithoutAuthority() {
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(Arcs(0.20, -0.10)));

            result.IsIdentifiable.Should().BeTrue(string.Join("; ", result.Issues));
            result.BiasWestwardSkyComponentArcMinutes.Should().BeApproximately(0.20, 0.001);
            result.BiasZenithwardSkyComponentArcMinutes.Should().BeApproximately(-0.10, 0.001);
            result.WitnessObservedSpreadArcMinutes.Should().BeApproximately(0, 1e-6);
            result.DistinctPointingCount.Should().Be(4);
            result.PierOddBiasArcMinutes.Should().BeApproximately(0, 1e-6);
            result.GrantsMotionAuthority.Should().BeFalse();
            result.GrantsCompletionAuthority.Should().BeFalse();
        }

        [Test]
        public void SingleArcPierSideFailsClosed() {
            var arcs = Arcs(0.2, -0.1).Select(value => value with { PierSide = "east" }).ToArray();
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("both pier sides"));
        }

        [Test]
        public void SingleWitnessPierSideFailsClosed() {
            var evidence = Evidence(Arcs(0.2, -0.1)) with {
                Witnesses = Witnesses().Select(value => value with { PierSide = "east" }).ToArray()
            };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(evidence);
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("witness observations"));
        }

        [Test]
        public void CollinearSkyGeometryFailsConditioningGate() {
            var arcs = Arcs(0.2, -0.1).Select((value, index) => value with {
                CenterHourAngleDegrees = -30 + index * 12,
                CenterDeclinationDegrees = 65 + index * 4
            }).ToArray();
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("ill-conditioned"));
        }

        [Test]
        public void ThreeRepeatedPointingsCannotClaimFieldCoverage() {
            var points = new[] { (-30.0, 70.0), (30.0, 70.0), (0.0, 80.0) };
            var arcs = Arcs(0.2, -0.1).Select((value, index) => value with {
                CenterHourAngleDegrees = points[index % 3].Item1,
                CenterDeclinationDegrees = points[index % 3].Item2
            }).ToArray();
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.DistinctPointingCount.Should().Be(3);
            result.Issues.Should().Contain(issue => issue.Contains("distinct TPPA pointings"));
        }

        [Test]
        public void HourAnglesAcrossAntimeridianDoNotFakeGeometrySpan() {
            var ha = new[] { -179.0, -179.0, 179.0, 179.0, -179.0, -179.0, 179.0, 179.0 };
            var dec = new[] { 68.0, 82.0, 68.0, 82.0, 68.0, 82.0, 68.0, 82.0 };
            var arcs = Arcs(0.2, -0.1).Select((value, index) => value with {
                CenterHourAngleDegrees = ha[index], CenterDeclinationDegrees = dec[index]
            }).ToArray();
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.HourAngleSpanDegrees.Should().BeApproximately(2.0, 0.001);
        }

        [Test]
        public void FieldDependentBiasInflatesConservativeBound() {
            var arcs = Arcs(0.2, -0.1).ToArray();
            arcs[^1] = arcs[^1] with { EstimatedMountAxis = AxisWithBias(1.2, -0.1) };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.InSampleFieldVariationArcMinutes.Should().BeGreaterThan(0.5);
        }

        [Test]
        public void TwoDegreeDiscrepancyIsRejected() {
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(Arcs(120, 0)));
            result.IsIdentifiable.Should().BeFalse();
            result.BiasMagnitudeArcMinutes.Should().BeApproximately(120, 0.01);
        }

        [Test]
        public void MixedArcMechanicalEpochFailsClosed() {
            var arcs = Arcs(0.2, -0.1).ToArray();
            arcs[^1] = arcs[^1] with { MechanicalStateDigest = Hex('d') };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("mechanical"));
        }

        [Test]
        public void WitnessMechanicalEpochMismatchFailsClosed() {
            var witnesses = Witnesses().ToArray();
            witnesses[1] = witnesses[1] with { MechanicalStateDigest = Hex('d') };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(Arcs(0.2, -0.1)) with { Witnesses = witnesses });
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("mechanical"));
        }

        [Test]
        public void WitnessEnvironmentMismatchFailsClosed() {
            var witnesses = Witnesses().ToArray();
            witnesses[0] = witnesses[0] with { EnvironmentEnvelopeId = "other-environment" };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(Arcs(0.2, -0.1)) with { Witnesses = witnesses });
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("environment"));
        }

        [Test]
        public void RefractedWitnessFailsTruePoleCalibration() {
            var witnesses = Witnesses().ToArray();
            witnesses[0] = witnesses[0] with { RefractionAdjustmentEnabled = false };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(Arcs(0.2, -0.1)) with { Witnesses = witnesses });
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("true-pole"));
        }

        [Test]
        public void WitnessOutsideCampaignWindowFailsClosed() {
            var witnesses = Witnesses().ToArray();
            witnesses[0] = witnesses[0] with { ObservationUtc = StartUtc.AddMinutes(-1) };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(Arcs(0.2, -0.1)) with { Witnesses = witnesses });
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("outside"));
        }

        [Test]
        public void SharedOpticalTrainFailsIndependenceGate() {
            var witnesses = Witnesses().Select(value => value with {
                OpticalTrainId = "main-optics-1"
            }).ToArray();
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(Arcs(0.2, -0.1)) with { Witnesses = witnesses });
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("not independent"));
        }

        [Test]
        public void DuplicateArcMeasurementDigestFailsClosed() {
            var arcs = Arcs(0.2, -0.1).ToArray();
            arcs[1] = arcs[1] with { MeasurementDigest = arcs[0].MeasurementDigest };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("measurement digest"));
        }

        [Test]
        public void DuplicateWitnessReceiptFailsClosed() {
            var witnesses = Witnesses().ToArray();
            witnesses[1] = witnesses[1] with {
                QualificationReceiptDigest = witnesses[0].QualificationReceiptDigest
            };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(Arcs(0.2, -0.1)) with { Witnesses = witnesses });
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("qualification receipt"));
        }

        [Test]
        public void WitnessObservedSpreadCannotHideBehindStatedUncertainty() {
            var witnesses = Witnesses().ToArray();
            witnesses[1] = witnesses[1] with { EstimatedMountAxis = AxisWithBias(0.7, 0) };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(Arcs(0.2, -0.1)) with { Witnesses = witnesses });
            result.IsIdentifiable.Should().BeFalse();
            result.WitnessObservedSpreadArcMinutes.Should().BeGreaterThan(0.25);
        }

        [Test]
        public void LeftHandedBasisFailsClosed() {
            var evidence = Evidence(Arcs(0.2, -0.1)) with {
                LocalWest = new TppaQualificationVector(0, -1, 0)
            };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(evidence);
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("North-West-Up"));
        }

        [Test]
        public void HandComputedWestwardOffsetHasPositiveSign() {
            var angle = Math.PI / 180.0 / 60.0;
            var witness = Unit(WitnessAxis);
            var displaced = Unit(new TppaQualificationVector(
                witness.X * Math.Cos(angle), Math.Sin(angle), witness.Z * Math.Cos(angle)));
            var arcs = Arcs(0, 0).Select(value => value with {
                EstimatedMountAxis = displaced
            }).ToArray();
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeTrue(string.Join("; ", result.Issues));
            result.BiasWestwardSkyComponentArcMinutes.Should().BeApproximately(1.0, 0.001);
            result.BiasZenithwardSkyComponentArcMinutes.Should().BeApproximately(0, 0.001);
        }

        [Test]
        public void WeakerFieldPolicyIsRejected() {
            Action act = () => TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(Arcs(0.2, -0.1)),
                new TppaCommonModeBiasPolicy(MaximumCalibrationUncertaintyArcMinutes: 1.0));
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void RefractionDisabledArcFailsTruePoleCalibration() {
            var arcs = Arcs(0.2, -0.1).ToArray();
            arcs[0] = arcs[0] with { RefractionAdjustmentEnabled = false };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("true pole"));
        }

        [Test]
        public void ConservativeBoundAddsTermsWithoutRss() {
            var arcs = Arcs(0.2, -0.1).Select((value, index) => value with {
                AxisUncertaintyBoundArcMinutes = 0.12,
                EstimatedMountAxis = AxisWithBias(index == 0 ? 0.3 : 0.2, -0.1)
            }).ToArray();
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            var expected = result.WitnessUncertaintyArcMinutes + 0.12
                + result.InSampleFieldVariationArcMinutes
                + result.PierOddBiasArcMinutes;
            result.ConservativeCalibrationUncertaintyArcMinutes.Should().BeApproximately(expected, 1e-9);
            result.ConservativeCalibrationUncertaintyArcMinutes.Should().BeGreaterThan(
                Math.Sqrt(result.WitnessUncertaintyArcMinutes * result.WitnessUncertaintyArcMinutes + 0.12 * 0.12));
        }

        [Test]
        public void UnbalancedPierCountsFailClosed() {
            var arcs = Arcs(0.2, -0.1).ToList();
            arcs.Add(arcs[0] with {
                ArcId = "arc-9",
                MeasurementDigest = $"{99:x64}",
                PierSide = "east",
                ObservationUtc = StartUtc.AddMinutes(75)
            });
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("exactly pier balanced"));
        }

        [Test]
        public void PierOddDetectorBiasIsSeparatedAndRejected() {
            var arcs = Arcs(0, 0).Select(value => value with {
                EstimatedMountAxis = AxisWithBias(
                    value.PierSide == "east" ? 0.4 : -0.4, 0)
            }).ToArray();
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.BiasMagnitudeArcMinutes.Should().BeApproximately(0, 0.001);
            result.PierOddBiasArcMinutes.Should().BeApproximately(0.4, 0.001);
            result.Issues.Should().Contain(issue => issue.Contains("pier-odd"));
        }

        [Test]
        public void WitnessesMustCoverBothCampaignEndpoints() {
            var witnesses = Witnesses().Select((value, index) => value with {
                ObservationUtc = StartUtc.AddMinutes(index == 0 ? 20 : 50)
            }).ToArray();
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(Arcs(0.2, -0.1)) with { Witnesses = witnesses });
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("campaign endpoints"));
        }

        [Test]
        public void WitnessPipelineConfigurationMustRemainFixed() {
            var witnesses = Witnesses().ToArray();
            witnesses[1] = witnesses[1] with { PipelineConfigurationDigest = Hex('e') };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(Arcs(0.2, -0.1)) with { Witnesses = witnesses });
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("not independent"));
        }

        [Test]
        public void WitnessCannotReuseTppaMeasurement() {
            var arcs = Arcs(0.2, -0.1).ToArray();
            var witnesses = Witnesses().ToArray();
            witnesses[0] = witnesses[0] with { MeasurementDigest = arcs[0].MeasurementDigest };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(
                Evidence(arcs) with { Witnesses = witnesses });
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("reuses a TPPA arc"));
        }

        [Test]
        public void SouthPoleFrameCanQualify() {
            var southAxis = Unit(new TppaQualificationVector(-0.905, 0, 0.425));
            var arcs = Arcs(0, 0).Select(value => value with {
                EstimatedMountAxis = southAxis
            }).ToArray();
            var witnesses = Witnesses().Select(value => value with {
                EstimatedMountAxis = southAxis
            }).ToArray();
            var evidence = Evidence(arcs) with {
                SiteLatitudeDegrees = -Math.Asin(southAxis.Z) * 180.0 / Math.PI,
                CelestialPoleDirection = TppaCommonModeBiasObservabilityPolicy.SouthPole,
                Witnesses = witnesses
            };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(evidence);
            result.IsIdentifiable.Should().BeTrue(string.Join("; ", result.Issues));
        }

        [Test]
        public void HandComputedZenithwardOffsetHasPositiveSign() {
            var angle = Math.PI / 180.0 / 60.0;
            var witness = Unit(WitnessAxis);
            var displaced = Unit(new TppaQualificationVector(
                witness.X * Math.Cos(angle) - witness.Z * Math.Sin(angle),
                0,
                witness.Z * Math.Cos(angle) + witness.X * Math.Sin(angle)));
            var arcs = Arcs(0, 0).Select(value => value with {
                EstimatedMountAxis = displaced
            }).ToArray();
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeTrue(string.Join("; ", result.Issues));
            result.BiasWestwardSkyComponentArcMinutes.Should().BeApproximately(0, 0.001);
            result.BiasZenithwardSkyComponentArcMinutes.Should().BeApproximately(1.0, 0.001);
        }

        [Test]
        public void CampaignDurationCapIsEnforced() {
            var arcs = Arcs(0.2, -0.1).ToArray();
            arcs[^1] = arcs[^1] with { ObservationUtc = StartUtc.AddMinutes(121) };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("campaign duration"));
        }

        [Test]
        public void ArcSeparationFloorIsEnforced() {
            var arcs = Arcs(0.2, -0.1).ToArray();
            arcs[1] = arcs[1] with { ObservationUtc = StartUtc.AddSeconds(10) };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("separated by only"));
        }

        [Test]
        public void AntipodalArcPolarityFailsClosed() {
            var arcs = Arcs(0.2, -0.1).ToArray();
            arcs[0] = arcs[0] with { EstimatedMountAxis = Scale(WitnessAxis, -1) };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("polarity"));
        }

        [Test]
        public void SitePolePlausibilityGateIsEnforced() {
            var evidence = Evidence(Arcs(0.2, -0.1)) with { SiteLatitudeDegrees = 0 };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(evidence);
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("declared site latitude"));
        }

        [Test]
        public void ZenithSingularityGateIsEnforced() {
            var angle = 89.0 * Math.PI / 180.0;
            var nearZenith = Unit(new TppaQualificationVector(Math.Cos(angle), 0, Math.Sin(angle)));
            var arcs = Arcs(0, 0).Select(value => value with {
                EstimatedMountAxis = nearZenith
            }).ToArray();
            var witnesses = Witnesses().Select(value => value with {
                EstimatedMountAxis = nearZenith
            }).ToArray();
            var evidence = Evidence(arcs) with {
                SiteLatitudeDegrees = 89.0,
                Witnesses = witnesses
            };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(evidence);
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("singularity"));
        }

        [Test]
        public void NonUtcArcFailsClosed() {
            var arcs = Arcs(0.2, -0.1).ToArray();
            arcs[0] = arcs[0] with {
                ObservationUtc = DateTime.SpecifyKind(arcs[0].ObservationUtc, DateTimeKind.Unspecified)
            };
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("not UTC"));
        }

        [Test]
        public void MissingArcEntryFailsClosed() {
            var arcs = Arcs(0.2, -0.1).ToArray();
            arcs[0] = null;
            var result = TppaCommonModeBiasObservabilityPolicy.Evaluate(Evidence(arcs));
            result.IsIdentifiable.Should().BeFalse();
            result.Issues.Should().Contain(issue => issue.Contains("arc 1 is missing"));
        }

        private static TppaCommonModeBiasEvidence Evidence(
                IReadOnlyList<TppaBiasArcObservation> arcs) => new(
            TppaInstrumentId: "main-camera-tppa",
            TppaDetectorSerial: "main-detector-1",
            TppaOpticalTrainId: "main-optics-1",
            TppaSolverId: "astap-main",
            TppaPipelineConfigurationDigest: TppaDigest,
            BasisConventionId: TppaCommonModeBiasObservabilityPolicy.TopocentricNwuBasis,
            LocalNorth: new TppaQualificationVector(1, 0, 0),
            LocalWest: new TppaQualificationVector(0, 1, 0),
            LocalUp: new TppaQualificationVector(0, 0, 1),
            SiteLatitudeDegrees: Math.Asin(WitnessAxis.Z) * 180.0 / Math.PI,
            CelestialPoleDirection: TppaCommonModeBiasObservabilityPolicy.NorthPole,
            Witnesses: Witnesses(),
            Arcs: arcs);

        private static IReadOnlyList<TppaWitnessAxisObservation> Witnesses() =>
            Enumerable.Range(0, 2).Select(index => new TppaWitnessAxisObservation(
                WitnessId: $"witness-{index + 1}",
                MeasurementDigest: Hex(index == 0 ? '1' : '2'),
                QualificationReceiptDigest: Hex(index == 0 ? '3' : '4'),
                InstrumentId: "independent-axis-camera",
                DetectorSerial: "witness-detector-1",
                OpticalTrainId: "witness-optics-1",
                SolverId: "independent-axis-fit",
                PipelineConfigurationDigest: Hex('b'),
                Qualified: true,
                EstimatedMountAxis: WitnessAxis,
                AxisUncertaintyBoundArcMinutes: 0.10,
                PierSide: index == 0 ? "east" : "west",
                ObservationUtc: StartUtc.AddMinutes(index == 0 ? 1 : 69),
                HardwareConfigurationId: "hardware-1",
                MechanicalStateDigest: MechanicalDigest,
                SiteIdentity: "site-1",
                EnvironmentEnvelopeId: "environment-1",
                CoordinateFrame: TppaFastQualificationConventions.IcrsObservationEpoch,
                RefractionAdjustmentEnabled: true)).ToArray();

        private static IReadOnlyList<TppaBiasArcObservation> Arcs(
                double westwardArcMinutes,
                double zenithwardArcMinutes) {
            var hourAngles = new[] { -30.0, -10.0, 10.0, 30.0, -30.0, -10.0, 10.0, 30.0 };
            var declinations = new[] { 70.0, 80.0, 70.0, 80.0, 70.0, 80.0, 70.0, 80.0 };
            return Enumerable.Range(0, 8).Select(index => new TppaBiasArcObservation(
                ArcId: $"arc-{index + 1}",
                MeasurementDigest: $"{index + 10:x64}",
                EstimatedMountAxis: AxisWithBias(westwardArcMinutes, zenithwardArcMinutes),
                AxisFitQualified: true,
                AxisUncertaintyBoundArcMinutes: 0.05,
                CenterHourAngleDegrees: hourAngles[index],
                CenterDeclinationDegrees: declinations[index],
                PierSide: index < 4 ? "east" : "west",
                ObservationUtc: StartUtc.AddMinutes(index * 10),
                HardwareConfigurationId: "hardware-1",
                MechanicalStateDigest: MechanicalDigest,
                SiteIdentity: "site-1",
                EnvironmentEnvelopeId: "environment-1",
                CoordinateFrame: TppaFastQualificationConventions.IcrsObservationEpoch,
                RefractionAdjustmentEnabled: true,
                MechanicalCameraRotationDegrees: 12.0)).ToArray();
        }

        private static TppaQualificationVector AxisWithBias(double westward, double zenithward) {
            var witness = Unit(WitnessAxis);
            var west = new TppaQualificationVector(0, 1, 0);
            var up = new TppaQualificationVector(0, 0, 1);
            var zenith = Unit(Subtract(up, Scale(witness, Dot(up, witness))));
            var westRadians = westward / 60.0 * Math.PI / 180.0;
            var zenithRadians = zenithward / 60.0 * Math.PI / 180.0;
            var angle = Math.Sqrt(westRadians * westRadians + zenithRadians * zenithRadians);
            if (angle == 0) { return witness; }
            var direction = Unit(Add(Scale(west, westRadians), Scale(zenith, zenithRadians)));
            return Unit(Add(Scale(witness, Math.Cos(angle)), Scale(direction, Math.Sin(angle))));
        }

        private static string Hex(char value) => new(value, 64);
        private static TppaQualificationVector Unit(TppaQualificationVector value) {
            var length = Math.Sqrt(Dot(value, value));
            return Scale(value, 1.0 / length);
        }
        private static TppaQualificationVector Add(TppaQualificationVector a, TppaQualificationVector b) =>
            new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        private static TppaQualificationVector Subtract(TppaQualificationVector a, TppaQualificationVector b) =>
            new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        private static TppaQualificationVector Scale(TppaQualificationVector value, double scale) =>
            new(value.X * scale, value.Y * scale, value.Z * scale);
        private static double Dot(TppaQualificationVector a, TppaQualificationVector b) =>
            a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }
}
