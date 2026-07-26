using FluentAssertions;
using Newtonsoft.Json;
using NINA.Plugins.PolarAlignment;
using NUnit.Framework;
using System.Configuration;
using System.Reflection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace NINA.Plugins.PolarAlignment.Test {
    [TestFixture]
    public class UpasSupervisorBoundaryTest {
        private const string ValidDryRunStatus = """
            {
              "schemaVersion": 1,
              "mode": "phase2DryRunReadOnlyP20",
              "supervisorSessionId": "acaa2759-b829-41ec-9e93-59449a3ac38c",
              "lockedReason": null,
              "activeLease": null,
              "activeTransactionId": null,
              "witnessedAxes": ["az"],
              "remainingSessionTravelDegrees": 1.0,
              "capabilities": {
                "physicalMotion": false,
                "azDryRun": true,
                "alt": false,
                "p20Wss": true
              },
              "sensor": null
            }
            """;

        [Test]
        public void StrictStatusParserAcceptsFrozenPhase2DryRunContract() {
            var status = UpasSupervisorStatus.Parse(ValidDryRunStatus);

            status.SchemaVersion.Should().Be(1);
            status.Capabilities.PhysicalMotion.Should().BeFalse();
            status.Capabilities.AzDryRun.Should().BeTrue();
            status.WitnessedAxes.Should().Equal("az");
        }

        [Test]
        public void StrictStatusParserRejectsUnknownTopLevelAndCapabilityFields() {
            var topLevel = ValidDryRunStatus.Replace("\"sensor\": null", "\"sensor\": null, \"fallbackAllowed\": true");
            var capability = ValidDryRunStatus.Replace("\"p20Wss\": true", "\"p20Wss\": true, \"az\": true");

            Action parseTopLevel = () => UpasSupervisorStatus.Parse(topLevel);
            Action parseCapability = () => UpasSupervisorStatus.Parse(capability);

            parseTopLevel.Should().Throw<JsonException>();
            parseCapability.Should().Throw<JsonException>();
        }

        [Test]
        public void StrictStatusParserRejectsMissingPhysicalMotionCapability() {
            var missingDocument = Newtonsoft.Json.Linq.JObject.Parse(ValidDryRunStatus);
            ((Newtonsoft.Json.Linq.JObject)missingDocument["capabilities"]!)
                .Property("physicalMotion")!.Remove();

            Action parse = () => UpasSupervisorStatus.Parse(missingDocument.ToString());
            parse.Should().Throw<JsonException>();
        }

        [Test]
        public void ExternalSupervisorIsRequiredByDefault() {
            var property = typeof(NINA.Plugins.PolarAlignment.Properties.Settings)
                .GetProperty(nameof(NINA.Plugins.PolarAlignment.Properties.Settings.RequireExternalUpasSupervisorForAutomatedMoves));
            property.Should().NotBeNull();
            property!.GetCustomAttribute<DefaultSettingValueAttribute>()!.Value.Should().Be("True");
        }

        [Test]
        public void StrictStatusParserRejectsWhitespaceLockReason() {
            var whitespaceLock = ValidDryRunStatus.Replace(
                "\"lockedReason\": null",
                "\"lockedReason\": \"   \"");

            Action parse = () => UpasSupervisorStatus.Parse(whitespaceLock);
            parse.Should().Throw<JsonException>();
        }

        [TestCase("http://127.0.0.1:8443/")]
        [TestCase("https://user:secret@127.0.0.1:8443/")]
        [TestCase("https://127.0.0.1:8443/?mode=unsafe")]
        public void StatusEndpointMustBeUnadornedHttps(string endpoint) {
            Action build = () => HttpsUpasSupervisorStatusSource.BuildStatusUri(endpoint);
            build.Should().Throw<ArgumentException>();
        }

        [Test]
        public async Task StatusClientRequiresBearerBeforeSending() {
            var handler = new RecordingHandler(ValidDryRunStatus);
            var source = new HttpsUpasSupervisorStatusSource(
                new HttpClient(handler),
                "https://127.0.0.1:8443/",
                () => "");

            Func<Task> get = async () => await source.GetStatusAsync(CancellationToken.None);

            await get.Should().ThrowAsync<InvalidOperationException>();
            handler.RequestCount.Should().Be(0);
        }

        [Test]
        public async Task StatusClientSendsAuthenticatedStatusRequestOnly() {
            var handler = new RecordingHandler(ValidDryRunStatus);
            var source = new HttpsUpasSupervisorStatusSource(
                new HttpClient(handler),
                "https://127.0.0.1:8443/base",
                () => "secret");

            var status = await source.GetStatusAsync(CancellationToken.None);

            status.Capabilities.PhysicalMotion.Should().BeFalse();
            handler.RequestCount.Should().Be(1);
            handler.LastUri.Should().Be(new Uri("https://127.0.0.1:8443/base/v1/status"));
            handler.LastAuthorization.Should().Be("Bearer secret");
        }

        [Test]
        public async Task StatusBodyHasIndependentDeadlineAfterHeaders() {
            var handler = new RecordingHandler(ValidDryRunStatus) {
                ResponseContent = new NeverCompletingContent()
            };
            var source = new HttpsUpasSupervisorStatusSource(
                new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
                "https://127.0.0.1:8443/",
                () => "secret",
                TimeSpan.FromMilliseconds(25));

            Func<Task> get = async () => await source.GetStatusAsync(CancellationToken.None);

            await get.Should().ThrowAsync<TimeoutException>();
            handler.RequestCount.Should().Be(1);
        }

        [Test]
        public async Task SupervisorRequiredModeDeniesDryRunWithoutCallingLegacy() {
            var requirement = new MutableRequirement { IsRequired = true };
            var legacy = new RecordingMoveExecutor(AutomatedMoveExecutionResult.Verified("legacy"));
            var supervisor = new SupervisorRequiredAutomatedMoveExecutor(
                new StaticStatusSource(UpasSupervisorStatus.Parse(ValidDryRunStatus)));
            var router = new ModeSelectingAutomatedMoveExecutor(requirement, legacy, supervisor);

            var result = await router.ExecuteAsync(null, Axis.XAxis, 12, CancellationToken.None);

            result.PhysicalMotionVerified.Should().BeFalse();
            result.Reason.Should().Contain("physicalMotion=false");
            legacy.CallCount.Should().Be(0);
            router.RequiresLocalActuatorConnection.Should().BeFalse();
        }

        [Test]
        public async Task SupervisorRequiredModeStillDeniesFutureSuccessShapedStatus() {
            var futureStatus = UpasSupervisorStatus.Parse(
                ValidDryRunStatus.Replace("\"physicalMotion\": false", "\"physicalMotion\": true"));
            var supervisor = new SupervisorRequiredAutomatedMoveExecutor(new StaticStatusSource(futureStatus));

            var result = await supervisor.ExecuteAsync(null, Axis.XAxis, 12, CancellationToken.None);

            result.PhysicalMotionVerified.Should().BeFalse();
            result.Reason.Should().Contain("not commissioned");
        }

        [Test]
        public async Task SupervisorExecutorRejectsUnsupportedAxisWithoutMotion() {
            var supervisor = new SupervisorRequiredAutomatedMoveExecutor(
                new StaticStatusSource(UpasSupervisorStatus.Parse(
                    ValidDryRunStatus.Replace("\"physicalMotion\": false", "\"physicalMotion\": true"))));

            var result = await supervisor.ExecuteAsync(null, Axis.ZAxis, 12, CancellationToken.None);

            result.PhysicalMotionVerified.Should().BeFalse();
            result.Reason.Should().Contain("Unsupported");
        }

        [Test]
        public async Task AltitudeCapabilityDenialPrecedesUncommissionedSubmission() {
            var supervisor = new SupervisorRequiredAutomatedMoveExecutor(
                new StaticStatusSource(UpasSupervisorStatus.Parse(
                    ValidDryRunStatus.Replace("\"physicalMotion\": false", "\"physicalMotion\": true"))));

            var result = await supervisor.ExecuteAsync(null, Axis.YAxis, 12, CancellationToken.None);

            result.PhysicalMotionVerified.Should().BeFalse();
            result.Reason.Should().Contain("altitude motion is disabled");
        }

        [Test]
        public async Task CancellationNeverFallsBackToLegacy() {
            var requirement = new MutableRequirement { IsRequired = true };
            var legacy = new RecordingMoveExecutor(AutomatedMoveExecutionResult.Verified("legacy"));
            var supervisor = new SupervisorRequiredAutomatedMoveExecutor(new CancellingStatusSource());
            var router = new ModeSelectingAutomatedMoveExecutor(requirement, legacy, supervisor);

            Func<Task> execute = async () =>
                await router.ExecuteAsync(null, Axis.XAxis, 12, new CancellationToken(true));

            await execute.Should().ThrowAsync<OperationCanceledException>();
            legacy.CallCount.Should().Be(0);
        }

        [Test]
        public async Task LegacyModeRetainsExistingExecutorPath() {
            var requirement = new MutableRequirement { IsRequired = false };
            var legacy = new RecordingMoveExecutor(AutomatedMoveExecutionResult.Verified("legacy"));
            var supervisor = new SupervisorRequiredAutomatedMoveExecutor(new CancellingStatusSource());
            var router = new ModeSelectingAutomatedMoveExecutor(requirement, legacy, supervisor);

            var result = await router.ExecuteAsync(null, Axis.XAxis, 12, CancellationToken.None);

            result.PhysicalMotionVerified.Should().BeTrue();
            legacy.CallCount.Should().Be(1);
            router.RequiresLocalActuatorConnection.Should().BeTrue();
        }

        [Test]
        public void SupervisorExecutorCannotHoldALegacyMoverReference() {
            var fields = typeof(SupervisorRequiredAutomatedMoveExecutor)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            fields.Should().NotContain(field =>
                typeof(IAutomatedMoveExecutor).IsAssignableFrom(field.FieldType)
                || typeof(IPolarAlignmentSystemVM).IsAssignableFrom(field.FieldType));
        }

        [Test]
        public void TppaAutomatedCallSitesDoNotInvokeManualOrLegacyNudgesDirectly() {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                && !File.Exists(Path.Combine(directory.FullName, "PolarAlignment", "TPAPAVM.cs"))) {
                directory = directory.Parent;
            }
            directory.Should().NotBeNull("the test runs from within the repository build tree");
            var source = File.ReadAllText(Path.Combine(directory!.FullName, "PolarAlignment", "TPAPAVM.cs"));

            source.Should().NotContain(".TryNudgeXForAutomation(");
            source.Should().NotContain(".TryNudgeY(");
            source.Should().Contain("automatedMoveExecutor.ExecuteAsync(upas, Axis.XAxis");
            source.Should().Contain("automatedMoveExecutor.ExecuteAsync(activeSystem, Axis.XAxis");
            source.Should().Contain("automatedMoveExecutor.ExecuteAsync(activeSystem, Axis.YAxis");
        }

        [Test]
        public void ReadinessRejectsLockedAndUnknownAxisStates() {
            var unlocked = UpasSupervisorStatus.Parse(
                ValidDryRunStatus.Replace("\"physicalMotion\": false", "\"physicalMotion\": true"));
            var locked = unlocked with { LockedReason = "operator_invalidated" };

            UpasSupervisorReadinessPolicy.Evaluate(locked, Axis.XAxis).IsReady.Should().BeFalse();
            UpasSupervisorReadinessPolicy.Evaluate(unlocked, (Axis)99).IsReady.Should().BeFalse();
        }

        private sealed class MutableRequirement : IExternalSupervisorRequirement {
            public bool IsRequired { get; set; }
        }

        private sealed class StaticStatusSource : IUpasSupervisorStatusSource {
            private readonly UpasSupervisorStatus status;
            public StaticStatusSource(UpasSupervisorStatus status) => this.status = status;
            public Task<UpasSupervisorStatus> GetStatusAsync(CancellationToken token) =>
                Task.FromResult(status);
        }

        private sealed class CancellingStatusSource : IUpasSupervisorStatusSource {
            public Task<UpasSupervisorStatus> GetStatusAsync(CancellationToken token) =>
                Task.FromCanceled<UpasSupervisorStatus>(
                    token.IsCancellationRequested ? token : new CancellationToken(true));
        }

        private sealed class RecordingMoveExecutor : IAutomatedMoveExecutor {
            private readonly AutomatedMoveExecutionResult result;
            public RecordingMoveExecutor(AutomatedMoveExecutionResult result) => this.result = result;
            public int CallCount { get; private set; }
            public bool RequiresLocalActuatorConnection => true;
            public Task<AutomatedMoveExecutionResult> ExecuteAsync(
                IPolarAlignmentSystemVM activeSystem,
                Axis axis,
                float logicalUnits,
                CancellationToken token) {
                CallCount++;
                return Task.FromResult(result);
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler {
            private readonly string response;
            public RecordingHandler(string response) => this.response = response;
            public HttpContent? ResponseContent { get; init; }
            public int RequestCount { get; private set; }
            public Uri? LastUri { get; private set; }
            public string? LastAuthorization { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) {
                RequestCount++;
                LastUri = request.RequestUri;
                LastAuthorization = request.Headers.Authorization?.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = ResponseContent ?? new StringContent(response, Encoding.UTF8, "application/json")
                });
            }
        }

        private sealed class NeverCompletingContent : HttpContent {
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
                Task.Delay(Timeout.InfiniteTimeSpan);

            protected override Task SerializeToStreamAsync(
                Stream stream,
                TransportContext? context,
                CancellationToken cancellationToken) =>
                Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            protected override bool TryComputeLength(out long length) {
                length = 0;
                return false;
            }
        }
    }
}
