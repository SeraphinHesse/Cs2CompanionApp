using System;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// The logging seam for the flavor pipeline.
    ///
    /// <para>
    /// Everything in <c>Llm/</c> except <see cref="ColossalFlavorLog"/> logs through this interface
    /// rather than through <c>Colossal.Logging</c> directly. That is deliberate: the parsing,
    /// validation, prompt assembly and CLI-location logic touch no game type at all, so keeping the
    /// only game reference behind one two-line adapter means those classes stay constructible — and
    /// therefore testable — in a plain <c>dotnet test</c> process with no copy of the game installed.
    /// </para>
    /// </summary>
    public interface IFlavorLog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception exception = null);
        void Debug(string message);
    }

    /// <summary>Discards everything. The default, so nothing here ever crashes on a null logger.</summary>
    public sealed class NullFlavorLog : IFlavorLog
    {
        public static readonly NullFlavorLog Instance = new NullFlavorLog();

        private NullFlavorLog() { }

        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception exception = null) { }
        public void Debug(string message) { }
    }

    /// <summary>
    /// Routes to the mod's single <c>Colossal.Logging</c> logger (see <c>src/CLAUDE.md</c>).
    ///
    /// <para>
    /// The whole flavor pipeline runs on a background thread, and non-negotiable #7 says a broken LLM
    /// must never take the sim with it. A logger that throws would do exactly that, so every call is
    /// swallowed.
    /// </para>
    /// </summary>
    public sealed class ColossalFlavorLog : IFlavorLog
    {
        private const string Prefix = "llm: ";

        public static readonly ColossalFlavorLog Instance = new ColossalFlavorLog();

        public void Info(string message)
        {
            try { AgoraMod.Log.Info(Prefix + message); } catch { }
        }

        public void Warn(string message)
        {
            try { AgoraMod.Log.Warn(Prefix + message); } catch { }
        }

        public void Error(string message, Exception exception = null)
        {
            try
            {
                if (exception == null) AgoraMod.Log.Error(Prefix + message);
                else AgoraMod.Log.Error(exception, Prefix + message);
            }
            catch { }
        }

        public void Debug(string message)
        {
            try { AgoraMod.Log.Debug(Prefix + message); } catch { }
        }
    }
}
