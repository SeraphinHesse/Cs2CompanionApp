using System;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Routes to the mod's single <c>Colossal.Logging</c> logger (see <c>src/CLAUDE.md</c>).
    ///
    /// <para>
    /// The whole flavor pipeline runs on a background thread, and non-negotiable #7 says a broken LLM
    /// must never take the sim with it. A logger that throws would do exactly that, so every call is
    /// swallowed.
    /// </para>
    ///
    /// <para>
    /// In its own file, away from <see cref="IFlavorLog"/>, because it is the one type in
    /// <c>Llm/</c> that names a game type: the interface has to be compilable into
    /// <c>Agora.Core.Tests</c> by link (see that project's file list) and this adapter must not
    /// follow it there.
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
