// Requires the Persistence <Compile Link> lines in Agora.Core.Tests.csproj — AgoraJson, used here
// only as a value comparator. See the comment there for why those files are linked.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Agora.Core.Contracts;
using Agora.Mod.Persistence;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// <see cref="AgoraSettings.Clone"/> carries every property of <see cref="AgoraSettings"/>.
    ///
    /// <para>
    /// The sibling of <c>CloneStateCoverageTests</c>, and it exists for the same reason: a
    /// hand-maintained field list fails silently. <see cref="AgoraSettings.Clone"/>'s own remarks say
    /// a forgotten property "silently reverts to its default the first time a player changes their
    /// theme", and that the two places are kept "one screen apart so that the omission is visible" —
    /// which is a claim about human attention, not a check. This is the check.
    /// </para>
    ///
    /// <para>
    /// It was missing while the settings object grew from eight properties to twenty. Nothing had
    /// gone wrong, but nothing would have said so: the failure surfaces only through
    /// <c>PoliticalEngine.Retheme</c>, which is the one caller that clones settings, and only as a
    /// setting quietly reverting on a path most saves never take.
    /// </para>
    ///
    /// <para>
    /// <b>Narrow on purpose</b>, exactly as the state guard is: it asserts that a value was carried,
    /// never how. Settings are all scalars and enums today, so there is no deep-copy question to beg.
    /// </para>
    /// </summary>
    public class SettingsCloneCoverageTests
    {
        /// <summary>
        /// Properties <see cref="AgoraSettings.Clone"/> is allowed to return at something other than
        /// what it was given. Empty, and it should stay that way — <c>Clone</c> is a plain copy with
        /// no reinterpretation to do. Every future entry needs a reason, because an unexplained
        /// exclusion is how a guard like this rots into uselessness.
        /// </summary>
        private static readonly Dictionary<string, string> Excluded =
            new Dictionary<string, string>(StringComparer.Ordinal);

        [Fact]
        public void Clone_CarriesEveryPropertyOfAgoraSettings()
        {
            AgoraSettings populated = Populate();
            AgoraSettings clone = populated.Clone();

            var fresh = new AgoraSettings();
            var missing = new List<string>();

            foreach (PropertyInfo property in Properties())
            {
                if (Excluded.ContainsKey(property.Name)) continue;

                object? given = property.GetValue(populated);
                object? returned = property.GetValue(clone);

                // The guard on the guard. If the fixture never moved this property off its default,
                // "it came back carried" would be equally true of a clone that dropped it.
                Assert.True(Fingerprint(given) != Fingerprint(property.GetValue(fresh)),
                            "The fixture left " + property.Name + " at its default, so this test " +
                            "cannot tell a carried value from a dropped one. Teach Seed about it.");

                if (Fingerprint(given) != Fingerprint(returned)) missing.Add(property.Name);
            }

            Assert.True(missing.Count == 0,
                        "AgoraSettings.Clone did not carry: " + string.Join(", ", missing) +
                        ". It is a hand-maintained field list — add the property to it.");
        }

        /// <summary>
        /// A stale exclusion is worse than none: it exempts nothing today and would exempt a brand
        /// new property tomorrow if the name were ever reused.
        /// </summary>
        [Fact]
        public void EveryExclusion_NamesAPropertyThatStillExists()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (PropertyInfo property in Properties()) names.Add(property.Name);

            foreach (KeyValuePair<string, string> exclusion in Excluded)
            {
                Assert.True(names.Contains(exclusion.Key),
                            "AgoraSettings has no property named " + exclusion.Key +
                            "; the exclusion is stale.");
                Assert.NotEmpty(exclusion.Value);
            }
        }

        // --- the fixture ---------------------------------------------------------------------------

        /// <summary>
        /// Every readable, writable public property of <see cref="AgoraSettings"/>, sorted by name.
        /// Sorted because <c>GetProperties</c> makes no ordering promise, and a failure message whose
        /// order changes between runs is one nobody trusts.
        /// </summary>
        private static List<PropertyInfo> Properties()
        {
            var properties = new List<PropertyInfo>();

            foreach (PropertyInfo property in typeof(AgoraSettings)
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite) continue;
                if (property.GetIndexParameters().Length > 0) continue;
                properties.Add(property);
            }

            properties.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return properties;
        }

        /// <summary>
        /// A settings object with every property moved off its default.
        /// </summary>
        /// <remarks>
        /// A property whose type this does not recognise <b>throws</b> rather than being skipped.
        /// A silent skip would shrink the guard back to whatever it happened to cover and it would
        /// stop failing without anyone noticing — which is the failure mode of the very thing it is
        /// guarding.
        /// </remarks>
        private static AgoraSettings Populate()
        {
            var settings = new AgoraSettings();
            int counter = 0;

            foreach (PropertyInfo property in Properties())
            {
                counter++;
                property.SetValue(settings, Seed(property.PropertyType, property.Name, counter,
                                                 property.GetValue(settings)));
            }

            return settings;
        }

        private static object? Seed(Type type, string name, int counter, object? current)
        {
            Type bare = Nullable.GetUnderlyingType(type) ?? type;

            if (bare == typeof(int)) return 4_200 + counter;
            if (bare == typeof(long)) return 4_200L + counter;
            if (bare == typeof(double)) return 0.5 + counter;
            if (bare == typeof(bool)) return !(current is bool flag && flag);
            if (bare == typeof(string)) return "carried-" + name;
            if (bare == typeof(Guid))
            {
                return new Guid("c10e5747-0000-4000-8000-" +
                                counter.ToString("D12", CultureInfo.InvariantCulture));
            }
            if (bare == typeof(SimDate)) return new SimDate(2000 + counter % 50, 1 + counter % 12, 1);

            if (bare.IsEnum) return NextEnumValue(bare, current);

            if (bare.IsGenericType && bare.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type element = bare.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(bare)!;
                list.Add(element == typeof(string) ? "carried" : Activator.CreateInstance(element));
                return list;
            }

            throw new Xunit.Sdk.XunitException(
                "AgoraSettings." + name + " is a " + bare.Name + ", which this fixture does not know " +
                "how to make distinguishable. Teach Seed about it — silently skipping it would leave " +
                "the property uncovered by the clone guard.");
        }

        private static object NextEnumValue(Type enumType, object? current)
        {
            Array values = Enum.GetValues(enumType);
            for (int i = 0; i < values.Length; i++)
            {
                object candidate = values.GetValue(i)!;
                if (!Equals(candidate, current)) return candidate;
            }

            return values.GetValue(0)!;
        }

        private static string Fingerprint(object? value) =>
            value == null ? "<null>" : AgoraJson.Fingerprint(value);
    }
}
