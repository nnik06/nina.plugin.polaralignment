using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class UpasSupervisorCoarseRuntimeConstraintsTest {
        private const string Lease = "10000000-0000-0000-0000-000000000001";

        private static JObject ValidRoot() => JObject.Parse("""
            {
              "remainingTravelBudgetDegrees": {
                "signConvention": "adjusterIncreasing",
                "az": { "positiveDegrees": 5.0, "negativeDegrees": 4.0 },
                "alt": { "positiveDegrees": 3.0, "negativeDegrees": 2.0 },
                "cumulativeSessionDegrees": 10.0
              },
              "activeLeaseId": null,
              "activeTransactionId": null,
              "lockedReason": null,
              "capabilities": {
                "planningEvidence": true,
                "physicalMotionAvailable": true,
                "atomicBudgetReservationAvailable": true,
                "motionAuthorityIncluded": false
              }
            }
            """);

        [Test]
        public void ValidPlanningSnapshotParsesWithoutMotionAuthority() {
            var result = Parse(ValidRoot());

            result.Azimuth.PositiveDegrees.Should().Be(5.0);
            result.Azimuth.NegativeDegrees.Should().Be(4.0);
            result.Altitude.PositiveDegrees.Should().Be(3.0);
            result.CumulativeSessionDegrees.Should().Be(10.0);
            result.MotionAuthorityIncluded.Should().BeFalse();
        }

        [Test]
        public void CallerOwnedLeaseAdmitsAndEveryLeaseMismatchRejects() {
            var owned = ValidRoot();
            owned["activeLeaseId"] = Lease;
            var foreign = ValidRoot();
            foreign["activeLeaseId"] = "20000000-0000-0000-0000-000000000001";

            Parse(owned, Guid.Parse(Lease)).ActiveLeaseId.Should().Be(Guid.Parse(Lease));
            Action missingCaller = () => Parse(owned);
            Action staleCaller = () => Parse(ValidRoot(), Guid.Parse(Lease));
            Action foreignCaller = () => Parse(foreign, Guid.Parse(Lease));

            missingCaller.Should().Throw<JsonException>().WithMessage("*lease*");
            staleCaller.Should().Throw<JsonException>().WithMessage("*lease*");
            foreignCaller.Should().Throw<JsonException>().WithMessage("*lease*");
        }

        [Test]
        public void ActiveTransactionAndAnyLockReject() {
            var transaction = ValidRoot();
            transaction["activeTransactionId"] =
                "30000000-0000-0000-0000-000000000001";
            var locked = ValidRoot();
            locked["lockedReason"] = "operator-interlock";

            Action parseTransaction = () => Parse(transaction);
            Action parseLocked = () => Parse(locked);

            parseTransaction.Should().Throw<JsonException>().WithMessage("*active transaction*");
            parseLocked.Should().Throw<JsonException>().WithMessage("*locked*");
        }

        [Test]
        public void EveryRequiredCapabilityIsFailClosed() {
            foreach (var mutation in new Action<JObject>[] {
                value => value["capabilities"]!["planningEvidence"] = false,
                value => value["capabilities"]!["physicalMotionAvailable"] = false,
                value => value["capabilities"]!["atomicBudgetReservationAvailable"] = false,
                value => value["capabilities"]!["motionAuthorityIncluded"] = true
            }) {
                var root = ValidRoot();
                mutation(root);
                Action parse = () => Parse(root);
                parse.Should().Throw<JsonException>().WithMessage("*capabilities*");
            }
        }

        [Test]
        public void BudgetsAreDirectionalFiniteNonNegativeDegrees() {
            foreach (var mutation in new Action<JObject>[] {
                value => value["remainingTravelBudgetDegrees"]!["az"]!["positiveDegrees"] = -0.1,
                value => value["remainingTravelBudgetDegrees"]!["alt"]!["negativeDegrees"] = "2",
                value => value["remainingTravelBudgetDegrees"]!["cumulativeSessionDegrees"] = null,
                value => value["remainingTravelBudgetDegrees"]!["signConvention"] = "skyErrorSign"
            }) {
                var root = ValidRoot();
                mutation(root);
                Action parse = () => Parse(root);
                parse.Should().Throw<JsonException>();
            }
        }

        [Test]
        public void UnknownAndMissingNestedFieldsReject() {
            var unknownBudget = ValidRoot();
            unknownBudget["remainingTravelBudgetDegrees"]!["remainingDegrees"] = 10.0;
            var missingDirection = ValidRoot();
            ((JObject)missingDirection["remainingTravelBudgetDegrees"]!["az"]!)
                .Property("negativeDegrees")!.Remove();
            var unknownCapability = ValidRoot();
            unknownCapability["capabilities"]!["dryRun"] = true;

            Action parseUnknownBudget = () => Parse(unknownBudget);
            Action parseMissingDirection = () => Parse(missingDirection);
            Action parseUnknownCapability = () => Parse(unknownCapability);

            parseUnknownBudget.Should().Throw<JsonException>().WithMessage("*frozen V2*");
            parseMissingDirection.Should().Throw<JsonException>().WithMessage("*frozen V2*");
            parseUnknownCapability.Should().Throw<JsonException>().WithMessage("*frozen V2*");
        }

        [Test]
        public void LeaseAndTransactionIdentifiersAreCanonicalLowercaseNonNilUuidsOrNull() {
            foreach (var bad in new JToken[] {
                "10000000-0000-0000-0000-00000000000A",
                "{10000000-0000-0000-0000-000000000001}",
                "00000000-0000-0000-0000-000000000000",
                "",
                1
            }) {
                var root = ValidRoot();
                root["activeLeaseId"] = bad;
                Action parse = () => Parse(root);
                parse.Should().Throw<JsonException>();
            }
        }

        [Test]
        public void RuntimeConstraintsDoNotExposeMotionExecutor() {
            foreach (var property in typeof(UpasSupervisorCoarseRuntimeConstraints).GetProperties()) {
                property.PropertyType.Should().NotBe(typeof(IAutomatedMoveExecutor));
                property.PropertyType.Should().NotBe(typeof(IPolarAlignmentSystemVM));
            }
        }

        private static UpasSupervisorCoarseRuntimeConstraints Parse(
                JObject root,
                Guid? callerLeaseId = null) =>
            UpasSupervisorCoarseRuntimeConstraintsParser.Parse(root, callerLeaseId);
    }
}
