using System;
using System.Collections.Generic;
using Agora.Mod.Core;
using Colossal.UI.Binding;
using Game.UI;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Shared plumbing for the four dashboard publishers: register bindings once, then republish when
    /// — and only when — the engine says something changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>docs/contracts/ui_bindings.md</c> §7 rule 10: cadence is per change, not per frame. A
    /// <c>ValueBinding.Update</c> with an unchanged payload still costs a bridge crossing on some
    /// versions, and every payload here is a freshly built reference type, so the binding's own
    /// equality check cannot save us — two structurally identical lists are still different objects.
    /// Watching <see cref="AgoraRuntime.StateVersion"/> is what keeps the dashboard on the engine's
    /// monthly cadence instead of the renderer's 60 Hz one.
    /// </para>
    /// <para>
    /// Publishing is wrapped: a throw here happens inside the UI update loop, and a dashboard that
    /// cannot render is not a reason to take the interface down. It logs once per version rather than
    /// once per frame, because the version does not advance until the engine ticks again.
    /// </para>
    /// <para>
    /// <b>So is binding registration, and that one is not merely tidiness — it is what keeps a bug
    /// here from making the game unable to start.</b> <c>GameSystemBase.OnCreate</c> subscribes the
    /// system to <c>GameManager.onGamePreload</c> <i>before</i> any derived body runs. If a derived
    /// <c>OnCreate</c> throws, Unity aborts creation and frees the system state, but
    /// <c>OnDestroy</c> — the only place that unsubscribes — never runs. The dead system stays on a
    /// process-lifetime event, and the next new game trips <c>set_Enabled</c> on it; the game's own
    /// handler then calls <c>Enabled = false</c> again from inside its catch, which rethrows, escapes
    /// the multicast invoke and kills the load before deserialization. Completing <c>OnCreate</c> is
    /// the whole defence: a live system unsubscribes itself properly.
    /// </para>
    /// </remarks>
    public abstract partial class AgoraUISystemBase : UISystemBase
    {
        private int _publishedVersion = -1;
        private bool _bindingsFailed;

        /// <summary>
        /// Registers every binding this system owns. Called once from <see cref="OnCreate"/>, inside
        /// the guard — so implementations must not call <c>base.OnCreate()</c> themselves.
        /// </summary>
        protected abstract void CreateBindings();

        /// <summary>Builds and pushes every payload this system owns.</summary>
        protected abstract void Publish();

        protected override void OnCreate()
        {
            base.OnCreate();

            try
            {
                CreateBindings();
            }
            catch (Exception ex)
            {
                // Fail closed: this panel is inert for the session, every other Agora system carries
                // on, and — critically — this system stays alive, so it is still a valid preload
                // subscriber rather than a corpse that bricks the next load.
                _bindingsFailed = true;
                AgoraMod.Log.Error(ex, "Agora dashboard publisher " + GetType().Name + " could not " +
                                       "register its bindings; its panel stays empty for this session.");
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            // Nothing was bound, so there is nothing to push and Publish would only NRE.
            if (_bindingsFailed) return;

            int version = AgoraRuntime.StateVersion;
            if (version == _publishedVersion) return;

            _publishedVersion = version;

            try
            {
                Publish();
            }
            catch (Exception ex)
            {
                AgoraMod.Log.Error(ex, "Agora dashboard publisher " + GetType().Name + " failed; the " +
                                       "panel keeps its previous payload until the next engine tick.");
            }
        }

        /// <summary>
        /// A writer for a list payload. <b>Required</b> — a <c>ValueBinding&lt;List&lt;T&gt;&gt;</c>
        /// that omits its writer throws on construction.
        /// </summary>
        /// <remarks>
        /// <c>ValueBinding</c> falls back to <c>ValueWriters.Create&lt;T&gt;()</c>, which for a list
        /// does <c>Activator.CreateInstance(typeof(ListWriter&lt;&gt;).MakeGenericType(…))</c> — and
        /// <c>ListWriter&lt;T&gt;</c>'s only constructor is <c>ListWriter(IWriter&lt;T&gt; = null)</c>.
        /// A constructor with an optional parameter is not a default constructor to the CLR, so that
        /// reflection call raises <see cref="MissingMethodException"/> every single time. Supplying
        /// the writer here is what sidesteps the reflection path entirely.
        /// <para>
        /// The return type looks wrong and is not: <c>ListWriter&lt;T&gt;</c> implements
        /// <c>IWriter&lt;IList&lt;T&gt;&gt;</c>, and <c>IWriter&lt;in T&gt;</c> is contravariant, so it
        /// converts to <c>IWriter&lt;List&lt;T&gt;&gt;</c>.
        /// </para>
        /// </remarks>
        protected static IWriter<List<T>> ListOf<T>() where T : IJsonWritable =>
            new ListWriter<T>(new ValueWriter<T>());

        /// <summary>A writer that emits JSON null instead of throwing when the payload is absent.</summary>
        protected static NullableWriter<T> Nullable<T>() where T : class, IJsonWritable =>
            new NullableWriter<T>(new ValueWriter<T>());
    }
}
