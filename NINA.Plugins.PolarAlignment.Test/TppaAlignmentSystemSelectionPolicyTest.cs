using FluentAssertions;

namespace NINA.Plugins.PolarAlignment.Test {
    public class TppaAlignmentSystemSelectionPolicyTest {
        [Test]
        public void ResolvePersistedSelection_SelectsUpasWhenLegacySettingIsEnabledAndSelectorIsUnset() {
            TppaAlignmentSystemSelectionPolicy.ResolvePersistedSelection(null, legacyUpasEnabled: true)
                .Should().Be(PolarAlignmentSystemType.UPAS.ToString());
        }

        [TestCase("")]
        [TestCase(" ")]
        public void ResolvePersistedSelection_SelectsUpasWhenLegacySettingIsEnabledAndSelectorIsBlank(string selectedSystem) {
            TppaAlignmentSystemSelectionPolicy.ResolvePersistedSelection(selectedSystem, legacyUpasEnabled: true)
                .Should().Be(PolarAlignmentSystemType.UPAS.ToString());
        }

        [Test]
        public void ResolvePersistedSelection_SelectsUpasWhenLegacySettingIsEnabledAndSelectorIsNone() {
            TppaAlignmentSystemSelectionPolicy.ResolvePersistedSelection(
                    PolarAlignmentSystemType.None.ToString(),
                    legacyUpasEnabled: true)
                .Should().Be(PolarAlignmentSystemType.UPAS.ToString());
        }

        [TestCase("OAPA")]
        [TestCase("UPAS")]
        public void ResolvePersistedSelection_PreservesAnExplicitChoice(string selectedSystem) {
            TppaAlignmentSystemSelectionPolicy.ResolvePersistedSelection(selectedSystem, legacyUpasEnabled: true)
                .Should().Be(selectedSystem);
        }

        [Test]
        public void ResolvePersistedSelection_DoesNotEnableUpasWithoutLegacyOptIn() {
            TppaAlignmentSystemSelectionPolicy.ResolvePersistedSelection(null, legacyUpasEnabled: false)
                .Should().BeNull();
        }
    }
}
