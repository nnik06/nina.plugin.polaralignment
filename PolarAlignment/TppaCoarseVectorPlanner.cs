using System;

namespace NINA.Plugins.PolarAlignment {
    internal sealed record TppaCoarseErrorEvidence(
        double AzimuthMinutes,
        double AltitudeMinutes,
        double CovarianceAzAzSquareMinutes,
        double CovarianceAzAltSquareMinutes,
        double CovarianceAltAltSquareMinutes) {
        public double TotalMinutes => Math.Sqrt(
            AzimuthMinutes * AzimuthMinutes + AltitudeMinutes * AltitudeMinutes);
    }

    internal sealed record UpasMillidegreeInterval(int Lower, int Upper) {
        public double LowerDegrees => Lower / 1000.0;
        public double UpperDegrees => Upper / 1000.0;
    }

    internal sealed record TppaCoarseVectorPlan(
        bool IsAuthorized,
        bool RequiresMove,
        double RequestedAzimuthDeltaDegrees,
        double RequestedAltitudeDeltaDegrees,
        double AzimuthCorrectionModelStandardUncertaintyDegrees,
        double AltitudeCorrectionModelStandardUncertaintyDegrees,
        double AzimuthAdditiveBoundDegrees,
        double AltitudeAdditiveBoundDegrees,
        UpasMillidegreeInterval AzimuthExpandedPath,
        UpasMillidegreeInterval AltitudeExpandedPath,
        double ProjectedResidualMinutes,
        double ProjectedResidualUpperMinutes,
        string Reason);

    /// <summary>
    /// Produces a non-actuating, uncertainty-expanded coarse correction plan.
    /// A separate commissioned transaction must revalidate and execute it.
    /// </summary>
    internal static class TppaCoarseVectorPlanner {
        public const double MaximumObjectiveTotalErrorMinutes = 300.0;
        public const double FineControllerHandoffMinutes = 24.0;
        public const double CoarseCorrectionFraction = 0.90;
        public const double StandardUncertaintyMultiplier = 3.0;
        public const int MillidegreesPerDegree = 1000;
        private const double CovarianceTolerance = 1e-12;
        private static readonly UpasMillidegreeInterval EmptyInterval = new(0, 0);

        public static TppaCoarseVectorPlan Plan(
                TppaCoarseErrorEvidence error,
                UpasSupervisorAxesEvidence axes,
                UpasSupervisorCoarseResponseCalibration calibration,
                UpasSupervisorCoarseRuntimeConstraints runtime) {
            if (error == null) {
                throw new ArgumentNullException(nameof(error));
            }
            if (axes == null) {
                throw new ArgumentNullException(nameof(axes));
            }
            if (calibration == null) {
                throw new ArgumentNullException(nameof(calibration));
            }
            if (runtime == null) {
                throw new ArgumentNullException(nameof(runtime));
            }
            ValidateError(error);
            ValidateAxes(axes);
            ValidateRuntime(runtime);
            if (!runtime.PlanningEvidence
                    || !runtime.PhysicalMotionAvailable
                    || !runtime.AtomicBudgetReservationAvailable
                    || runtime.MotionAuthorityIncluded) {
                return Denied(
                    "runtime capabilities cannot support non-actuating coarse planning");
            }

            if (error.TotalMinutes > MaximumObjectiveTotalErrorMinutes) {
                return Denied(
                    $"total TPPA error {error.TotalMinutes:F2}' exceeds the " +
                    $"{MaximumObjectiveTotalErrorMinutes:F0}' coarse objective envelope");
            }
            if (error.TotalMinutes <= FineControllerHandoffMinutes) {
                return new TppaCoarseVectorPlan(
                    true, false, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
                    PointInterval(axes.Azimuth.PositionDegrees),
                    PointInterval(axes.Altitude.PositionDegrees),
                    error.TotalMinutes, error.TotalMinutes,
                    "fresh signed error vector is already inside the fine-controller handoff");
            }

            var r = Matrix(calibration.ResponseMatrix);
            var determinant = r[0, 0] * r[1, 1] - r[0, 1] * r[1, 0];
            if (!double.IsFinite(determinant) || Math.Abs(determinant) <= CovarianceTolerance
                    || !double.IsFinite(calibration.ResponseMatrix.ConditionNumber)
                    || calibration.ResponseMatrix.ConditionNumber
                        > UpasSupervisorCoarseResponseCalibrationParser.MaximumConditionNumber) {
                return Denied("response matrix is singular or exceeds the compiled condition limit");
            }
            var inverse = new[,] {
                { r[1, 1] / determinant, -r[0, 1] / determinant },
                { -r[1, 0] / determinant, r[0, 0] / determinant }
            };
            var e = new[] { error.AzimuthMinutes / 60.0, error.AltitudeMinutes / 60.0 };
            var requested = Scale(Multiply(inverse, e), -CoarseCorrectionFraction);
            if (!AllFinite(requested)) {
                return Denied("response inversion produced a non-finite correction vector");
            }

            var errorCovariance = new[,] {
                { error.CovarianceAzAzSquareMinutes / 3600.0,
                  error.CovarianceAzAltSquareMinutes / 3600.0 },
                { error.CovarianceAzAltSquareMinutes / 3600.0,
                  error.CovarianceAltAltSquareMinutes / 3600.0 }
            };
            var commandCovariance = CommandCovariance(
                inverse, errorCovariance, calibration.MatrixElementCovariance, requested);
            if (!IsPositiveSemidefinite2(commandCovariance)) {
                return Denied("propagated command covariance is invalid");
            }
            var commandSigma = new[] {
                Math.Sqrt(Math.Max(0.0, commandCovariance[0, 0])),
                Math.Sqrt(Math.Max(0.0, commandCovariance[1, 1]))
            };
            var additive = new[] {
                requested[0] == 0.0
                    ? 0.0
                    : calibration.FixedAzimuthCommandUncertaintyDegrees
                        + ApplicableDeadband(
                        requested[0], axes.Azimuth.EngagementState,
                        calibration.AzimuthDeadbandDegrees),
                requested[1] == 0.0
                    ? 0.0
                    : calibration.FixedAltitudeCommandUncertaintyDegrees
                        + ApplicableDeadband(
                        requested[1], axes.Altitude.EngagementState,
                        calibration.AltitudeDeadbandDegrees)
            };

            var azPath = ExpandedPath(
                axes.Azimuth.PositionDegrees,
                Math.Sqrt(axes.CovarianceAzAzSquareDegrees),
                requested[0], additive[0]);
            var altPath = ExpandedPath(
                axes.Altitude.PositionDegrees,
                Math.Sqrt(axes.CovarianceAltAltSquareDegrees),
                requested[1], additive[1]);
            var azimuthTravelCost = Math.Abs(requested[0]) + additive[0];
            var altitudeTravelCost = Math.Abs(requested[1]) + additive[1];
            if (!DirectionalBudgetCovers(runtime.Azimuth, requested[0], azimuthTravelCost)
                    || !DirectionalBudgetCovers(runtime.Altitude, requested[1], altitudeTravelCost)) {
                return Denied("directional supervisor travel budget does not cover the expanded plan",
                    requested, commandSigma, additive, azPath, altPath);
            }
            if (azimuthTravelCost + altitudeTravelCost > runtime.CumulativeSessionDegrees) {
                return Denied("cumulative supervisor travel budget does not cover the expanded plan",
                    requested, commandSigma, additive, azPath, altPath);
            }
            var operationalLimit = UpasCoarsePlanningSafetyPolicy.PhysicalHardLimitDegrees
                - UpasCoarsePlanningSafetyPolicy.MinimumReservedTravelDegrees;
            var hardLimit = UpasCoarsePlanningSafetyPolicy.PhysicalHardLimitDegrees;
            if (!StrictlyInside(azPath, operationalLimit)
                    || !StrictlyInside(altPath, operationalLimit)) {
                return Denied(
                    "uncertainty-expanded path does not remain strictly inside the compiled operational envelope",
                    requested, commandSigma, additive, azPath, altPath);
            }
            if (!StrictlyInside(azPath, hardLimit) || !StrictlyInside(altPath, hardLimit)) {
                return Denied(
                    "uncertainty-expanded path does not remain strictly inside the compiled hard limits",
                    requested, commandSigma, additive, azPath, altPath);
            }
            if (!EndpointInsideCalibration(
                    axes.Azimuth.PositionDegrees,
                    Math.Sqrt(axes.CovarianceAzAzSquareDegrees), requested[0], additive[0],
                    calibration.MinimumAzimuthPositionDegrees,
                    calibration.MaximumAzimuthPositionDegrees)
                    || !EndpointInsideCalibration(
                        axes.Altitude.PositionDegrees,
                        Math.Sqrt(axes.CovarianceAltAltSquareDegrees), requested[1], additive[1],
                        calibration.MinimumAltitudePositionDegrees,
                        calibration.MaximumAltitudePositionDegrees)) {
                return Denied(
                    "uncertainty-expanded endpoint is outside response-calibration applicability",
                    requested, commandSigma, additive, azPath, altPath);
            }

            var nominalResidual = Add(e, Multiply(r, requested));
            var matrixResidualCovariance = MatrixOutputCovariance(
                calibration.MatrixElementCovariance, requested);
            var residualCovariance = Add(errorCovariance, matrixResidualCovariance);
            if (!IsPositiveSemidefinite2(residualCovariance)) {
                return Denied(
                    "propagated residual covariance is invalid",
                    requested, commandSigma, additive, azPath, altPath);
            }
            var randomResidualRadius = StandardUncertaintyMultiplier
                * Math.Sqrt(Math.Max(0.0,
                    residualCovariance[0, 0] + residualCovariance[1, 1]));
            var additiveResidual = new[] {
                Math.Abs(r[0, 0]) * additive[0] + Math.Abs(r[0, 1]) * additive[1],
                Math.Abs(r[1, 0]) * additive[0] + Math.Abs(r[1, 1]) * additive[1]
            };
            var nominalResidualDegrees = Norm(nominalResidual);
            var residualUpperDegrees = nominalResidualDegrees
                + randomResidualRadius + Norm(additiveResidual);
            if (!double.IsFinite(residualUpperDegrees)
                    || nominalResidualDegrees >= error.TotalMinutes / 60.0) {
                return Denied(
                    "coarse vector does not provide finite nominal progress",
                    requested, commandSigma, additive, azPath, altPath);
            }

            return new TppaCoarseVectorPlan(
                true, true,
                requested[0], requested[1],
                commandSigma[0], commandSigma[1],
                additive[0], additive[1],
                azPath, altPath,
                nominalResidualDegrees * 60.0,
                residualUpperDegrees * 60.0,
                "full-vector coarse plan is uncertainty-expanded and non-actuating; " +
                "a commissioned transaction must revalidate all evidence before motion");
        }

        private static double[,] CommandCovariance(
                double[,] inverse,
                double[,] errorCovariance,
                double[,] matrixCovariance,
                double[] requested) {
            var errorJacobian = Scale(inverse, -CoarseCorrectionFraction);
            var fromError = Sandwich(errorJacobian, errorCovariance);
            var matrixJacobian = new[,] {
                { -inverse[0, 0] * requested[0], -inverse[0, 0] * requested[1],
                  -inverse[0, 1] * requested[0], -inverse[0, 1] * requested[1] },
                { -inverse[1, 0] * requested[0], -inverse[1, 0] * requested[1],
                  -inverse[1, 1] * requested[0], -inverse[1, 1] * requested[1] }
            };
            return Add(fromError, Sandwich(matrixJacobian, matrixCovariance));
        }

        private static double[,] MatrixOutputCovariance(
                double[,] matrixCovariance,
                double[] requested) {
            var jacobian = new[,] {
                { requested[0], requested[1], 0.0, 0.0 },
                { 0.0, 0.0, requested[0], requested[1] }
            };
            return Sandwich(jacobian, matrixCovariance);
        }

        private static double ApplicableDeadband(
                double requestedDelta,
                UpasAxisEngagementState engagement,
                UpasDirectionalBounds bounds) {
            if (requestedDelta == 0.0) {
                return 0.0;
            }
            if (requestedDelta > 0.0 && engagement == UpasAxisEngagementState.Positive
                    || requestedDelta < 0.0 && engagement == UpasAxisEngagementState.Negative) {
                return 0.0;
            }
            if (engagement == UpasAxisEngagementState.Unknown) {
                return bounds.WorstDegrees;
            }
            return requestedDelta > 0.0 ? bounds.PositiveDegrees : bounds.NegativeDegrees;
        }

        private static bool DirectionalBudgetCovers(
                UpasDirectionalTravelBudget budget,
                double requestedDelta,
                double travelCost) {
            if (requestedDelta > 0.0) {
                return travelCost <= budget.PositiveDegrees;
            }
            if (requestedDelta < 0.0) {
                return travelCost <= budget.NegativeDegrees;
            }
            return travelCost == 0.0;
        }

        private static UpasMillidegreeInterval ExpandedPath(
                double position,
                double positionSigma,
                double delta,
                double additiveBound) {
            var startRadius = StandardUncertaintyMultiplier * positionSigma;
            var endpointRadius = startRadius + additiveBound;
            var lower = Math.Min(position - startRadius, position + delta - endpointRadius);
            var upper = Math.Max(position + startRadius, position + delta + endpointRadius);
            return new UpasMillidegreeInterval(RoundLower(lower), RoundUpper(upper));
        }

        private static bool EndpointInsideCalibration(
                double position,
                double positionSigma,
                double delta,
                double additiveBound,
                double minimum,
                double maximum) {
            var radius = StandardUncertaintyMultiplier * positionSigma + additiveBound;
            return position + delta - radius > minimum
                && position + delta + radius < maximum;
        }

        private static bool StrictlyInside(UpasMillidegreeInterval interval, double limit) {
            var limitMillidegrees = checked((int)Math.Round(
                limit * MillidegreesPerDegree, MidpointRounding.AwayFromZero));
            return interval.Lower > -limitMillidegrees && interval.Upper < limitMillidegrees;
        }

        private static int RoundLower(double degrees) => checked((int)Math.Floor(
            degrees * MillidegreesPerDegree));

        private static int RoundUpper(double degrees) => checked((int)Math.Ceiling(
            degrees * MillidegreesPerDegree));

        private static UpasMillidegreeInterval PointInterval(double degrees) =>
            new(RoundLower(degrees), RoundUpper(degrees));

        private static double[,] Matrix(UpasResponseMatrix value) => new[,] {
            { value.AzErrorPerAzDelta, value.AzErrorPerAltDelta },
            { value.AltErrorPerAzDelta, value.AltErrorPerAltDelta }
        };

        private static double[] Multiply(double[,] matrix, double[] vector) {
            var result = new double[matrix.GetLength(0)];
            for (var row = 0; row < matrix.GetLength(0); row++) {
                for (var column = 0; column < matrix.GetLength(1); column++) {
                    result[row] += matrix[row, column] * vector[column];
                }
            }
            return result;
        }

        private static double[,] Multiply(double[,] first, double[,] second) {
            var result = new double[first.GetLength(0), second.GetLength(1)];
            for (var row = 0; row < result.GetLength(0); row++) {
                for (var column = 0; column < result.GetLength(1); column++) {
                    for (var index = 0; index < first.GetLength(1); index++) {
                        result[row, column] += first[row, index] * second[index, column];
                    }
                }
            }
            return result;
        }

        private static double[,] Sandwich(double[,] jacobian, double[,] covariance) =>
            Multiply(Multiply(jacobian, covariance), Transpose(jacobian));

        private static double[,] Transpose(double[,] value) {
            var result = new double[value.GetLength(1), value.GetLength(0)];
            for (var row = 0; row < value.GetLength(0); row++) {
                for (var column = 0; column < value.GetLength(1); column++) {
                    result[column, row] = value[row, column];
                }
            }
            return result;
        }

        private static double[,] Scale(double[,] value, double factor) {
            var result = new double[value.GetLength(0), value.GetLength(1)];
            for (var row = 0; row < value.GetLength(0); row++) {
                for (var column = 0; column < value.GetLength(1); column++) {
                    result[row, column] = factor * value[row, column];
                }
            }
            return result;
        }

        private static double[] Scale(double[] value, double factor) =>
            new[] { factor * value[0], factor * value[1] };

        private static double[] Add(double[] first, double[] second) =>
            new[] { first[0] + second[0], first[1] + second[1] };

        private static double[,] Add(double[,] first, double[,] second) => new[,] {
            { first[0, 0] + second[0, 0], first[0, 1] + second[0, 1] },
            { first[1, 0] + second[1, 0], first[1, 1] + second[1, 1] }
        };

        private static double Norm(double[] value) =>
            Math.Sqrt(value[0] * value[0] + value[1] * value[1]);

        private static bool AllFinite(double[] values) =>
            double.IsFinite(values[0]) && double.IsFinite(values[1]);

        private static bool IsPositiveSemidefinite2(double[,] value) =>
            AllFinite(new[] { value[0, 0], value[0, 1], value[1, 0], value[1, 1] })
            && Math.Abs(value[0, 1] - value[1, 0]) <= CovarianceTolerance
            && value[0, 0] >= -CovarianceTolerance
            && value[1, 1] >= -CovarianceTolerance
            && value[0, 0] * value[1, 1] - value[0, 1] * value[0, 1]
                >= -CovarianceTolerance;

        private static void ValidateError(TppaCoarseErrorEvidence error) {
            RequireFinite(error.AzimuthMinutes, nameof(error));
            RequireFinite(error.AltitudeMinutes, nameof(error));
            RequireFinite(error.CovarianceAzAzSquareMinutes, nameof(error));
            RequireFinite(error.CovarianceAzAltSquareMinutes, nameof(error));
            RequireFinite(error.CovarianceAltAltSquareMinutes, nameof(error));
            var covariance = new[,] {
                { error.CovarianceAzAzSquareMinutes, error.CovarianceAzAltSquareMinutes },
                { error.CovarianceAzAltSquareMinutes, error.CovarianceAltAltSquareMinutes }
            };
            if (!IsPositiveSemidefinite2(covariance)) {
                throw new ArgumentOutOfRangeException(nameof(error), "TPPA error covariance must be PSD.");
            }
        }

        private static void ValidateAxes(UpasSupervisorAxesEvidence axes) {
            RequireFinite(axes.Azimuth.PositionDegrees, nameof(axes));
            RequireFinite(axes.Altitude.PositionDegrees, nameof(axes));
            var covariance = new[,] {
                { axes.CovarianceAzAzSquareDegrees, axes.CovarianceAzAltSquareDegrees },
                { axes.CovarianceAzAltSquareDegrees, axes.CovarianceAltAltSquareDegrees }
            };
            if (!IsPositiveSemidefinite2(covariance)) {
                throw new ArgumentOutOfRangeException(nameof(axes), "Axis covariance must be PSD.");
            }
        }

        private static void ValidateRuntime(UpasSupervisorCoarseRuntimeConstraints runtime) {
            RequireNonNegativeFinite(runtime.Azimuth.PositiveDegrees, nameof(runtime));
            RequireNonNegativeFinite(runtime.Azimuth.NegativeDegrees, nameof(runtime));
            RequireNonNegativeFinite(runtime.Altitude.PositiveDegrees, nameof(runtime));
            RequireNonNegativeFinite(runtime.Altitude.NegativeDegrees, nameof(runtime));
            RequireNonNegativeFinite(runtime.CumulativeSessionDegrees, nameof(runtime));
        }

        private static void RequireNonNegativeFinite(double value, string name) {
            RequireFinite(value, name);
            if (value < 0.0) {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void RequireFinite(double value, string name) {
            if (!double.IsFinite(value) || value == 0.0 && BitConverter.DoubleToInt64Bits(value) < 0) {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static TppaCoarseVectorPlan Denied(
                string reason,
                double[] requested = null,
                double[] sigma = null,
                double[] additive = null,
                UpasMillidegreeInterval azPath = null,
                UpasMillidegreeInterval altPath = null) =>
            new(false, false,
                requested?[0] ?? 0.0, requested?[1] ?? 0.0,
                sigma?[0] ?? 0.0, sigma?[1] ?? 0.0,
                additive?[0] ?? 0.0, additive?[1] ?? 0.0,
                azPath ?? EmptyInterval, altPath ?? EmptyInterval,
                double.NaN, double.NaN, reason);
    }
}
