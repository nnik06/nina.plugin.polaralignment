using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NINA.Plugins.PolarAlignment {
    internal sealed class TppaCoarsePlanningReceipt {
        private readonly JObject unsignedPayload;

        private TppaCoarsePlanningReceipt(JObject unsignedPayload) {
            this.unsignedPayload = (JObject)unsignedPayload.DeepClone();
            ReceiptContentSha256 = Digest(this.unsignedPayload.ToString(Formatting.None));
        }

        public string ReceiptContentSha256 { get; }

        public static TppaCoarsePlanningReceipt Create(
                UpasSupervisorCoarsePlanningEvidence evidence,
                TppaCoarseErrorEvidence error,
                TppaCoarseVectorPlan plan) {
            if (evidence == null) {
                throw new ArgumentNullException(nameof(evidence));
            }
            if (error == null) {
                throw new ArgumentNullException(nameof(error));
            }
            if (plan == null) {
                throw new ArgumentNullException(nameof(plan));
            }
            evidence.RequireFreshAtPlanEmission(0.0);

            var envelope = evidence.Envelope;
            var calibration = evidence.ResponseCalibration;
            var payload = new JObject {
                ["schemaVersion"] = 1,
                ["receiptKind"] = "tppaCoarsePlanningOnly",
                ["motionAuthorityIncluded"] = false,
                ["coordinateConvention"] = envelope.CoordinateConvention,
                ["supervisorEvidence"] = new JObject {
                    ["evidenceId"] = envelope.EvidenceId,
                    ["responseContentSha256"] = evidence.ResponseContentSha256,
                    ["clientRequestNonce"] = envelope.ClientRequestNonce,
                    ["supervisorSessionId"] = envelope.SupervisorSessionId.ToString("D"),
                    ["supervisorBootId"] = envelope.SupervisorBootId.ToString("D"),
                    ["activeLeaseId"] = evidence.Runtime.ActiveLeaseId?.ToString("D") is string lease
                        ? JValue.CreateString(lease)
                        : JValue.CreateNull()
                },
                ["responseCalibration"] = new JObject {
                    ["calibrationId"] = calibration.CalibrationId.ToString("D"),
                    ["artifactSha256"] = calibration.ArtifactSha256
                },
                ["tppaError"] = new JObject {
                    ["azimuthMinutes"] = error.AzimuthMinutes,
                    ["altitudeMinutes"] = error.AltitudeMinutes,
                    ["covarianceSquareMinutes"] = new JArray {
                        new JArray(error.CovarianceAzAzSquareMinutes,
                            error.CovarianceAzAltSquareMinutes),
                        new JArray(error.CovarianceAzAltSquareMinutes,
                            error.CovarianceAltAltSquareMinutes)
                    }
                },
                ["plan"] = new JObject {
                    ["isAuthorizedForPlanning"] = plan.IsAuthorized,
                    ["requiresMove"] = plan.RequiresMove,
                    ["requestedDeltaDegrees"] = new JObject {
                        ["az"] = plan.RequestedAzimuthDeltaDegrees,
                        ["alt"] = plan.RequestedAltitudeDeltaDegrees
                    },
                    ["modelStandardUncertaintyDegrees"] = new JObject {
                        ["az"] = plan.AzimuthCorrectionModelStandardUncertaintyDegrees,
                        ["alt"] = plan.AltitudeCorrectionModelStandardUncertaintyDegrees
                    },
                    ["additiveBoundDegrees"] = new JObject {
                        ["az"] = plan.AzimuthAdditiveBoundDegrees,
                        ["alt"] = plan.AltitudeAdditiveBoundDegrees
                    },
                    ["expandedPathMillidegrees"] = new JObject {
                        ["az"] = Interval(plan.AzimuthExpandedPath),
                        ["alt"] = Interval(plan.AltitudeExpandedPath)
                    },
                    ["projectedResidualMinutes"] = FiniteOrNull(plan.ProjectedResidualMinutes),
                    ["projectedResidualUpperMinutes"] = FiniteOrNull(plan.ProjectedResidualUpperMinutes),
                    ["reason"] = plan.Reason
                }
            };
            return new TppaCoarsePlanningReceipt(payload);
        }

        public string ToJson() {
            var result = (JObject)unsignedPayload.DeepClone();
            result["receiptContentSha256"] = ReceiptContentSha256;
            return result.ToString(Formatting.None);
        }

        public bool VerifyIntegrity() =>
            string.Equals(ReceiptContentSha256,
                Digest(unsignedPayload.ToString(Formatting.None)), StringComparison.Ordinal);

        private static JObject Interval(UpasMillidegreeInterval interval) => new() {
            ["lower"] = interval.Lower,
            ["upper"] = interval.Upper
        };

        private static JToken FiniteOrNull(double value) =>
            double.IsFinite(value) ? new JValue(value) : JValue.CreateNull();

        private static string Digest(string value) {
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(Encoding.UTF8.GetBytes(value))
                .Select(item => item.ToString("x2")));
        }
    }
}
