// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// The logging seam for the flavor pipeline.
    ///
    /// <para>
    /// Everything in <c>Llm/</c> except <c>ColossalFlavorLog</c> logs through this interface
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
}
