using System;
using NINA.Core.Utility;

namespace NINA.Plugins.PolarAlignment {
    internal sealed class FixedObservationDateTime : ICustomDateTime {
        private readonly DateTime dateTime;

        public FixedObservationDateTime(DateTime dateTime) {
            this.dateTime = dateTime.Kind == DateTimeKind.Utc
                ? dateTime
                : dateTime.ToUniversalTime();
        }

        public DateTime Now => dateTime;

        public DateTime UtcNow => dateTime;
    }
}
