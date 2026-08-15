// Requires the Persistence <Compile Link> lines in Agora.Core.Tests.csproj — AgoraJson, used here
// only as a value comparator. See the comment there for why those files are linked.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Agora.Core.Contracts;
using Agora.Core.Engine;
using Agora.Core.Tuning;
using Agora.Mod.Persistence;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// <c>PoliticalEngine.CloneState</c> carries every field of <see cref="PoliticalState"/>.
    ///
    /// <para>
    /// It is a hand-maintained field list, and the failure mode of a hand-maintained field list is
    /// silence. Adding a property to <see cref="PoliticalState"/> and forgetting to add it to the
    /// clone compiles, runs, and passes every existing test — the clone simply arrives at the
    /// property's default, and the value the caller set is gone by the next tick. That is not a
    /// hypothetical: <c>LastCompletedTickMonth</c> was added to the contract and not to the clone, so
    /// a state that had completed a month came back claiming it had never completed one, and the very
    /// duplicate-tick bug the field exists to close reappeared through <c>Retheme</c>.
    /// </para>
    ///
    /// <para>
    /// So the enumeration is reflective and the assertion is per property. Nothing here names a
    /// property, which is the entire point: the five collections the story system adds later are
    /// covered by this test the moment they exist, without anyone remembering to come back.
    /// </para>
    ///
    /// <para>
    /// <b>Narrow on purpose.</b> It asserts only that a value was carried, never how — no deep-copy
    /// semantics, because <c>ActiveEvents</c> is a deliberate shallow copy and
    /// <c>ElectionHistory</c> deliberately shares its elements. A reflective coupling test that
    /// asserts more than it has to is a test that goes red for reasons that are nobody's bug.
    /// </para>
    /// </summary>
    public class CloneStateCoverageTests
    {
        /// <summary>
        /// Properties the clone is allowed to return at something other than what it was given.
        /// Every entry needs a reason, because an unexplained exclusion is how this guard rots into
        /// uselessness: the next person with a failing property adds its name here and the test stops
        /// meaning anything.
        /// </summary>
        private static readonly Dictionary<string, string> Excluded = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // CloneState takes the date as an argument and assigns it. Advancing the clock is what the
            // call is for, so carrying the old date would be the defect.
            { "Date", "reassigned by design — CloneState(source, date) sets it from its parameter" }
        };

        /// <summary>
        /// The clone path with nothing else happening on it. A month that is not an engine tick
        /// returns <c>CloneState(prior, date)</c> and nothing more — no election, no lifecycle, no
        /// write-back — so anything that differs between the state going in and the state coming out
        /// is the clone, and not some other part of the tick.
        /// </summary>
        /// <remarks>
        /// <c>Retheme</c> reaches the same method but deliberately resets a dozen fields afterwards,
        /// which would mean a dozen exclusions and a much weaker test.
        /// </remarks>
        [Fact]
        public void CloneState_CarriesEveryPropertyOfPoliticalState()
        {
            PoliticalState populated = Populate();

            // A twelve-month engine cadence, asked about the month after the anchor: TickPlanner says
            // no work is due, and Advance returns on the clone-and-exit branch.
            EngineTuning yearly = EngineTuning.FromJson("{\"scheduler\":{\"tickIntervalMonths\":12}}");
            var start = new SimDate(1990, 1, 1);

            EngineTickResult result = PoliticalEngine.Advance(new EngineTickInput
            {
                SaveGuid = populated.SaveGuid,
                Date = start.AddMonths(1),
                StartDate = start,
                PriorState = populated,
                Tuning = yearly
            });

            Assert.False(result.DidWork);
            Assert.NotNull(result.State);

            var fresh = new PoliticalState();
            var missing = new List<string>();

            foreach (PropertyInfo property in Properties())
            {
                if (Excluded.ContainsKey(property.Name)) continue;

                object? given = property.GetValue(populated);
                object? returned = property.GetValue(result.State);

                // The guard on the guard. If the fixture never actually moved this property off its
                // default, "it came back carried" would be true of a clone that dropped it entirely.
                Assert.True(Fingerprint(given) != Fingerprint(property.GetValue(fresh)),
                            "The fixture left " + property.Name + " at its default, so this test " +
                            "cannot tell a carried value from a dropped one. Teach Populate about it.");

                if (Fingerprint(given) != Fingerprint(returned)) missing.Add(property.Name);
            }

            Assert.True(missing.Count == 0,
                        "PoliticalEngine.CloneState did not carry: " + string.Join(", ", missing) +
                        ". It is a hand-maintained field list — add the property to it.");
        }

        /// <summary>
        /// The exclusion list may only name properties that exist. A stale entry is worse than none:
        /// it silently exempts nothing today and would exempt a brand new property tomorrow if the
        /// name were ever reused.
        /// </summary>
        [Fact]
        public void EveryExclusion_NamesAPropertyThatStillExists()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (PropertyInfo property in Properties()) names.Add(property.Name);

            foreach (KeyValuePair<string, string> exclusion in Excluded)
            {
                Assert.True(names.Contains(exclusion.Key),
                            "PoliticalState has no property named " + exclusion.Key +
                            "; the exclusion is stale.");
                Assert.NotEmpty(exclusion.Value);
            }
        }

        // --- the fixture ---------------------------------------------------------------------------

        /// <summary>
        /// Every readable, writable public property of <see cref="PoliticalState"/>, sorted by name.
        /// Sorted because <c>GetProperties</c> makes no ordering promise, and a failure message that
        /// lists properties in a different order on different runs is a failure message nobody trusts.
        /// </summary>
        private static List<PropertyInfo> Properties()
        {
            var properties = new List<PropertyInfo>();

            foreach (PropertyInfo property in typeof(PoliticalState)
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
        /// A state in which every property holds something no freshly constructed one does.
        /// </summary>
        /// <remarks>
        /// The values are seeded by type rather than by name, so a property added to the contract
        /// later is populated without anyone editing this method — which is what makes the test cover
        /// a field nobody has written yet. A property whose type this does not recognise throws
        /// rather than being skipped: skipping would quietly shrink the guard back to whatever it
        /// happened to understand.
        /// </remarks>
        private static PoliticalState Populate()
        {
            var state = new PoliticalState();
            int counter = 0;

            foreach (PropertyInfo property in Properties())
            {
                counter++;
                property.SetValue(state, Seed(property.PropertyType, property.Name, counter,
                                              property.GetValue(state)));
            }

            return state;
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
                return SingletonList(bare);
            }

            if (bare.IsClass)
            {
                // A default-constructed instance is indistinguishable from the one the property
                // already holds, so it is perturbed until it is not.
                object instance = Activator.CreateInstance(bare)!;
                Perturb(instance);
                return instance;
            }

            throw new Xunit.Sdk.XunitException(
                "PoliticalState." + name + " is a " + bare.Name + ", which this fixture does not know " +
                "how to make distinguishable. Teach Seed about it — silently skipping it would leave " +
                "the property uncovered by the clone guard.");
        }

        /// <summary>A one-element list, which no freshly constructed empty list can be mistaken for.</summary>
        private static object SingletonList(Type listType)
        {
            Type element = listType.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(listType)!;

            // string has no parameterless constructor to activate, and an empty string would be a
            // perfectly good element anyway — it is the count that distinguishes the list.
            list.Add(element == typeof(string) ? "carried" : Activator.CreateInstance(element));
            return list;
        }

        /// <summary>
        /// Writes a sentinel into every simple property of <paramref name="instance"/>, so that a
        /// default-constructed object stops looking like one. Only the scalar types are touched:
        /// nested collections are left alone, because this test asserts that a value was carried and
        /// not how deeply it was copied.
        /// </summary>
        private static void Perturb(object instance)
        {
            int counter = 0;

            foreach (PropertyInfo property in instance.GetType()
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite) continue;
                if (property.GetIndexParameters().Length > 0) continue;

                Type bare = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                counter++;

                if (bare == typeof(int)) property.SetValue(instance, 300 + counter);
                else if (bare == typeof(long)) property.SetValue(instance, 300L + counter);
                else if (bare == typeof(double)) property.SetValue(instance, 0.25 + counter);
                else if (bare == typeof(string)) property.SetValue(instance, "perturbed-" + counter);
                else if (bare == typeof(bool))
                {
                    property.SetValue(instance, !(property.GetValue(instance) is bool f && f));
                }
            }
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

        /// <summary>
        /// Value identity for a property, whatever its type. Serialization rather than
        /// <c>Equals</c>, because the clone rebuilds lists and objects — reference equality would
        /// report every carried collection as missing, and <c>Equals</c> on a <c>List&lt;T&gt;</c> is
        /// reference equality.
        /// </summary>
        private static string Fingerprint(object? value) =>
            value == null ? "<null>" : AgoraJson.Fingerprint(value);
    }
}
