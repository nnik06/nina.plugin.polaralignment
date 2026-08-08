using System;
using System.Collections.Generic;
using NINA.Core.Utility;

namespace NINA.Plugins.PolarAlignment {
    /// <summary>
    /// Learns a local linear actuator model from observed error changes and uses that model
    /// to choose bounded correction moves.
    ///
    /// The local model is:
    /// <c>delta_error ~= A * command</c>
    /// where the error vector is <c>[azimuth, altitude]</c> in degrees and the command vector
    /// is <c>[X, Y]</c> in the logical nudge units exposed by the selected hardware system.
    /// </summary>
    internal sealed class AutomatedAdjustmentController {
        /// <summary>
        /// Probe moves are intentionally small and conservative. They exist to identify
        /// the local actuator response, not to make rapid progress.
        /// </summary>
        private const double DefaultXProbeMagnitude = 4.0;
        private const double UpasXProbeMagnitude = 8.0;
        private const double DefaultYProbeMagnitude = 2.0;
        /// <summary>
        /// Commands below this magnitude are ignored to avoid chattering around zero and to
        /// prevent learning from motions that are likely smaller than backlash or slop.
        /// </summary>
        private const double MinimumMoveMagnitude = 0.05;
        /// <summary>
        /// Do not learn from measured changes that are comparable to solve jitter.
        /// </summary>
        private const double MinimumSampleResponseDegrees = 0.25 / 60.0;
        /// <summary>
        /// Responses larger than this are likely hidden compensation, slip recovery, or a bad solve.
        /// Do not let those outliers dominate the learned local model.
        /// </summary>
        private const double MaximumSampleResponseDegreesPerUnit = 15.0 / 60.0;
        /// <summary>
        /// Below this per-unit response, a learned actuator column is too weak to trust.
        /// </summary>
        private const double MinimumAxisResponseDegreesPerUnit = 0.08 / 60.0;
        /// <summary>
        /// Cross-axis response smaller than this fraction of the main response is treated
        /// as noise rather than real authority over the other polar-alignment axis.
        /// </summary>
        private const double MaximumCrossAxisToPrimaryRatio = 0.35;
        /// <summary>
        /// If the remaining total error is already below the solve-to-solve noise floor, probing
        /// risks making a solved alignment worse just to collect another sample.
        /// </summary>
        private const double MinimumResidualForProbeDegrees = 0.25 / 60.0;
        private const int MaxRejectedProbeAttempts = 5;
        private const int MaxConsecutiveUnsafeModelSkips = 2;
        private const double ProbeRetryScale = 1.5;
        /// <summary>
        /// Maximum correction magnitude issued in a single solve or move cycle.
        /// Large residuals are intentionally corrected over multiple iterations.
        /// </summary>
        private const double MaximumMoveMagnitude = 8.0;
        private const double MaximumProbeMagnitude = 20.0;
        private const double AzimuthAcquisitionThresholdDegrees = 0.5 / 60.0;
        private const double AzimuthAcquisitionAltitudeGuardDegrees = 5.0 / 60.0;
        private const double AzimuthAcquisitionDominanceRatio = 3.0;
        private const double LargeAzimuthDominanceThresholdDegrees = 15.0 / 60.0;
        private const double XAcquisitionClearanceMagnitude = 24.0;
        private const double ConfirmedXCorrectionGain = 0.65;
        private const double MaximumConfirmedXMoveMagnitude = 20.0;
        private const double NearConvergenceXResidualDegrees = 8.0 / 60.0;
        private const double NearConvergenceMaximumConfirmedXMoveMagnitude = 6.0;
        private const double ModerateConvergenceXResidualDegrees = 15.0 / 60.0;
        private const double ModerateConvergenceMaximumConfirmedXMoveMagnitude = 9.0;
        private const double MinimumTrustedXCorrectionResidualDegrees = 2.0 / 60.0;
        private const double MinimumEarlyXEngagementImprovementDegrees = 3.0 / 60.0;
        private const int MinimumEarlyXEngagementEvidenceSamples = 2;
        private const double MinimumEarlyXEngagementWorseningDegrees = 5.0 / 60.0;
        private const int MaximumEarlyXReversalsPerAcquisition = 1;
        private const double MaximumAcceptedZeroCrossingOvershootDegrees = 3.0 / 60.0;
        private const double MaximumRememberedResponseCorrectionResidualDegrees = 3.0 / 60.0;
        private const double RememberedResponseCorrectionGain = 0.65;
        private const double MaximumRememberedResponseMoveMagnitude = 6.0;
        private const double MinimumRememberedResponseMoveMagnitude = 2.0;
        private const int MaxRememberedResponsesPerDirection = 5;
        private const double NearTargetXMoveLimitResidualDegrees = 3.0 / 60.0;
        private const double NearTargetMaximumXMoveMagnitude = 4.0;
        private const double LargeAltitudeResidualDegrees = 30.0 / 60.0;
        private const double MaximumConfirmedYMoveMagnitude = 16.0;
        private const int MinimumConfirmedYResponseSamples = 3;
        private const double MaximumConfirmedYResponseSpreadRatio = 1.75;
        /// <summary>
        /// Small damping term used as a numerical floor and as regularization when
        /// inverting the local response model.
        /// </summary>
        private const double NormalEquationDamping = 1e-6;
        /// <summary>
        /// A candidate move must predict at least a slight reduction in total error before
        /// the controller will accept it.
        /// </summary>
        private const double MinimumExpectedImprovementFactor = 0.99;
        private const double MinimumExpectedImprovementCostFactor = MinimumExpectedImprovementFactor;
        /// <summary>
        /// The controller should strongly avoid moving an axis that is already within
        /// normal alignment tolerance unless the total predicted improvement is decisive.
        /// </summary>
        private const double SolvedAxisGuardDegrees = 1.0 / 60.0;
        private const double SolvedAxisPenaltyFactor = 10.0;
        private const double AltitudeProtectionDuringAzimuthCorrectionDegrees = 2.0 / 60.0;
        /// <summary>
        /// If a non-probe move makes the measured total error materially worse, the learned
        /// model is treated as stale and discarded.
        /// </summary>
        private const double ModelResetWorseningFactor = 1.05;
        /// <summary>
        /// Maximum number of recent identification samples retained in the local model.
        /// </summary>
        private const int MaxSamples = 12;

        private readonly Queue<ResponseSample> samples = new Queue<ResponseSample>();
        private AutomatedAdjustmentObservation currentObservation;
        private PendingPlan pendingPlan;
        private int consecutiveUnsafeModelSkips;
        private int rejectedXProbeCount;
        private int rejectedYProbeCount;
        private int? lastExecutedXDirection;
        private int? pendingXReversalDirection;
        private int? preferredXAcquisitionDirection;
        private int? learnedXAcquisitionDirection;
        private double? rememberedXAzimuthDeltaPerUnit;
        private readonly Queue<double> positiveXAzimuthResponses = new Queue<double>();
        private readonly Queue<double> negativeXAzimuthResponses = new Queue<double>();
        private int? committedXDirection;
        private double committedXTravelSinceReversal;
        private double azimuthTravelUsedDegrees;
        private bool altitudeTravelGuardInitialized;
        private bool altitudeTravelGuardWasConfirmed;
        private double altitudePossibleMinimumDegrees;
        private double altitudePossibleMaximumDegrees;
        private bool xAcquisitionConfirmed;
        private int rejectedWorseningXDirectionMask;
        private int earlyXImprovementEvidenceCount;
        private int earlyXWorseningEvidenceCount;
        private int earlyXReversalCount;
        private bool dominantXAcquisitionBlocked;
        private bool hasObservation;

        public AutomatedAdjustmentController(bool useUpasEngagementController = false) {
            UseUpasEngagementController = useUpasEngagementController;
        }

        public bool UseUpasEngagementController { get; set; }
        private CalibratedDirectFullTravelRoute calibratedDirectFullTravelRoute;

        public bool AzimuthTravelGuardEnabled { get; set; }
        public bool AzimuthTravelGuardConfirmed { get; set; }
        public double AzimuthTravelLimitDegrees { get; set; }
        public double AzimuthDegreesPerXUnit { get; set; } = 0.025;
        public double AzimuthTravelUsedDegrees => azimuthTravelUsedDegrees;
        public bool AltitudeTravelGuardEnabled { get; private set; }
        public bool AltitudeTravelGuardConfirmed { get; private set; }
        public double AltitudeStartingPositionDegrees { get; private set; }
        public double AltitudeMinimumDegrees { get; private set; } = -5.0;
        public double AltitudeMaximumDegrees { get; private set; } = 5.0;
        public double AltitudeDegreesPerYUnit { get; private set; } = 0.022;
        public int AltitudeCommandDirectionMultiplier { get; private set; } = 1;
        public double AltitudePossibleMinimumDegrees => altitudePossibleMinimumDegrees;
        public double AltitudePossibleMaximumDegrees => altitudePossibleMaximumDegrees;

        public int SampleCount => samples.Count;

        /// <summary>
        /// Gets whether the current sample set is rich enough and well-conditioned enough
        /// to estimate a two-axis local response model.
        /// </summary>
        public bool HasResponseModel {
            get => TryBuildResponseModel(out _);
        }

        public bool HasRememberedXAzimuthResponse => rememberedXAzimuthDeltaPerUnit.HasValue;

        /// <summary>
        /// True while the most recent automated azimuth action has not produced a
        /// mechanically trustworthy response yet. Completion logic uses this as a
        /// diagnostic signal while it validates a below-tolerance solve without moving.
        /// </summary>
        public bool RequiresCompletionValidation => pendingPlan != null
                                                    || pendingXReversalDirection.HasValue
                                                    || IsXAcquisitionCommitmentActive();

        /// <summary>
        /// Clears all learned actuator response state and any pending move bookkeeping.
        /// </summary>
        public void Reset() {
            samples.Clear();
            currentObservation = null;
            pendingPlan = null;
            consecutiveUnsafeModelSkips = 0;
            azimuthTravelUsedDegrees = 0;
            ResetAcquisitionState(preserveLearnedDirection: false);
            hasObservation = false;
        }

        public void SeedUpasAzimuthResponseMemory(double azimuthDeltaPerXUnit) {
            if (!UseUpasEngagementController || !IsTrustedXAzimuthResponse(azimuthDeltaPerXUnit)) {
                return;
            }

            rememberedXAzimuthDeltaPerUnit = azimuthDeltaPerXUnit;
            positiveXAzimuthResponses.Clear();
            negativeXAzimuthResponses.Clear();
            positiveXAzimuthResponses.Enqueue(azimuthDeltaPerXUnit);
            negativeXAzimuthResponses.Enqueue(azimuthDeltaPerXUnit);
            Logger.Info($"Seeded UPAS azimuth response memory: Az/X={Math.Round(azimuthDeltaPerXUnit * 60.0, 4)}'/unit.");
        }

        public void SeedXSeating(int logicalDirection) {
            if (!UseUpasEngagementController || logicalDirection == 0) {
                return;
            }

            var direction = Math.Sign(logicalDirection);
            committedXDirection = direction;
            lastExecutedXDirection = direction;
            preferredXAcquisitionDirection = direction;
            committedXTravelSinceReversal = XAcquisitionClearanceMagnitude;
            xAcquisitionConfirmed = false;
            Logger.Info($"Seeded UPAS azimuth seating state after pre-seat move. Logical X direction: {direction}.");
        }

        public bool TryGetTrustedXAzimuthResponse(out double azimuthDeltaPerXUnit) {
            if (xAcquisitionConfirmed
                && rememberedXAzimuthDeltaPerUnit.HasValue
                && IsTrustedXAzimuthResponse(rememberedXAzimuthDeltaPerUnit.Value)) {
                azimuthDeltaPerXUnit = rememberedXAzimuthDeltaPerUnit.Value;
                return true;
            }

            if (xAcquisitionConfirmed && TryEstimateXAzimuthResponse(out azimuthDeltaPerXUnit)) {
                return true;
            }

            azimuthDeltaPerXUnit = 0;
            return false;
        }

        public void ConfigureAltitudeTravelGuard(
            bool enabled,
            bool confirmed,
            double startingPositionDegrees,
            double minimumDegrees,
            double maximumDegrees,
            double degreesPerYUnit,
            int commandDirectionMultiplier) {
            var normalizedDirection = commandDirectionMultiplier < 0 ? -1 : 1;
            var configurationChanged =
                AltitudeStartingPositionDegrees != startingPositionDegrees
                || AltitudeMinimumDegrees != minimumDegrees
                || AltitudeMaximumDegrees != maximumDegrees
                || AltitudeDegreesPerYUnit != degreesPerYUnit
                || AltitudeCommandDirectionMultiplier != normalizedDirection;

            AltitudeTravelGuardEnabled = enabled;
            AltitudeTravelGuardConfirmed = confirmed;
            AltitudeStartingPositionDegrees = startingPositionDegrees;
            AltitudeMinimumDegrees = minimumDegrees;
            AltitudeMaximumDegrees = maximumDegrees;
            AltitudeDegreesPerYUnit = degreesPerYUnit;
            AltitudeCommandDirectionMultiplier = normalizedDirection;

            if (!enabled || !confirmed) {
                altitudeTravelGuardInitialized = false;
                altitudeTravelGuardWasConfirmed = confirmed;
                return;
            }

            if (configurationChanged || !altitudeTravelGuardInitialized || !altitudeTravelGuardWasConfirmed) {
                altitudePossibleMinimumDegrees = startingPositionDegrees;
                altitudePossibleMaximumDegrees = startingPositionDegrees;
                altitudeTravelGuardInitialized = true;
                Logger.Info($"UPAS altitude travel guard initialized from visually confirmed ALT {Math.Round(startingPositionDegrees, 3)} deg; permitted range {Math.Round(minimumDegrees, 3)}..{Math.Round(maximumDegrees, 3)} deg; scale {Math.Round(degreesPerYUnit, 6)} deg/Y unit; command direction multiplier {normalizedDirection}.");
            }

            altitudeTravelGuardWasConfirmed = true;
        }

        public void ConfigureCalibratedDirectFullTravelRoute(
            bool enabled,
            bool operatorConfirmed,
            double azimuthStartingPositionDegrees,
            double azimuthMinimumDegrees,
            double azimuthMaximumDegrees,
            double altitudeStartingPositionDegrees,
            double altitudeMinimumDegrees,
            double altitudeMaximumDegrees,
            double azimuthDeltaPerXUnitDegrees,
            double azimuthDeltaPerYUnitDegrees,
            double altitudeDeltaPerXUnitDegrees,
            double altitudeDeltaPerYUnitDegrees,
            double maximumXUnitsPerMove,
            double maximumYUnitsPerMove) {
            calibratedDirectFullTravelRoute = enabled && operatorConfirmed
                ? new CalibratedDirectFullTravelRoute(
                    azimuthStartingPositionDegrees,
                    azimuthMinimumDegrees,
                    azimuthMaximumDegrees,
                    altitudeStartingPositionDegrees,
                    altitudeMinimumDegrees,
                    altitudeMaximumDegrees,
                    new ResponseModel(
                        azimuthDeltaPerXUnitDegrees,
                        azimuthDeltaPerYUnitDegrees,
                        altitudeDeltaPerXUnitDegrees,
                        altitudeDeltaPerYUnitDegrees),
                    maximumXUnitsPerMove,
                    maximumYUnitsPerMove)
                : null;
        }

        public bool CanExecuteAltitudeTravel(double yMagnitude, out string reason) {
            reason = null;
            if (!UseUpasEngagementController || !AltitudeTravelGuardEnabled || Math.Abs(yMagnitude) <= 0) {
                return true;
            }

            if (!AltitudeTravelGuardConfirmed || !altitudeTravelGuardInitialized) {
                reason = "UPAS altitude travel guard is enabled, but the physical ALT marker confirmation is not checked.";
                return false;
            }

            if (!double.IsFinite(AltitudeMinimumDegrees)
                || !double.IsFinite(AltitudeMaximumDegrees)
                || AltitudeMinimumDegrees >= AltitudeMaximumDegrees) {
                reason = "UPAS altitude travel guard has an invalid physical range.";
                return false;
            }

            if (!double.IsFinite(AltitudeStartingPositionDegrees)
                || AltitudeStartingPositionDegrees < AltitudeMinimumDegrees
                || AltitudeStartingPositionDegrees > AltitudeMaximumDegrees) {
                reason = "UPAS altitude travel guard starting position is outside its physical range.";
                return false;
            }

            var degreesPerUnit = Math.Abs(AltitudeDegreesPerYUnit);
            if (!double.IsFinite(degreesPerUnit) || degreesPerUnit <= 0) {
                reason = "UPAS altitude travel guard has an invalid degrees-per-Y-unit calibration.";
                return false;
            }

            var physicalDelta = yMagnitude * degreesPerUnit * AltitudeCommandDirectionMultiplier;
            var predictedMinimum = altitudePossibleMinimumDegrees + Math.Min(0, physicalDelta);
            var predictedMaximum = altitudePossibleMaximumDegrees + Math.Max(0, physicalDelta);
            if (predictedMinimum < AltitudeMinimumDegrees || predictedMaximum > AltitudeMaximumDegrees) {
                reason = $"UPAS altitude travel guard refused Y {Math.Round(yMagnitude, 3)} because the conservative physical interval would become {Math.Round(predictedMinimum, 3)}..{Math.Round(predictedMaximum, 3)} deg outside {Math.Round(AltitudeMinimumDegrees, 3)}..{Math.Round(AltitudeMaximumDegrees, 3)} deg.";
                return false;
            }

            return true;
        }

        public bool CanExecuteAzimuthTravel(double xMagnitude, out string reason) {
            reason = null;
            if (!UseUpasEngagementController || !AzimuthTravelGuardEnabled || Math.Abs(xMagnitude) <= 0) {
                return true;
            }

            if (!AzimuthTravelGuardConfirmed) {
                reason = "UPAS azimuth travel guard is enabled, but the visual marker confirmation is not checked.";
                return false;
            }

            var limitDegrees = Math.Abs(AzimuthTravelLimitDegrees);
            if (limitDegrees <= 0) {
                reason = "UPAS azimuth travel guard is enabled, but its travel limit is zero.";
                return false;
            }

            var degreesPerUnit = Math.Abs(AzimuthDegreesPerXUnit);
            if (degreesPerUnit <= 0) {
                reason = "UPAS azimuth travel guard is enabled, but its degrees-per-unit calibration is zero.";
                return false;
            }

            var requestedDegrees = Math.Abs(xMagnitude) * degreesPerUnit;
            if (azimuthTravelUsedDegrees + requestedDegrees > limitDegrees + 1e-9) {
                reason = $"UPAS azimuth travel guard refused X {Math.Round(xMagnitude, 3)} because it would use {Math.Round(azimuthTravelUsedDegrees + requestedDegrees, 3)} deg of the {Math.Round(limitDegrees, 3)} deg visual-marker budget.";
                return false;
            }

            return true;
        }

        public void NoteExternalAzimuthTravel(double xMagnitude, string context) {
            NoteAzimuthTravelUsed(xMagnitude, context);
        }

        public void RebaseObservation(double azimuthErrorDegrees, double altitudeErrorDegrees) {
            pendingPlan = null;
            pendingXReversalDirection = null;
            consecutiveUnsafeModelSkips = 0;
            currentObservation = new AutomatedAdjustmentObservation(azimuthErrorDegrees, altitudeErrorDegrees);
            hasObservation = true;
            Logger.Info($"Rebased automated polar-alignment controller to fresh three-point error: Az={Math.Round(azimuthErrorDegrees * 60.0, 3)}', Alt={Math.Round(altitudeErrorDegrees * 60.0, 3)}'.");
        }

        /// <summary>
        /// Feeds the latest measured residual error into the controller.
        ///
        /// If a move was executed in the previous cycle, this method converts the before/after
        /// difference into one identification sample of the local actuator response.
        /// </summary>
        public void UpdateObservation(double azimuthErrorDegrees, double altitudeErrorDegrees) {
            var latestObservation = new AutomatedAdjustmentObservation(azimuthErrorDegrees, altitudeErrorDegrees);

            if (pendingPlan != null) {
                var deltaAzimuth = latestObservation.AzimuthErrorDegrees - pendingPlan.BeforeMoveObservation.AzimuthErrorDegrees;
                var deltaAltitude = latestObservation.AltitudeErrorDegrees - pendingPlan.BeforeMoveObservation.AltitudeErrorDegrees;
                var responseMagnitude = Math.Sqrt(deltaAzimuth * deltaAzimuth + deltaAltitude * deltaAltitude);
                var commandMagnitude = Math.Sqrt(pendingPlan.Plan.XMagnitude * pendingPlan.Plan.XMagnitude
                                                 + pendingPlan.Plan.YMagnitude * pendingPlan.Plan.YMagnitude);
                var responsePerUnit = commandMagnitude > 0 ? responseMagnitude / commandMagnitude : 0;

                if (ShouldRejectDominantAzimuthXSample(latestObservation, out var dominantAzimuthRejectionReason, out var countAsProbeRejection)) {
                    Logger.Warning(dominantAzimuthRejectionReason);
                    samples.Clear();
                    consecutiveUnsafeModelSkips = 0;
                    if (countAsProbeRejection) {
                        IncrementXAcquisitionRejection();
                    }
                } else if (responseMagnitude < MinimumSampleResponseDegrees) {
                    Logger.Info($"Rejected automated polar-alignment sample below response floor. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, Y={Math.Round(pendingPlan.Plan.YMagnitude, 3)}, response={Math.Round(responseMagnitude * 60.0, 3)}'");
                    if (pendingPlan.Plan.IsProbe) {
                        IncrementProbeRejectionCount(pendingPlan.Plan);
                    }
                } else if (responsePerUnit > MaximumSampleResponseDegreesPerUnit) {
                    Logger.Warning($"Rejected automated polar-alignment sample as an outlier. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, Y={Math.Round(pendingPlan.Plan.YMagnitude, 3)}, response={Math.Round(responseMagnitude * 60.0, 3)}', response/unit={Math.Round(responsePerUnit * 60.0, 3)}'/unit.");
                    samples.Clear();
                    consecutiveUnsafeModelSkips = 0;
                    ResetAcquisitionState(preserveLearnedDirection: true);
                } else if (!pendingPlan.Plan.IsProbe
                           && latestObservation.TotalErrorDegrees > pendingPlan.BeforeMoveObservation.TotalErrorDegrees * ModelResetWorseningFactor
                           && latestObservation.TotalErrorDegrees - pendingPlan.BeforeMoveObservation.TotalErrorDegrees > MinimumSampleResponseDegrees) {
                    Logger.Warning("Rejected automated polar-alignment sample and reset the model because a corrective move made the measured error materially worse.");
                    samples.Clear();
                    consecutiveUnsafeModelSkips = 0;
                    ResetAcquisitionState(preserveLearnedDirection: true);
                } else {
                    consecutiveUnsafeModelSkips = 0;
                    if (ShouldSkipSampleForModel(pendingPlan.Plan, pendingPlan.BeforeMoveObservation)) {
                        Logger.Info($"Accepted automated polar-alignment movement but skipped model learning from a small X-only command. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, response={Math.Round(responseMagnitude * 60.0, 3)}'");
                    } else {
                        AddSample(new ResponseSample(pendingPlan.Plan.XMagnitude,
                                                     pendingPlan.Plan.YMagnitude,
                                                     deltaAzimuth,
                                                     deltaAltitude));
                        Logger.Info($"Accepted automated polar-alignment sample. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, Y={Math.Round(pendingPlan.Plan.YMagnitude, 3)}, response={Math.Round(responseMagnitude * 60.0, 3)}'");
                    }
                    if (pendingPlan.Plan.IsProbe) {
                        ResetProbeRejectionCount(pendingPlan.Plan);
                    }
                }

                pendingPlan = null;
            }

            currentObservation = latestObservation;
            hasObservation = true;
        }

        /// <summary>
        /// Creates the next automated move.
        ///
        /// Before the response matrix becomes observable, this returns small probe moves
        /// to learn the hardware sign and gain. After that, it returns the safest corrective
        /// move that is predicted to reduce the residual error norm.
        /// </summary>
        public AutomatedAdjustmentPlan CreatePlan() {
            if (!hasObservation) {
                return AutomatedAdjustmentPlan.Skip("No continuous error measurement is available yet.");
            }

            if (dominantXAcquisitionBlocked && ShouldUseXAcquisition(currentObservation)) {
                return AutomatedAdjustmentPlan.Skip("Both automated azimuth motor directions made the dominant azimuth residual worse. Check reversal settings or mechanics before continuing automation.");
            }

            if (TryCreateCalibratedDirectFullTravelPlan(currentObservation, out var calibratedPlan)) {
                return ApplyAzimuthTravelGuard(calibratedPlan);
            }

            if (TryCreateXEngagementPlan(currentObservation, out var engagementPlan)) {
                consecutiveUnsafeModelSkips = 0;
                return ApplyAzimuthTravelGuard(engagementPlan);
            }

            if (TryCreateRememberedDominantAzimuthPlan(currentObservation, out var rememberedPlan)) {
                consecutiveUnsafeModelSkips = 0;
                return ApplyAzimuthTravelGuard(rememberedPlan);
            }

            if (TryCreateConfirmedDominantAzimuthPlan(currentObservation, out var dominantAzimuthPlan)) {
                dominantAzimuthPlan = EnforceXReversalClearance(dominantAzimuthPlan);
                if (ShouldDebounceXDirectionReversal(dominantAzimuthPlan, out var debouncePlan)) {
                    return debouncePlan;
                }

                consecutiveUnsafeModelSkips = 0;
                return ApplyAzimuthTravelGuard(dominantAzimuthPlan);
            }

            if (TryBuildResponseModel(out var responseModel)) {
                var correctivePlan = CreateCorrectivePlan(responseModel, currentObservation);
                if (correctivePlan.HasMovement) {
                    correctivePlan = EnforceXReversalClearance(correctivePlan);
                    if (ShouldDebounceXDirectionReversal(correctivePlan, out var debouncePlan)) {
                        return debouncePlan;
                    }

                    consecutiveUnsafeModelSkips = 0;
                    return ApplyAzimuthTravelGuard(correctivePlan);
                }

                consecutiveUnsafeModelSkips++;
                if (consecutiveUnsafeModelSkips >= MaxConsecutiveUnsafeModelSkips) {
                    Logger.Warning("Automated polar-alignment model reset after repeated unsafe correction skips.");
                    samples.Clear();
                    consecutiveUnsafeModelSkips = 0;
                    ResetAcquisitionState(preserveLearnedDirection: true);
                    return ApplyAzimuthTravelGuard(CreateProbePlan());
                }

                return correctivePlan;
            }

            return ApplyAzimuthTravelGuard(CreateProbePlan());
        }

        private bool TryCreateCalibratedDirectFullTravelPlan(
            AutomatedAdjustmentObservation observation,
            out AutomatedAdjustmentPlan plan) {
            plan = null;
            if (calibratedDirectFullTravelRoute == null) {
                return false;
            }

            var qualification = TppaDirectFullTravelRouteQualification.Evaluate(
                enabled: true,
                operatorConfirmed: true,
                calibratedDirectFullTravelRoute.AzimuthStartingPositionDegrees,
                calibratedDirectFullTravelRoute.AzimuthMinimumDegrees,
                calibratedDirectFullTravelRoute.AzimuthMaximumDegrees,
                calibratedDirectFullTravelRoute.AltitudeStartingPositionDegrees,
                calibratedDirectFullTravelRoute.AltitudeMinimumDegrees,
                calibratedDirectFullTravelRoute.AltitudeMaximumDegrees,
                calibratedDirectFullTravelRoute.ResponseModel.AzimuthDeltaPerXUnit,
                calibratedDirectFullTravelRoute.ResponseModel.AzimuthDeltaPerYUnit,
                calibratedDirectFullTravelRoute.ResponseModel.AltitudeDeltaPerXUnit,
                calibratedDirectFullTravelRoute.ResponseModel.AltitudeDeltaPerYUnit,
                calibratedDirectFullTravelRoute.MaximumXUnitsPerMove,
                calibratedDirectFullTravelRoute.MaximumYUnitsPerMove,
                observation.AzimuthErrorDegrees * 60.0,
                observation.AltitudeErrorDegrees * 60.0);
            if (!qualification.IsQualified) {
                Logger.Info($"Calibrated direct full-travel route is not eligible: {qualification.Reason}.");
                return false;
            }

            if (!TrySolveLeastSquaresCommand(calibratedDirectFullTravelRoute.ResponseModel, observation, out var rawX, out var rawY)) {
                Logger.Warning("Calibrated direct full-travel route could not solve the measured 2x2 response matrix.");
                return false;
            }

            var xMagnitude = NormalizeMagnitude(rawX * ConfirmedXCorrectionGain, calibratedDirectFullTravelRoute.MaximumXUnitsPerMove);
            var yMagnitude = NormalizeMagnitude(rawY * ConfirmedXCorrectionGain, calibratedDirectFullTravelRoute.MaximumYUnitsPerMove);
            if (Math.Abs(xMagnitude) < MinimumMoveMagnitude && Math.Abs(yMagnitude) < MinimumMoveMagnitude) {
                return false;
            }

            plan = new AutomatedAdjustmentPlan(xMagnitude, yMagnitude, false, "Calibrated direct full-travel correction");
            return true;
        }

        /// <summary>
        /// Records that a move was executed successfully. The controller waits for the next
        /// measured solve result before turning that move into a training sample.
        /// </summary>
        public void NoteSuccessfulExecution(AutomatedAdjustmentPlan plan) {
            if (!hasObservation || !plan.HasMovement) {
                return;
            }

            if (Math.Abs(plan.XMagnitude) > 0) {
                NoteAzimuthTravelUsed(plan.XMagnitude, "automated correction");
                var direction = Math.Sign(plan.XMagnitude);
                if (!committedXDirection.HasValue || committedXDirection.Value != direction) {
                    committedXDirection = direction;
                    committedXTravelSinceReversal = 0;
                    xAcquisitionConfirmed = false;
                    ResetEarlyXEngagementEvidence();
                }

                committedXTravelSinceReversal += Math.Abs(plan.XMagnitude);
                lastExecutedXDirection = direction;
                pendingXReversalDirection = null;
            }

            if (Math.Abs(plan.YMagnitude) > 0) {
                NoteAltitudeTravelUsed(plan.YMagnitude, "automated correction");
            }

            pendingPlan = new PendingPlan(plan, currentObservation);
        }

        /// <summary>
        /// Records that the attempted move failed. Failed moves must not contribute to the
        /// learned actuator model. A failed UPAS X command is conservatively treated as having
        /// consumed its full travel allowance because the actual actuator position is unknown.
        /// </summary>
        public void NoteFailedExecution(AutomatedAdjustmentPlan attemptedPlan) {
            pendingPlan = null;

            if (attemptedPlan == null) {
                return;
            }

            if (Math.Abs(attemptedPlan.XMagnitude) > 0) {
                NoteAzimuthTravelUsed(attemptedPlan.XMagnitude, "failed automated correction (conservative)");
                InvalidateXSeatingAndEngagementState(preserveLearnedDirection: true);
            }

            if (Math.Abs(attemptedPlan.YMagnitude) > 0) {
                NoteAltitudeTravelUsed(attemptedPlan.YMagnitude, "failed automated correction (conservative)");
            }
        }

        private void AddSample(ResponseSample sample) {
            samples.Enqueue(sample);
            while (samples.Count > MaxSamples) {
                samples.Dequeue();
            }
        }

        private AutomatedAdjustmentPlan ApplyAzimuthTravelGuard(AutomatedAdjustmentPlan plan) {
            plan = ApplyNearTargetXMoveLimit(plan);
            if (plan == null || !plan.HasMovement) {
                return plan;
            }

            if (Math.Abs(plan.XMagnitude) > 0 && !CanExecuteAzimuthTravel(plan.XMagnitude, out var azimuthReason)) {
                return AutomatedAdjustmentPlan.Skip(azimuthReason);
            }

            if (Math.Abs(plan.YMagnitude) > 0 && !CanExecuteAltitudeTravel(plan.YMagnitude, out var altitudeReason)) {
                return AutomatedAdjustmentPlan.Skip(altitudeReason);
            }

            return plan;
        }

        private AutomatedAdjustmentPlan ApplyNearTargetXMoveLimit(AutomatedAdjustmentPlan plan) {
            if (plan == null
                || !UseUpasEngagementController
                || !plan.HasMovement
                || Math.Abs(plan.XMagnitude) <= NearTargetMaximumXMoveMagnitude
                || Math.Abs(currentObservation.AzimuthErrorDegrees) > NearTargetXMoveLimitResidualDegrees) {
                return plan;
            }

            var limitedX = Math.Sign(plan.XMagnitude) * NearTargetMaximumXMoveMagnitude;
            Logger.Info($"Limited near-target UPAS azimuth command from X {Math.Round(plan.XMagnitude, 3)} to X {Math.Round(limitedX, 3)} while the azimuth residual is {Math.Round(Math.Abs(currentObservation.AzimuthErrorDegrees) * 60.0, 3)}'.");
            return new AutomatedAdjustmentPlan(limitedX,
                                               plan.YMagnitude,
                                               plan.IsProbe,
                                               plan.Reason + " (near-target X limit)");
        }

        private void NoteAzimuthTravelUsed(double xMagnitude, string context) {
            if (!UseUpasEngagementController || !AzimuthTravelGuardEnabled || Math.Abs(xMagnitude) <= 0) {
                return;
            }

            var degreesPerUnit = Math.Abs(AzimuthDegreesPerXUnit);
            if (degreesPerUnit <= 0) {
                return;
            }

            var used = Math.Abs(xMagnitude) * degreesPerUnit;
            azimuthTravelUsedDegrees += used;
            Logger.Info($"UPAS azimuth travel guard recorded {Math.Round(used, 3)} deg for {context}; cumulative {Math.Round(azimuthTravelUsedDegrees, 3)}/{Math.Round(Math.Abs(AzimuthTravelLimitDegrees), 3)} deg.");
        }

        private void NoteAltitudeTravelUsed(double yMagnitude, string context) {
            if (!UseUpasEngagementController
                || !AltitudeTravelGuardEnabled
                || !AltitudeTravelGuardConfirmed
                || !altitudeTravelGuardInitialized
                || Math.Abs(yMagnitude) <= 0) {
                return;
            }

            var physicalDelta = yMagnitude * Math.Abs(AltitudeDegreesPerYUnit) * AltitudeCommandDirectionMultiplier;
            altitudePossibleMinimumDegrees += Math.Min(0, physicalDelta);
            altitudePossibleMaximumDegrees += Math.Max(0, physicalDelta);
            Logger.Info($"UPAS altitude travel guard recorded conservative physical delta {Math.Round(physicalDelta, 3)} deg for {context}; possible ALT interval {Math.Round(altitudePossibleMinimumDegrees, 3)}..{Math.Round(altitudePossibleMaximumDegrees, 3)} deg within {Math.Round(AltitudeMinimumDegrees, 3)}..{Math.Round(AltitudeMaximumDegrees, 3)} deg.");
        }

        private void IncrementProbeRejectionCount(AutomatedAdjustmentPlan plan) {
            if (Math.Abs(plan.XMagnitude) > 0) {
                rejectedXProbeCount = Math.Min(MaxRejectedProbeAttempts, rejectedXProbeCount + 1);
                Logger.Warning($"Automated X probe response was below the measurement floor. Rejected attempts: {rejectedXProbeCount}/{MaxRejectedProbeAttempts}.");
            }

            if (Math.Abs(plan.YMagnitude) > 0) {
                rejectedYProbeCount = Math.Min(MaxRejectedProbeAttempts, rejectedYProbeCount + 1);
                Logger.Warning($"Automated Y probe response was below the measurement floor. Rejected attempts: {rejectedYProbeCount}/{MaxRejectedProbeAttempts}.");
            }
        }

        private void ResetProbeRejectionCount(AutomatedAdjustmentPlan plan) {
            if (Math.Abs(plan.XMagnitude) > 0) {
                rejectedXProbeCount = 0;
            }

            if (Math.Abs(plan.YMagnitude) > 0) {
                rejectedYProbeCount = 0;
            }
        }

        private bool ShouldSkipSampleForModel(AutomatedAdjustmentPlan plan, AutomatedAdjustmentObservation beforeMoveObservation) {
            return Math.Abs(plan.XMagnitude) > 0
                   && Math.Abs(plan.YMagnitude) <= 0
                   && Math.Abs(plan.XMagnitude) <= GetMinimumTrustedXSampleMagnitude()
                   && !xAcquisitionConfirmed
                   && ShouldUseXAcquisition(beforeMoveObservation);
        }

        private void ResetAcquisitionState(bool preserveLearnedDirection) {
            rejectedYProbeCount = 0;
            InvalidateXSeatingAndEngagementState(preserveLearnedDirection);
        }

        private void InvalidateXSeatingAndEngagementState(bool preserveLearnedDirection) {
            rejectedXProbeCount = 0;
            lastExecutedXDirection = null;
            pendingXReversalDirection = null;
            if (!preserveLearnedDirection) {
                learnedXAcquisitionDirection = null;
            }

            preferredXAcquisitionDirection = preserveLearnedDirection ? learnedXAcquisitionDirection : null;
            committedXDirection = null;
            committedXTravelSinceReversal = 0;
            xAcquisitionConfirmed = false;
            rejectedWorseningXDirectionMask = 0;
            dominantXAcquisitionBlocked = false;
            earlyXReversalCount = 0;
            ResetEarlyXEngagementEvidence();
        }

        private void ResetEarlyXEngagementEvidence() {
            earlyXImprovementEvidenceCount = 0;
            earlyXWorseningEvidenceCount = 0;
        }

        private void IncrementXAcquisitionRejection() {
            rejectedXProbeCount = Math.Min(MaxRejectedProbeAttempts, rejectedXProbeCount + 1);
            Logger.Warning($"Automated X acquisition response did not improve the dominant azimuth residual. Rejected attempts: {rejectedXProbeCount}/{MaxRejectedProbeAttempts}.");
        }

        private double GetProbeMagnitude(bool xAxis) {
            var baseMagnitude = xAxis ? GetBaseXProbeMagnitude() : DefaultYProbeMagnitude;
            var rejectedCount = xAxis ? rejectedXProbeCount : rejectedYProbeCount;
            return Math.Min(MaximumProbeMagnitude, baseMagnitude * Math.Pow(ProbeRetryScale, rejectedCount));
        }

        private double GetBaseXProbeMagnitude() {
            return UseUpasEngagementController ? UpasXProbeMagnitude : DefaultXProbeMagnitude;
        }

        private double GetMinimumTrustedXCorrectionMagnitude(AutomatedAdjustmentObservation observation) {
            if (!UseUpasEngagementController) {
                return GetBaseXProbeMagnitude();
            }

            if (Math.Abs(observation.AzimuthErrorDegrees) <= NearConvergenceXResidualDegrees) {
                return 0;
            }

            return Math.Min(GetBaseXProbeMagnitude(), GetMaximumConfirmedXMoveMagnitude(observation));
        }

        private double GetMinimumTrustedXSampleMagnitude() {
            return GetBaseXProbeMagnitude() * ProbeRetryScale;
        }

        private bool IsProbeAxisExhausted(bool xAxis) {
            return (xAxis ? rejectedXProbeCount : rejectedYProbeCount) >= MaxRejectedProbeAttempts && !(xAxis && IsXAcquisitionCommitmentActive());
        }

        private bool ShouldDebounceXDirectionReversal(AutomatedAdjustmentPlan plan, out AutomatedAdjustmentPlan debouncePlan) {
            debouncePlan = null;

            if (plan.IsProbe || Math.Abs(plan.XMagnitude) <= 0 || !lastExecutedXDirection.HasValue) {
                return false;
            }

            var requestedDirection = Math.Sign(plan.XMagnitude);
            if (requestedDirection == lastExecutedXDirection.Value) {
                pendingXReversalDirection = null;
                return false;
            }

            if (pendingXReversalDirection == requestedDirection) {
                pendingXReversalDirection = null;
                Logger.Info("Confirmed automated X direction reversal after consecutive solve estimates.");
                return false;
            }

            pendingXReversalDirection = requestedDirection;
            debouncePlan = AutomatedAdjustmentPlan.Skip("Debouncing automated azimuth direction reversal until the next solve confirms it.");
            Logger.Info($"Debouncing automated X direction reversal from {lastExecutedXDirection.Value} to {requestedDirection}.");
            return true;
        }

        private AutomatedAdjustmentPlan EnforceXReversalClearance(AutomatedAdjustmentPlan plan) {
            if (!plan.HasMovement
                || plan.IsProbe
                || Math.Abs(plan.XMagnitude) <= 0
                || Math.Abs(plan.YMagnitude) > 0
                || !lastExecutedXDirection.HasValue) {
                return plan;
            }

            var requestedDirection = Math.Sign(plan.XMagnitude);
            if (requestedDirection == lastExecutedXDirection.Value) {
                return plan;
            }

            var minimumReversalMagnitude = GetProbeMagnitude(true);
            if (Math.Abs(plan.XMagnitude) >= minimumReversalMagnitude) {
                return plan;
            }

            var adjustedX = requestedDirection * minimumReversalMagnitude;
            Logger.Info($"Raised automated X reversal from {Math.Round(plan.XMagnitude, 3)} to {Math.Round(adjustedX, 3)} so the first move in the new direction is above the azimuth backlash verdict floor.");
            return new AutomatedAdjustmentPlan(adjustedX,
                                               0,
                                               false,
                                               plan.Reason + " (X reversal clearance)");
        }

        /// <summary>
        /// Creates a probe move on the less-observed axis so the sample set becomes informative
        /// in both columns of the local response matrix.
        /// </summary>
        private AutomatedAdjustmentPlan CreateProbePlan() {
            if (currentObservation.TotalErrorDegrees < MinimumResidualForProbeDegrees) {
                return AutomatedAdjustmentPlan.Skip("Alignment is within the automated probe resolution.");
            }

            var xExcitation = 0.0;
            var yExcitation = 0.0;

            foreach (var sample in samples) {
                xExcitation += Math.Abs(sample.XMagnitude);
                yExcitation += Math.Abs(sample.YMagnitude);
            }

            var protectAltitudeWhileAzimuthRemainsLarger = ShouldProtectAltitudeWhileAzimuthRemainsLarger(currentObservation);
            var probeXAxis = protectAltitudeWhileAzimuthRemainsLarger || ShouldUseXAcquisition(currentObservation) || xExcitation <= yExcitation;
            if (IsProbeAxisExhausted(probeXAxis)) {
                if (protectAltitudeWhileAzimuthRemainsLarger && probeXAxis) {
                    return AutomatedAdjustmentPlan.Skip("Azimuth remains larger than altitude while altitude is near target; refusing to probe altitude as a substitute for azimuth correction.");
                }

                return AutomatedAdjustmentPlan.Skip($"Automated probe response on {(probeXAxis ? "azimuth" : "altitude")} remained below the measurement floor after {MaxRejectedProbeAttempts} attempts. Check mechanics or backlash compensation.");
            }

            if (probeXAxis) {
                var direction = GetPreferredXAcquisitionDirection(currentObservation);
                return new AutomatedAdjustmentPlan(direction * GetProbeMagnitude(true),
                                                   0,
                                                   true,
                                                   $"Probing azimuth response (attempt {rejectedXProbeCount + 1})");
            }

            return new AutomatedAdjustmentPlan(0,
                                               GetProbeMagnitude(false),
                                               true,
                                               $"Probing altitude response (attempt {rejectedYProbeCount + 1})");
        }


        private bool TryCreateXEngagementPlan(AutomatedAdjustmentObservation observation, out AutomatedAdjustmentPlan plan) {
            plan = null;

            if (!UseUpasEngagementController
                || !IsXAcquisitionCommitmentActive()
                || !committedXDirection.HasValue
                || !ShouldUseXAcquisition(observation)) {
                return false;
            }

            preferredXAcquisitionDirection = committedXDirection.Value;
            var remainingClearance = XAcquisitionClearanceMagnitude - committedXTravelSinceReversal;
            var engagementMagnitude = Math.Min(GetBaseXProbeMagnitude(), remainingClearance);
            plan = new AutomatedAdjustmentPlan(committedXDirection.Value * engagementMagnitude,
                                               0,
                                               true,
                                               $"Continuing UPAS azimuth engagement run: X travel {Math.Round(committedXTravelSinceReversal, 3)}/{XAcquisitionClearanceMagnitude}");
            return true;
        }

        private bool TryCreateRememberedDominantAzimuthPlan(AutomatedAdjustmentObservation observation, out AutomatedAdjustmentPlan plan) {
            plan = null;
            if (!UseUpasEngagementController
                || xAcquisitionConfirmed
                || IsXAcquisitionCommitmentActive()
                || !ShouldUseXAcquisition(observation)
                || Math.Abs(observation.AzimuthErrorDegrees) > MaximumRememberedResponseCorrectionResidualDegrees
                || !rememberedXAzimuthDeltaPerUnit.HasValue
                || !IsTrustedXAzimuthResponse(rememberedXAzimuthDeltaPerUnit.Value)) {
                return false;
            }

            var response = rememberedXAzimuthDeltaPerUnit.Value;
            var command = -observation.AzimuthErrorDegrees / response * RememberedResponseCorrectionGain;
            if (Math.Abs(command) <= 0) {
                return false;
            }

            var direction = Math.Sign(command);
            if (TryGetRememberedXAzimuthResponse(direction, out var directionalResponse)) {
                response = directionalResponse;
                command = -observation.AzimuthErrorDegrees / response * RememberedResponseCorrectionGain;
            }

            command = NormalizeMagnitude(command, MaximumRememberedResponseMoveMagnitude);
            if (Math.Abs(command) < MinimumRememberedResponseMoveMagnitude) {
                command = Math.Sign(command) * MinimumRememberedResponseMoveMagnitude;
            }

            preferredXAcquisitionDirection = Math.Sign(command);
            plan = new AutomatedAdjustmentPlan(command, 0, true, "Damped correction from remembered UPAS azimuth response");
            Logger.Info($"Using remembered UPAS Az/X response for a near-target correction: residual={Math.Round(observation.AzimuthErrorDegrees * 60.0, 3)}', response={Math.Round(response * 60.0, 4)}'/unit, X={Math.Round(command, 3)}.");
            return true;
        }

        /// <summary>
        /// Builds the best available corrective command from the learned response model.
        ///
        /// The controller evaluates a damped two-axis least-squares step and one-axis fallback
        /// moves, then keeps the candidate that predicts the largest guarded error reduction.
        /// </summary>
        private AutomatedAdjustmentPlan CreateCorrectivePlan(ResponseModel responseModel, AutomatedAdjustmentObservation observation) {
            var currentCost = CalculateGuardedErrorCost(observation, observation.AzimuthErrorDegrees, observation.AltitudeErrorDegrees);
            var candidates = new List<AutomatedAdjustmentPlan>();
            var azimuthDominant = ShouldUseXAcquisition(observation);
            var protectAltitudeWhileAzimuthRemainsLarger = ShouldProtectAltitudeWhileAzimuthRemainsLarger(observation);

            if (!azimuthDominant && !protectAltitudeWhileAzimuthRemainsLarger && TrySolveLeastSquaresCommand(responseModel, observation, out var rawX, out var rawY)) {
                candidates.Add(CreateScaledPlan(rawX, rawY, 0.5, "Adaptive two-axis correction"));
                candidates.Add(CreateScaledPlan(rawX, rawY, 0.25, "Adaptive two-axis correction"));
                candidates.Add(CreateScaledPlan(rawX, rawY, 0.125, "Adaptive two-axis correction"));
            }

            if (TryCreateSingleAxisPlan(responseModel.AzimuthDeltaPerXUnit,
                                        responseModel.AltitudeDeltaPerXUnit,
                                        observation,
                                        true,
                                        MaximumMoveMagnitude,
                                        out var xAxisPlan)) {
                if (!azimuthDominant
                    || !preferredXAcquisitionDirection.HasValue
                    || Math.Sign(xAxisPlan.XMagnitude) == preferredXAcquisitionDirection.Value) {
                    candidates.Add(xAxisPlan);
                }
            }

            if (!azimuthDominant && !protectAltitudeWhileAzimuthRemainsLarger && TryCreateSingleAxisPlan(responseModel.AzimuthDeltaPerYUnit,
                                        responseModel.AltitudeDeltaPerYUnit,
                                        observation,
                                        false,
                                        GetMaximumYMoveMagnitude(observation),
                                        out var yAxisPlan)) {
                candidates.Add(yAxisPlan);
            }

            AutomatedAdjustmentPlan bestPlan = null;
            var bestPredictedCost = currentCost * MinimumExpectedImprovementCostFactor;

            foreach (var candidate in candidates) {
                if (!candidate.HasMovement) {
                    continue;
                }

                var predictedCost = PredictErrorCost(responseModel, observation, candidate);
                Logger.Info($"Automated polar-alignment candidate '{candidate.Reason}' predicts guarded residual score {Math.Round(predictedCost * 3600.0, 3)}\" from current {Math.Round(currentCost * 3600.0, 3)}\".");
                if (predictedCost < bestPredictedCost) {
                    bestPredictedCost = predictedCost;
                    bestPlan = candidate;
                }
            }

            return bestPlan ?? AutomatedAdjustmentPlan.Skip("The learned automation model does not yet predict a safe improvement.");
        }

        private double GetMaximumYMoveMagnitude(AutomatedAdjustmentObservation observation) {
            return UseUpasEngagementController
                   && Math.Abs(observation.AltitudeErrorDegrees) >= LargeAltitudeResidualDegrees
                   && HasConsistentYResponse()
                ? MaximumConfirmedYMoveMagnitude
                : MaximumMoveMagnitude;
        }

        private bool HasConsistentYResponse() {
            var responses = new List<double>();
            foreach (var sample in samples) {
                if (Math.Abs(sample.YMagnitude) <= 0 || Math.Abs(sample.XMagnitude) > 0) {
                    continue;
                }

                var response = sample.AltitudeDeltaDegrees / sample.YMagnitude;
                var magnitude = Math.Abs(response);
                if (magnitude < MinimumAxisResponseDegreesPerUnit || magnitude > MaximumSampleResponseDegreesPerUnit) {
                    continue;
                }

                responses.Add(response);
            }

            if (responses.Count < MinimumConfirmedYResponseSamples) {
                return false;
            }

            var sign = Math.Sign(responses[0]);
            var minimumMagnitude = double.MaxValue;
            var maximumMagnitude = 0.0;
            foreach (var response in responses) {
                if (Math.Sign(response) != sign) {
                    return false;
                }

                var magnitude = Math.Abs(response);
                minimumMagnitude = Math.Min(minimumMagnitude, magnitude);
                maximumMagnitude = Math.Max(maximumMagnitude, magnitude);
            }

            return minimumMagnitude > 0
                   && maximumMagnitude / minimumMagnitude <= MaximumConfirmedYResponseSpreadRatio;
        }

        private bool TryCreateConfirmedDominantAzimuthPlan(AutomatedAdjustmentObservation observation, out AutomatedAdjustmentPlan plan) {
            plan = null;

            if (!xAcquisitionConfirmed || !ShouldUseXAcquisition(observation)) {
                return false;
            }

            if (!TryEstimateXAzimuthResponse(out var azimuthDeltaPerXUnit)) {
                return false;
            }

            var command = -observation.AzimuthErrorDegrees / azimuthDeltaPerXUnit * ConfirmedXCorrectionGain;
            var maximumConfirmedMove = GetMaximumConfirmedXMoveMagnitude(observation);
            command = NormalizeMagnitude(command, maximumConfirmedMove);
            if (Math.Abs(command) < MinimumMoveMagnitude) {
                return false;
            }

            var reason = "Confirmed azimuth correction";
            var minimumTrustedXCorrectionMagnitude = GetMinimumTrustedXCorrectionMagnitude(observation);
            if (minimumTrustedXCorrectionMagnitude > MinimumMoveMagnitude
                && Math.Abs(observation.AzimuthErrorDegrees) > MinimumTrustedXCorrectionResidualDegrees
                && Math.Abs(command) < minimumTrustedXCorrectionMagnitude) {
                command = Math.Sign(command) * minimumTrustedXCorrectionMagnitude;
                reason += " (minimum trusted X magnitude)";
            }

            plan = new AutomatedAdjustmentPlan(command, 0, false, reason);
            return true;
        }

        private static double GetMaximumConfirmedXMoveMagnitude(AutomatedAdjustmentObservation observation) {
            var azimuthResidual = Math.Abs(observation.AzimuthErrorDegrees);
            if (azimuthResidual <= NearConvergenceXResidualDegrees) {
                return NearConvergenceMaximumConfirmedXMoveMagnitude;
            }

            if (azimuthResidual <= ModerateConvergenceXResidualDegrees) {
                return ModerateConvergenceMaximumConfirmedXMoveMagnitude;
            }

            return MaximumConfirmedXMoveMagnitude;
        }

        private bool TryEstimateXAzimuthResponse(out double azimuthDeltaPerXUnit) {
            var s00 = 0.0;
            var b0 = 0.0;

            foreach (var sample in samples) {
                if (Math.Abs(sample.XMagnitude) <= 0 || Math.Abs(sample.YMagnitude) > 0) {
                    continue;
                }

                s00 += sample.XMagnitude * sample.XMagnitude;
                b0 += sample.XMagnitude * sample.AzimuthDeltaDegrees;
            }

            if (s00 <= NormalEquationDamping) {
                azimuthDeltaPerXUnit = 0;
                return false;
            }

            azimuthDeltaPerXUnit = b0 / s00;
            return Math.Abs(azimuthDeltaPerXUnit) >= MinimumAxisResponseDegreesPerUnit;
        }

        private bool ShouldRejectDominantAzimuthXSample(AutomatedAdjustmentObservation latestObservation, out string reason, out bool countAsProbeRejection) {
            reason = null;
            countAsProbeRejection = false;

            if (pendingPlan == null
                || Math.Abs(pendingPlan.Plan.XMagnitude) <= 0
                || Math.Abs(pendingPlan.Plan.YMagnitude) > 0
                || !ShouldUseXAcquisition(pendingPlan.BeforeMoveObservation)) {
                return false;
            }

            var commandDirection = Math.Sign(pendingPlan.Plan.XMagnitude);
            var beforeAzimuthError = pendingPlan.BeforeMoveObservation.AzimuthErrorDegrees;
            var afterAzimuthError = latestObservation.AzimuthErrorDegrees;
            var beforeAzimuthMagnitude = Math.Abs(beforeAzimuthError);
            var afterAzimuthMagnitude = Math.Abs(afterAzimuthError);
            var improvement = beforeAzimuthMagnitude - afterAzimuthMagnitude;
            var acceptedZeroCrossing = IsAcceptedZeroCrossing(beforeAzimuthError, afterAzimuthError);

            var changeArcminutes = Math.Round((afterAzimuthMagnitude - beforeAzimuthMagnitude) * 60.0, 3);
            if (UseUpasEngagementController
                && IsXAcquisitionCommitmentActive()
                && committedXDirection.HasValue
                && committedXDirection.Value == commandDirection) {
                var totalAzimuthChange = Math.Abs(afterAzimuthError - beforeAzimuthError);
                var strongImprovement = improvement > MinimumEarlyXEngagementImprovementDegrees
                                        || (acceptedZeroCrossing && totalAzimuthChange > MinimumEarlyXEngagementImprovementDegrees);
                var strongWorsening = improvement < -MinimumEarlyXEngagementWorseningDegrees;

                if (strongImprovement) {
                    earlyXImprovementEvidenceCount++;
                    earlyXWorseningEvidenceCount = 0;
                    if (earlyXImprovementEvidenceCount >= MinimumEarlyXEngagementEvidenceSamples) {
                        ResetEarlyXEngagementEvidence();
                        preferredXAcquisitionDirection = commandDirection;
                        learnedXAcquisitionDirection = commandDirection;
                        if (improvement > MinimumEarlyXEngagementImprovementDegrees) {
                            UpdateRememberedXAzimuthResponse(beforeAzimuthError, afterAzimuthError, pendingPlan.Plan.XMagnitude);
                        }
                        xAcquisitionConfirmed = true;
                        rejectedXProbeCount = 0;
                        rejectedWorseningXDirectionMask = 0;
                        dominantXAcquisitionBlocked = false;
                        return false;
                    }

                    preferredXAcquisitionDirection = commandDirection;
                    reason = $"Observed the first above-noise UPAS X improvement during engagement; requiring one consecutive confirmation before learning. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, committed travel={Math.Round(committedXTravelSinceReversal, 3)}/{XAcquisitionClearanceMagnitude}, azimuth change={changeArcminutes} arcmin";
                    return true;
                }

                if (strongWorsening) {
                    earlyXWorseningEvidenceCount++;
                    earlyXImprovementEvidenceCount = 0;
                    if (earlyXWorseningEvidenceCount >= MinimumEarlyXEngagementEvidenceSamples) {
                        ResetEarlyXEngagementEvidence();
                        if (earlyXReversalCount >= MaximumEarlyXReversalsPerAcquisition) {
                            preferredXAcquisitionDirection = commandDirection;
                            reason = $"Observed repeated above-noise UPAS X worsening after the one permitted early reversal; continuing to the clearance distance before making another direction verdict. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, committed travel={Math.Round(committedXTravelSinceReversal, 3)}/{XAcquisitionClearanceMagnitude}, azimuth change={changeArcminutes} arcmin";
                            return true;
                        }

                        earlyXReversalCount++;
                        rejectedWorseningXDirectionMask |= GetXDirectionMask(commandDirection);
                        xAcquisitionConfirmed = false;
                        preferredXAcquisitionDirection = -commandDirection;
                        committedXDirection = null;
                        committedXTravelSinceReversal = 0;
                        countAsProbeRejection = true;
                        reason = $"Rejected automated X acquisition direction after two consecutive above-noise worsenings during engagement; trying the opposite X direction. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, azimuth change={changeArcminutes} arcmin";
                        return true;
                    }

                    preferredXAcquisitionDirection = commandDirection;
                    reason = $"Observed the first above-noise UPAS X worsening during engagement; requiring one consecutive confirmation before reversing. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, committed travel={Math.Round(committedXTravelSinceReversal, 3)}/{XAcquisitionClearanceMagnitude}, azimuth change={changeArcminutes} arcmin";
                    return true;
                }

                ResetEarlyXEngagementEvidence();
                preferredXAcquisitionDirection = commandDirection;
                reason = $"Rejected automated X acquisition sample because the X travel is still inside the measured UPAS azimuth engagement distance; treating this solve as inconclusive and continuing the current X direction. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, committed travel={Math.Round(committedXTravelSinceReversal, 3)}/{XAcquisitionClearanceMagnitude}, azimuth change={changeArcminutes} arcmin";
                return true;
            }

            ResetEarlyXEngagementEvidence();

            if (improvement > MinimumSampleResponseDegrees || acceptedZeroCrossing) {
                preferredXAcquisitionDirection = commandDirection;
                learnedXAcquisitionDirection = commandDirection;
                if (improvement > MinimumSampleResponseDegrees) {
                    UpdateRememberedXAzimuthResponse(beforeAzimuthError, afterAzimuthError, pendingPlan.Plan.XMagnitude);
                }
                xAcquisitionConfirmed = true;
                rejectedXProbeCount = 0;
                rejectedWorseningXDirectionMask = 0;
                dominantXAcquisitionBlocked = false;
                return false;
            }
            if (improvement < -MinimumSampleResponseDegrees) {
                rejectedWorseningXDirectionMask |= GetXDirectionMask(commandDirection);
                xAcquisitionConfirmed = false;

                if ((rejectedWorseningXDirectionMask & 0b11) == 0b11) {
                    dominantXAcquisitionBlocked = true;
                    reason = $"Rejected automated X acquisition sample because both X directions measurably worsened the dominant azimuth residual. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, azimuth change={changeArcminutes} arcmin";
                    return true;
                }

                preferredXAcquisitionDirection = -commandDirection;
                committedXDirection = null;
                committedXTravelSinceReversal = 0;
                countAsProbeRejection = true;
                reason = $"Rejected automated X acquisition sample because it measurably worsened the dominant azimuth residual after the azimuth backlash clearance distance was reached; trying the opposite X direction next. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, azimuth change={changeArcminutes} arcmin";
                return true;
            }

            if (!preferredXAcquisitionDirection.HasValue) {
                preferredXAcquisitionDirection = commandDirection;
            }

            countAsProbeRejection = true;
            reason = $"Rejected automated X acquisition sample because it remained below the dominant azimuth improvement floor while clearing backlash. Command X={Math.Round(pendingPlan.Plan.XMagnitude, 3)}, azimuth change={changeArcminutes} arcmin";
            return true;
        }

        private static bool IsAcceptedZeroCrossing(double beforeAzimuthError, double afterAzimuthError) {
            if (Math.Abs(beforeAzimuthError) <= MinimumSampleResponseDegrees
                || Math.Abs(afterAzimuthError) <= MinimumSampleResponseDegrees
                || Math.Sign(beforeAzimuthError) == Math.Sign(afterAzimuthError)) {
                return false;
            }

            return Math.Abs(afterAzimuthError) <= Math.Abs(beforeAzimuthError) + MaximumAcceptedZeroCrossingOvershootDegrees;
        }

        private static int GetXDirectionMask(int direction) {
            return direction > 0 ? 0b01 : 0b10;
        }

        private int GetPreferredXAcquisitionDirection(AutomatedAdjustmentObservation observation) {
            if (preferredXAcquisitionDirection.HasValue) {
                return preferredXAcquisitionDirection.Value;
            }

            if (UseUpasEngagementController
                && rememberedXAzimuthDeltaPerUnit.HasValue
                && IsTrustedXAzimuthResponse(rememberedXAzimuthDeltaPerUnit.Value)
                && Math.Abs(observation.AzimuthErrorDegrees) > MinimumMoveMagnitude * MinimumAxisResponseDegreesPerUnit) {
                var rememberedCommand = -observation.AzimuthErrorDegrees / rememberedXAzimuthDeltaPerUnit.Value;
                if (Math.Abs(rememberedCommand) > 0) {
                    var direction = Math.Sign(rememberedCommand);
                    Logger.Info($"Using remembered UPAS Az/X response to choose initial X acquisition direction {direction}.");
                    return direction;
                }
            }

            return learnedXAcquisitionDirection ?? 1;
        }

        private void UpdateRememberedXAzimuthResponse(double beforeAzimuthError, double afterAzimuthError, double xMagnitude) {
            if (!UseUpasEngagementController || Math.Abs(xMagnitude) <= 0) {
                return;
            }

            var response = (afterAzimuthError - beforeAzimuthError) / xMagnitude;
            if (!IsTrustedXAzimuthResponse(response)) {
                return;
            }

            if (rememberedXAzimuthDeltaPerUnit.HasValue
                && Math.Sign(response) != Math.Sign(rememberedXAzimuthDeltaPerUnit.Value)) {
                Logger.Warning($"Rejected UPAS azimuth response memory sample with contradictory sign. Existing={Math.Round(rememberedXAzimuthDeltaPerUnit.Value * 60.0, 4)}'/unit, sample={Math.Round(response * 60.0, 4)}'/unit.");
                return;
            }

            var responses = xMagnitude > 0 ? positiveXAzimuthResponses : negativeXAzimuthResponses;
            responses.Enqueue(response);
            while (responses.Count > MaxRememberedResponsesPerDirection) {
                responses.Dequeue();
            }

            rememberedXAzimuthDeltaPerUnit = GetMedianRememberedResponse();
            Logger.Info($"Updated robust UPAS azimuth response memory: direction={Math.Sign(xMagnitude)}, sample={Math.Round(response * 60.0, 4)}'/unit, median={Math.Round(rememberedXAzimuthDeltaPerUnit.Value * 60.0, 4)}'/unit.");
        }

        private bool TryGetRememberedXAzimuthResponse(int direction, out double response) {
            var responses = direction >= 0 ? positiveXAzimuthResponses : negativeXAzimuthResponses;
            if (responses.Count > 0) {
                response = GetMedian(responses);
                return IsTrustedXAzimuthResponse(response);
            }

            response = rememberedXAzimuthDeltaPerUnit ?? 0;
            return IsTrustedXAzimuthResponse(response);
        }

        private double GetMedianRememberedResponse() {
            var combined = new List<double>(positiveXAzimuthResponses.Count + negativeXAzimuthResponses.Count);
            combined.AddRange(positiveXAzimuthResponses);
            combined.AddRange(negativeXAzimuthResponses);
            return combined.Count > 0 ? GetMedian(combined) : rememberedXAzimuthDeltaPerUnit ?? 0;
        }

        private static double GetMedian(IEnumerable<double> values) {
            var sorted = new List<double>(values);
            sorted.Sort();
            var middle = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) / 2.0
                : sorted[middle];
        }

        private static bool IsTrustedXAzimuthResponse(double azimuthDeltaPerXUnit) {
            var magnitude = Math.Abs(azimuthDeltaPerXUnit);
            return magnitude >= MinimumAxisResponseDegreesPerUnit
                   && magnitude <= MaximumSampleResponseDegreesPerUnit;
        }

        private bool IsXAcquisitionCommitmentActive() {
            return UseUpasEngagementController
                   && committedXDirection.HasValue
                   && !xAcquisitionConfirmed
                   && committedXTravelSinceReversal < XAcquisitionClearanceMagnitude;
        }

        private static bool ShouldProtectAltitudeWhileAzimuthRemainsLarger(AutomatedAdjustmentObservation observation) {
            var azimuthMagnitude = Math.Abs(observation.AzimuthErrorDegrees);
            var altitudeMagnitude = Math.Abs(observation.AltitudeErrorDegrees);
            return altitudeMagnitude <= AltitudeProtectionDuringAzimuthCorrectionDegrees && azimuthMagnitude > altitudeMagnitude;
        }

        private bool ShouldUseXAcquisition(AutomatedAdjustmentObservation observation) {
            return UseUpasEngagementController
                ? IsAzimuthActionableForUpasEngagement(observation)
                : IsAzimuthDominantForXAcquisition(observation);
        }

        private static bool IsAzimuthActionableForUpasEngagement(AutomatedAdjustmentObservation observation) {
            var azimuthMagnitude = Math.Abs(observation.AzimuthErrorDegrees);
            var altitudeMagnitude = Math.Abs(observation.AltitudeErrorDegrees);
            return azimuthMagnitude >= AzimuthAcquisitionThresholdDegrees
                   && azimuthMagnitude >= altitudeMagnitude;
        }

        private static bool IsAzimuthDominantForXAcquisition(AutomatedAdjustmentObservation observation) {
            var azimuthMagnitude = Math.Abs(observation.AzimuthErrorDegrees);
            var altitudeMagnitude = Math.Abs(observation.AltitudeErrorDegrees);

            if (azimuthMagnitude < AzimuthAcquisitionThresholdDegrees) {
                return false;
            }

            var ratioDominant = azimuthMagnitude >= altitudeMagnitude * AzimuthAcquisitionDominanceRatio;
            return ratioDominant
                   && (altitudeMagnitude <= AzimuthAcquisitionAltitudeGuardDegrees
                       || azimuthMagnitude >= LargeAzimuthDominanceThresholdDegrees);
        }

        /// <summary>
        /// Predicts the post-move residual cost using the current local response model.
        /// </summary>
        private static double PredictErrorCost(ResponseModel responseModel, AutomatedAdjustmentObservation observation, AutomatedAdjustmentPlan plan) {
            var predictedAzimuth = observation.AzimuthErrorDegrees
                                   + responseModel.AzimuthDeltaPerXUnit * plan.XMagnitude
                                   + responseModel.AzimuthDeltaPerYUnit * plan.YMagnitude;
            var predictedAltitude = observation.AltitudeErrorDegrees
                                     + responseModel.AltitudeDeltaPerXUnit * plan.XMagnitude
                                     + responseModel.AltitudeDeltaPerYUnit * plan.YMagnitude;
            return CalculateGuardedErrorCost(observation, predictedAzimuth, predictedAltitude);
        }

        private static double CalculateGuardedErrorCost(AutomatedAdjustmentObservation observation, double azimuthErrorDegrees, double altitudeErrorDegrees) {
            var cost = Math.Abs(azimuthErrorDegrees) + Math.Abs(altitudeErrorDegrees);

            cost += CalculateSolvedAxisPenalty(observation.AzimuthErrorDegrees, azimuthErrorDegrees);
            cost += CalculateSolvedAxisPenalty(observation.AltitudeErrorDegrees, altitudeErrorDegrees);

            return cost;
        }

        private static double CalculateSolvedAxisPenalty(double currentErrorDegrees, double predictedErrorDegrees) {
            var predictedExcess = Math.Abs(predictedErrorDegrees) - SolvedAxisGuardDegrees;
            if (predictedExcess <= 0) {
                return 0;
            }

            var currentExcess = Math.Max(0, Math.Abs(currentErrorDegrees) - SolvedAxisGuardDegrees);
            var worsenedExcess = predictedExcess - currentExcess;
            if (worsenedExcess <= 0) {
                return 0;
            }

            return SolvedAxisPenaltyFactor * worsenedExcess;
        }

        /// <summary>
        /// Scales and clamps a raw move candidate to the controller's safe operating bounds.
        /// </summary>
        private static AutomatedAdjustmentPlan CreateScaledPlan(double xMagnitude, double yMagnitude, double scale, string reason) {
            xMagnitude *= scale;
            yMagnitude *= scale;

            var maxComponent = Math.Max(Math.Abs(xMagnitude), Math.Abs(yMagnitude));
            if (maxComponent > MaximumMoveMagnitude) {
                var clampScale = MaximumMoveMagnitude / maxComponent;
                xMagnitude *= clampScale;
                yMagnitude *= clampScale;
            }

            return new AutomatedAdjustmentPlan(ApplyDeadband(xMagnitude),
                                               ApplyDeadband(yMagnitude),
                                               false,
                                               reason);
        }

        /// <summary>
        /// Creates a one-axis fallback move by projecting the current error onto a single
        /// actuator response vector.
        /// </summary>
        private static bool TryCreateSingleAxisPlan(double azimuthDeltaPerUnit,
                                                    double altitudeDeltaPerUnit,
                                                    AutomatedAdjustmentObservation observation,
                                                    bool xAxis,
                                                    double maximumMagnitude,
                                                    out AutomatedAdjustmentPlan plan) {
            var leverage = azimuthDeltaPerUnit * azimuthDeltaPerUnit + altitudeDeltaPerUnit * altitudeDeltaPerUnit;
            if (leverage <= NormalEquationDamping) {
                plan = null;
                return false;
            }

            var command = -((azimuthDeltaPerUnit * observation.AzimuthErrorDegrees)
                           + (altitudeDeltaPerUnit * observation.AltitudeErrorDegrees)) / leverage;
            command = NormalizeMagnitude(command * 0.5, maximumMagnitude);

            if (Math.Abs(command) < MinimumMoveMagnitude) {
                plan = null;
                return false;
            }

            plan = xAxis
                ? new AutomatedAdjustmentPlan(command, 0, false, "Adaptive azimuth correction")
                : new AutomatedAdjustmentPlan(0, command, false, "Adaptive altitude correction");
            return true;
        }

        /// <summary>
        /// Solves the damped least-squares command
        /// <c>min || e + A u ||^2</c>
        /// by forming the corresponding <c>(A^T A + lambda I) u = -A^T e</c> normal equations.
        /// </summary>
        private static bool TrySolveLeastSquaresCommand(ResponseModel responseModel,
                                                        AutomatedAdjustmentObservation observation,
                                                        out double xMagnitude,
                                                        out double yMagnitude) {
            var m00 = responseModel.AzimuthDeltaPerXUnit * responseModel.AzimuthDeltaPerXUnit
                      + responseModel.AltitudeDeltaPerXUnit * responseModel.AltitudeDeltaPerXUnit
                      + NormalEquationDamping;
            var m01 = responseModel.AzimuthDeltaPerXUnit * responseModel.AzimuthDeltaPerYUnit
                      + responseModel.AltitudeDeltaPerXUnit * responseModel.AltitudeDeltaPerYUnit;
            var m11 = responseModel.AzimuthDeltaPerYUnit * responseModel.AzimuthDeltaPerYUnit
                      + responseModel.AltitudeDeltaPerYUnit * responseModel.AltitudeDeltaPerYUnit
                      + NormalEquationDamping;

            var rhs0 = -(responseModel.AzimuthDeltaPerXUnit * observation.AzimuthErrorDegrees
                         + responseModel.AltitudeDeltaPerXUnit * observation.AltitudeErrorDegrees);
            var rhs1 = -(responseModel.AzimuthDeltaPerYUnit * observation.AzimuthErrorDegrees
                         + responseModel.AltitudeDeltaPerYUnit * observation.AltitudeErrorDegrees);

            var determinant = m00 * m11 - m01 * m01;
            if (Math.Abs(determinant) <= NormalEquationDamping) {
                xMagnitude = 0;
                yMagnitude = 0;
                return false;
            }

            xMagnitude = ((rhs0 * m11) - (rhs1 * m01)) / determinant;
            yMagnitude = ((m00 * rhs1) - (m01 * rhs0)) / determinant;
            return true;
        }

        /// <summary>
        /// Fits the local response matrix from the recent sample window using least squares.
        ///
        /// The fit is rejected if the sample geometry is too sparse or too ill-conditioned
        /// to support a reliable two-axis estimate.
        /// </summary>
        private bool TryBuildResponseModel(out ResponseModel responseModel) {
            responseModel = null;

            if (samples.Count < 2) {
                return false;
            }

            var s00 = 0.0;
            var s01 = 0.0;
            var s11 = 0.0;
            var azimuthB0 = 0.0;
            var azimuthB1 = 0.0;
            var altitudeB0 = 0.0;
            var altitudeB1 = 0.0;

            foreach (var sample in samples) {
                s00 += sample.XMagnitude * sample.XMagnitude;
                s01 += sample.XMagnitude * sample.YMagnitude;
                s11 += sample.YMagnitude * sample.YMagnitude;

                azimuthB0 += sample.XMagnitude * sample.AzimuthDeltaDegrees;
                azimuthB1 += sample.YMagnitude * sample.AzimuthDeltaDegrees;
                altitudeB0 += sample.XMagnitude * sample.AltitudeDeltaDegrees;
                altitudeB1 += sample.YMagnitude * sample.AltitudeDeltaDegrees;
            }

            var determinant = s00 * s11 - s01 * s01;
            if (determinant <= NormalEquationDamping) {
                return false;
            }

            // Reject nearly singular sample sets. This is the identification-side equivalent of
            // saying "we have not yet probed the hardware in enough independent directions."
            var trace = s00 + s11;
            var discriminant = Math.Sqrt(Math.Max(0, trace * trace - 4 * determinant));
            var largestEigenvalue = (trace + discriminant) / 2.0;
            var smallestEigenvalue = (trace - discriminant) / 2.0;

            if (smallestEigenvalue <= NormalEquationDamping || largestEigenvalue / smallestEigenvalue > 1e6) {
                return false;
            }

            var inverseS00 = s11 / determinant;
            var inverseS01 = -s01 / determinant;
            var inverseS11 = s00 / determinant;

            var azimuthDeltaPerXUnit = inverseS00 * azimuthB0 + inverseS01 * azimuthB1;
            var azimuthDeltaPerYUnit = inverseS01 * azimuthB0 + inverseS11 * azimuthB1;
            var altitudeDeltaPerXUnit = inverseS00 * altitudeB0 + inverseS01 * altitudeB1;
            var altitudeDeltaPerYUnit = inverseS01 * altitudeB0 + inverseS11 * altitudeB1;

            var xColumnMagnitude = Math.Sqrt(azimuthDeltaPerXUnit * azimuthDeltaPerXUnit + altitudeDeltaPerXUnit * altitudeDeltaPerXUnit);
            var yColumnMagnitude = Math.Sqrt(azimuthDeltaPerYUnit * azimuthDeltaPerYUnit + altitudeDeltaPerYUnit * altitudeDeltaPerYUnit);

            if (xColumnMagnitude < MinimumAxisResponseDegreesPerUnit || yColumnMagnitude < MinimumAxisResponseDegreesPerUnit) {
                Logger.Info($"Rejected automated polar-alignment model because a response column is below the per-unit floor. X={Math.Round(xColumnMagnitude * 60.0, 4)}'/unit, Y={Math.Round(yColumnMagnitude * 60.0, 4)}'/unit.");
                return false;
            }

            var rawAzimuthDeltaPerYUnit = azimuthDeltaPerYUnit;
            var rawAltitudeDeltaPerXUnit = altitudeDeltaPerXUnit;
            ClampSmallCrossAxisTerms(ref azimuthDeltaPerXUnit,
                                     ref altitudeDeltaPerXUnit,
                                     "X");
            ClampSmallCrossAxisTerms(ref azimuthDeltaPerYUnit,
                                     ref altitudeDeltaPerYUnit,
                                     "Y");

            responseModel = new ResponseModel(
                azimuthDeltaPerXUnit: azimuthDeltaPerXUnit,
                azimuthDeltaPerYUnit: azimuthDeltaPerYUnit,
                altitudeDeltaPerXUnit: altitudeDeltaPerXUnit,
                altitudeDeltaPerYUnit: altitudeDeltaPerYUnit);
            Logger.Info($"Automated polar-alignment model: Az/X={Math.Round(responseModel.AzimuthDeltaPerXUnit * 60.0, 4)}'/unit, Az/Y={Math.Round(responseModel.AzimuthDeltaPerYUnit * 60.0, 4)}'/unit (raw {Math.Round(rawAzimuthDeltaPerYUnit * 60.0, 4)}'), Alt/X={Math.Round(responseModel.AltitudeDeltaPerXUnit * 60.0, 4)}'/unit (raw {Math.Round(rawAltitudeDeltaPerXUnit * 60.0, 4)}'), Alt/Y={Math.Round(responseModel.AltitudeDeltaPerYUnit * 60.0, 4)}'/unit.");
            return true;
        }

        private static void ClampSmallCrossAxisTerms(ref double azimuthResponse, ref double altitudeResponse, string columnName) {
            var azimuthMagnitude = Math.Abs(azimuthResponse);
            var altitudeMagnitude = Math.Abs(altitudeResponse);

            if (azimuthMagnitude >= altitudeMagnitude) {
                ClampSecondaryResponse(ref altitudeResponse, azimuthMagnitude, columnName, "altitude");
            } else {
                ClampSecondaryResponse(ref azimuthResponse, altitudeMagnitude, columnName, "azimuth");
            }
        }

        private static void ClampSecondaryResponse(ref double secondaryResponse, double primaryMagnitude, string columnName, string componentName) {
            var secondaryMagnitude = Math.Abs(secondaryResponse);
            if (primaryMagnitude > MinimumAxisResponseDegreesPerUnit
                && secondaryMagnitude < primaryMagnitude * MaximumCrossAxisToPrimaryRatio) {
                if (secondaryMagnitude > 0) {
                    Logger.Info($"Clamped automated polar-alignment {componentName} component in {columnName} column from {Math.Round(secondaryResponse * 60.0, 4)}'/unit to 0.");
                }
                secondaryResponse = 0;
            }
        }

        /// <summary>
        /// Applies the controller's deadband and move clamp to a raw command magnitude.
        /// </summary>
        private static double NormalizeMagnitude(double magnitude) {
            if (Math.Abs(magnitude) < MinimumMoveMagnitude) {
                return 0;
            }

            if (magnitude > MaximumMoveMagnitude) {
                return MaximumMoveMagnitude;
            }

            if (magnitude < -MaximumMoveMagnitude) {
                return -MaximumMoveMagnitude;
            }

            return magnitude;
        }

        private static double NormalizeMagnitude(double magnitude, double maximumMagnitude) {
            if (Math.Abs(magnitude) < MinimumMoveMagnitude) {
                return 0;
            }

            if (magnitude > maximumMagnitude) {
                return maximumMagnitude;
            }

            if (magnitude < -maximumMagnitude) {
                return -maximumMagnitude;
            }

            return magnitude;
        }

        private static double ApplyDeadband(double magnitude) {
            return Math.Abs(magnitude) < MinimumMoveMagnitude ? 0 : magnitude;
        }

        private sealed class PendingPlan {
            public PendingPlan(AutomatedAdjustmentPlan plan, AutomatedAdjustmentObservation beforeMoveObservation) {
                Plan = plan;
                BeforeMoveObservation = beforeMoveObservation;
            }

            public AutomatedAdjustmentPlan Plan { get; }
            public AutomatedAdjustmentObservation BeforeMoveObservation { get; }
        }

        private sealed class ResponseSample {
            public ResponseSample(double xMagnitude, double yMagnitude, double azimuthDeltaDegrees, double altitudeDeltaDegrees) {
                XMagnitude = xMagnitude;
                YMagnitude = yMagnitude;
                AzimuthDeltaDegrees = azimuthDeltaDegrees;
                AltitudeDeltaDegrees = altitudeDeltaDegrees;
            }

            public double XMagnitude { get; }
            public double YMagnitude { get; }
            public double AzimuthDeltaDegrees { get; }
            public double AltitudeDeltaDegrees { get; }
        }

        private sealed class ResponseModel {
            public ResponseModel(double azimuthDeltaPerXUnit,
                                 double azimuthDeltaPerYUnit,
                                 double altitudeDeltaPerXUnit,
                                 double altitudeDeltaPerYUnit) {
                AzimuthDeltaPerXUnit = azimuthDeltaPerXUnit;
                AzimuthDeltaPerYUnit = azimuthDeltaPerYUnit;
                AltitudeDeltaPerXUnit = altitudeDeltaPerXUnit;
                AltitudeDeltaPerYUnit = altitudeDeltaPerYUnit;
            }

            public double AzimuthDeltaPerXUnit { get; }
            public double AzimuthDeltaPerYUnit { get; }
            public double AltitudeDeltaPerXUnit { get; }
            public double AltitudeDeltaPerYUnit { get; }
        }

        private sealed class CalibratedDirectFullTravelRoute {
            public CalibratedDirectFullTravelRoute(
                double azimuthStartingPositionDegrees,
                double azimuthMinimumDegrees,
                double azimuthMaximumDegrees,
                double altitudeStartingPositionDegrees,
                double altitudeMinimumDegrees,
                double altitudeMaximumDegrees,
                ResponseModel responseModel,
                double maximumXUnitsPerMove,
                double maximumYUnitsPerMove) {
                AzimuthStartingPositionDegrees = azimuthStartingPositionDegrees;
                AzimuthMinimumDegrees = azimuthMinimumDegrees;
                AzimuthMaximumDegrees = azimuthMaximumDegrees;
                AltitudeStartingPositionDegrees = altitudeStartingPositionDegrees;
                AltitudeMinimumDegrees = altitudeMinimumDegrees;
                AltitudeMaximumDegrees = altitudeMaximumDegrees;
                ResponseModel = responseModel;
                MaximumXUnitsPerMove = maximumXUnitsPerMove;
                MaximumYUnitsPerMove = maximumYUnitsPerMove;
            }

            public double AzimuthStartingPositionDegrees { get; }
            public double AzimuthMinimumDegrees { get; }
            public double AzimuthMaximumDegrees { get; }
            public double AltitudeStartingPositionDegrees { get; }
            public double AltitudeMinimumDegrees { get; }
            public double AltitudeMaximumDegrees { get; }
            public ResponseModel ResponseModel { get; }
            public double MaximumXUnitsPerMove { get; }
            public double MaximumYUnitsPerMove { get; }
        }
    }

    internal sealed class AutomatedAdjustmentPlan {
        public AutomatedAdjustmentPlan(double xMagnitude, double yMagnitude, bool isProbe, string reason) {
            XMagnitude = xMagnitude;
            YMagnitude = yMagnitude;
            IsProbe = isProbe;
            Reason = reason;
        }

        public double XMagnitude { get; }
        public double YMagnitude { get; }
        public bool IsProbe { get; }
        public string Reason { get; }
        public bool HasMovement => Math.Abs(XMagnitude) > 0 || Math.Abs(YMagnitude) > 0;

        public static AutomatedAdjustmentPlan Skip(string reason) => new AutomatedAdjustmentPlan(0, 0, false, reason);
    }

    internal sealed class AutomatedAdjustmentObservation {
        public AutomatedAdjustmentObservation(double azimuthErrorDegrees, double altitudeErrorDegrees) {
            AzimuthErrorDegrees = azimuthErrorDegrees;
            AltitudeErrorDegrees = altitudeErrorDegrees;
        }

        public double AzimuthErrorDegrees { get; }
        public double AltitudeErrorDegrees { get; }
        public double TotalErrorDegrees => Math.Sqrt(AzimuthErrorDegrees * AzimuthErrorDegrees + AltitudeErrorDegrees * AltitudeErrorDegrees);
    }
}
