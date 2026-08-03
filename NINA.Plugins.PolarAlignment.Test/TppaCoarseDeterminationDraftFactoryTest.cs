using System;
using System.Linq;
using FluentAssertions;
using NINA.Core.Enum;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class TppaCoarseDeterminationDraftFactoryTest {
        [Test]
        public void Create_UsesOnlyCommissionedIdentitiesAndCovariance() {
            var authority = Authority();
            var started = Utc(0);
            var draft = TppaCoarseDeterminationDraftFactory.Create(
                Guid.NewGuid(), started, Utc(30), true, authority,
                Solves(started), 4.5, -2.0);

            draft.TargetSkyArcId.Should().Be(authority.TargetSkyArcId);
            draft.RepositoryHead.Should().Be(authority.RepositoryHead);
            draft.SourceSolves.Should().HaveCount(3);
            draft.SourceSolves.Select(item => item.PierSide)
                .Should().OnlyContain(item => item == "pierEast");
            draft.CovarianceAzAzSquareMinutes.Should()
                .Be(authority.CovarianceAzAzSquareMinutes);
        }

        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void Create_RejectsNonstationaryMountEvidence(
                bool connected, bool tracking, bool slewing) {
            var started = Utc(0);
            var solves = Solves(started);
            solves[1] = solves[1] with {
                MountConnected = connected,
                TrackingEnabled = tracking,
                Slewing = slewing
            };

            var action = () => TppaCoarseDeterminationDraftFactory.Create(
                Guid.NewGuid(), started, Utc(30), true, Authority(),
                solves, 1.0, 1.0);

            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*stationary tracked mount*");
        }

        [Test]
        public void Create_RejectsFalsePoleAndSolverMismatch() {
            var started = Utc(0);
            var falsePole = () => TppaCoarseDeterminationDraftFactory.Create(
                Guid.NewGuid(), started, Utc(30), false, Authority(),
                Solves(started), 1.0, 1.0);
            falsePole.Should().Throw<InvalidOperationException>()
                .WithMessage("*true-pole*");

            var solves = Solves(started);
            solves[2] = solves[2] with { SolverIdentity = "other" };
            var mismatch = () => TppaCoarseDeterminationDraftFactory.Create(
                Guid.NewGuid(), started, Utc(30), true, Authority(),
                solves, 1.0, 1.0);
            mismatch.Should().Throw<InvalidOperationException>()
                .WithMessage("*commissioned authority*");
        }

        private static TppaCapturedSolveEvidence[] Solves(DateTime started) =>
            Enumerable.Range(0, 3).Select(index => new TppaCapturedSolveEvidence(
                Guid.NewGuid(), Hex(index + 1), Hex(index + 11),
                started.AddSeconds(index * 8 + 1), 2000,
                started.AddSeconds(index * 8 + 2),
                started.AddSeconds(index * 8), true, true, false,
                10.0 + index, 20.0, PierSide.pierEast, "solver-v1"))
            .ToArray();

        private static TppaCommissionedCovarianceAuthority Authority() => new(
            Hex(21), Guid.NewGuid(), Utc(-3600), Utc(3600),
            new string('a', 40), Hex(22), "hardware-v1", Hex(23),
            "load-v1", "solver-v1", "catalog-v1", "arc-v1",
            20.0, 45.0, 1.0, 0.1, 2.0, Hex(24), 20, 20, 0.95);

        private static DateTime Utc(int seconds) =>
            new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);

        private static string Hex(int value) => value.ToString("x64");
    }
}
