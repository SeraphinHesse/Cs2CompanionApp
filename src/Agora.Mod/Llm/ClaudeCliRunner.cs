// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Agora.Mod.Llm
{
    /// <summary>Why a CLI attempt ended.</summary>
    public enum CliOutcome
    {
        /// <summary>Process ran and exited 0. <see cref="CliResult.StandardOutput"/> holds its stdout.</summary>
        Success = 0,

        /// <summary>No executable could be located. Retrying is pointless.</summary>
        NotInstalled = 1,

        /// <summary>Located but would not start - permissions, a broken shim, a wrong architecture.</summary>
        LaunchFailed = 2,

        /// <summary>Exceeded the time budget and was killed.</summary>
        TimedOut = 3,

        /// <summary>Ran to completion with a non-zero exit code.</summary>
        Failed = 4
    }

    /// <summary>One attempt's outcome. Immutable; never carries an exception the caller must handle.</summary>
    public sealed class CliResult
    {
        public CliOutcome Outcome { get; internal set; }
        public int ExitCode { get; internal set; }
        public string StandardOutput { get; internal set; }
        public string StandardError { get; internal set; }

        /// <summary>Human-readable summary, already safe to log.</summary>
        public string Detail { get; internal set; }

        public bool IsSuccess => Outcome == CliOutcome.Success;

        /// <summary>True when a second attempt could plausibly do better.</summary>
        public bool IsRetryable => Outcome == CliOutcome.TimedOut || Outcome == CliOutcome.Failed;

        internal CliResult()
        {
            StandardOutput = string.Empty;
            StandardError = string.Empty;
            Detail = string.Empty;
        }
    }

    /// <summary>
    /// Spawns the Claude CLI, feeds it a prompt on stdin, and returns whatever it wrote - or an
    /// explanation of why it did not.
    ///
    /// <para>
    /// <b>This method never throws.</b> Non-negotiable #7 makes that a hard requirement rather than a
    /// nicety: the flavor path is allowed to fail, and every way it can fail - a missing binary, a
    /// wedged process, a hostile stdout - is a value in <see cref="CliOutcome"/>.
    /// </para>
    ///
    /// <para>
    /// Three details here are not optional and are easy to get wrong:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>stdout and stderr are drained asynchronously.</b> Reading <c>StandardOutput</c> to the end
    /// before <c>WaitForExit</c> deadlocks the moment the child fills the stderr pipe buffer, and a
    /// CLI that prints progress to stderr will do that on a long generation. The event-based drain
    /// plus <c>WaitForExit(timeout)</c> plus a final unbounded <c>WaitForExit()</c> is the documented
    /// pattern.
    /// </description></item>
    /// <item><description>
    /// <b>The prompt is written as UTF-8 bytes to the raw stdin stream.</b> <c>StandardInputEncoding</c>
    /// does not exist on .NET Framework, and the default <c>StandardInput</c> writer uses the console's
    /// ANSI code page - which would mangle any non-ASCII district name on the way in.
    /// </description></item>
    /// <item><description>
    /// <b>A <c>.cmd</c> shim is routed through <c>cmd.exe</c>.</b> See
    /// <see cref="ClaudeCliLocator.NeedsCommandShell"/>.
    /// </description></item>
    /// </list>
    /// </summary>
    public sealed class ClaudeCliRunner
    {
        private readonly IFlavorLog _log;

        public ClaudeCliRunner(IFlavorLog log)
        {
            _log = log ?? NullFlavorLog.Instance;
        }

        /// <summary>
        /// The CLI arguments. Headless, single-shot, JSON envelope - exactly the invocation §3
        /// ratified.
        /// </summary>
        /// <remarks>
        /// The prompt is <i>not</i> passed as an argument. A prompt carrying a city snapshot runs to
        /// tens of kilobytes and the Windows command line caps out around 32k, so it goes on stdin;
        /// bare <c>-p</c> with no positional prompt makes the CLI read there.
        /// </remarks>
        public const string Arguments = "-p --output-format json";

        /// <summary>
        /// The full argument string for one invocation: <see cref="Arguments"/> plus the model.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both branches of <c>BuildStartInfo</c> call this, and that is the point of its existing.
        /// The direct branch hands its arguments to <c>CreateProcess</c> untouched; the <c>cmd.exe</c>
        /// branch nests them inside one outer pair of quotes that <c>cmd /s /c</c> strips before
        /// re-parsing what is left. Two hand-assembled strings would let someone fix one and leave
        /// the other, and the resulting bug would be invisible on a machine where the CLI resolves to
        /// <c>claude.exe</c> - which is to say invisible to whoever wrote it, and live for the
        /// majority of players, who have the npm <c>.cmd</c> shim.
        /// </para>
        /// <para>
        /// The model goes in <b>bare</b>, with no quotes of its own. Quoting it would be the natural
        /// instinct and would be wrong: inner quotes break the outer pair <c>/s</c> keys on, and cmd
        /// then re-parses the whole line. Bare is safe only because
        /// <see cref="ClaudeCliOptions.IsValidModelId"/> has already excluded whitespace and every
        /// character cmd treats as syntax - <c>&amp; | ^ &lt; &gt; %</c> and the quote itself.
        /// </para>
        /// </remarks>
        public static string BuildArguments(ClaudeCliOptions options)
        {
            string model = options == null ? ClaudeCliOptions.DefaultModel : options.Model;
            if (!ClaudeCliOptions.IsValidModelId(model)) model = ClaudeCliOptions.DefaultModel;
            return Arguments + " --model " + model;
        }

        /// <summary>
        /// Runs one attempt. <paramref name="executablePath"/> null or missing gives
        /// <see cref="CliOutcome.NotInstalled"/>.
        /// </summary>
        public CliResult Run(string executablePath, string prompt, ClaudeCliOptions options)
        {
            options = options ?? new ClaudeCliOptions();

            if (string.IsNullOrEmpty(executablePath))
            {
                return new CliResult
                {
                    Outcome = CliOutcome.NotInstalled,
                    Detail = "no Claude CLI found on PATH or in the known install locations"
                };
            }

            ProcessStartInfo startInfo;
            try
            {
                startInfo = BuildStartInfo(executablePath, options);
            }
            catch (Exception ex)
            {
                return new CliResult
                {
                    Outcome = CliOutcome.LaunchFailed,
                    Detail = "could not build the process start info: " + ex.Message
                };
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var stdoutDone = new System.Threading.ManualResetEvent(false);
            var stderrDone = new System.Threading.ManualResetEvent(false);

            Process process = null;
            try
            {
                process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };

                process.OutputDataReceived += (sender, e) =>
                {
                    // A null Data is the stream's end-of-file marker, not a blank line.
                    if (e.Data == null) { TrySet(stdoutDone); return; }
                    lock (stdout) { stdout.Append(e.Data).Append('\n'); }
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null) { TrySet(stderrDone); return; }
                    lock (stderr) { stderr.Append(e.Data).Append('\n'); }
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    return new CliResult
                    {
                        Outcome = CliOutcome.LaunchFailed,
                        Detail = "'" + executablePath + "' would not start: " + ex.Message
                    };
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                WritePromptAndCloseStdin(process, prompt);

                int timeoutMs = ClampTimeoutMs(options.TimeoutSeconds);
                if (!process.WaitForExit(timeoutMs))
                {
                    KillQuietly(process);
                    return new CliResult
                    {
                        Outcome = CliOutcome.TimedOut,
                        StandardError = Read(stderr),
                        Detail = "no answer within " + (timeoutMs / 1000).ToString(CultureInfo.InvariantCulture) +
                                 "s; the process was killed"
                    };
                }

                // WaitForExit(int) returns as soon as the process object exits, which can be BEFORE
                // the redirected streams have been fully drained. The parameterless overload is the
                // one that also waits for the async readers, so call it once the timed wait passed.
                try { process.WaitForExit(); } catch { }
                stdoutDone.WaitOne(TimeSpan.FromSeconds(2));
                stderrDone.WaitOne(TimeSpan.FromSeconds(2));

                int exitCode = SafeExitCode(process);
                string outText = Read(stdout);
                string errText = Read(stderr);

                if (exitCode != 0)
                {
                    return new CliResult
                    {
                        Outcome = CliOutcome.Failed,
                        ExitCode = exitCode,
                        StandardOutput = outText,
                        StandardError = errText,
                        Detail = "exited " + exitCode.ToString(CultureInfo.InvariantCulture) +
                                 (string.IsNullOrEmpty(errText) ? string.Empty : ": " + FirstLine(errText))
                    };
                }

                return new CliResult
                {
                    Outcome = CliOutcome.Success,
                    ExitCode = 0,
                    StandardOutput = outText,
                    StandardError = errText,
                    Detail = "ok, " + outText.Length.ToString(CultureInfo.InvariantCulture) + " characters"
                };
            }
            catch (Exception ex)
            {
                // The catch-all that makes the "never throws" promise true.
                _log.Debug("CLI attempt failed unexpectedly: " + ex.Message);
                return new CliResult
                {
                    Outcome = CliOutcome.LaunchFailed,
                    Detail = "unexpected failure running the CLI: " + ex.Message
                };
            }
            finally
            {
                try { stdoutDone.Close(); } catch { }
                try { stderrDone.Close(); } catch { }
                if (process != null)
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }

        /// <summary>
        /// Assembles the start info without starting anything.
        /// </summary>
        /// <remarks>
        /// Internal rather than private so the test suite - which compiles this file in by link - can
        /// assert on the finished command line. The alternative, covering the quoting by calling
        /// <see cref="Run"/>, would spawn a real subprocess on whatever machine ran the tests.
        /// <para>
        /// A null <paramref name="options"/> falls back to the defaults rather than throwing, matching
        /// <see cref="BuildArguments"/>. <see cref="Run"/> already coalesces before it calls here, so
        /// this is defensive only - but nothing on the flavor path may throw (non-negotiable #7), and
        /// two neighbouring methods disagreeing about null is how one of them eventually does.
        /// </para>
        /// </remarks>
        internal static ProcessStartInfo BuildStartInfo(string executablePath, ClaudeCliOptions options)
        {
            ProcessStartInfo startInfo;
            string arguments = BuildArguments(options);

            if (ClaudeCliLocator.NeedsCommandShell(executablePath))
            {
                // cmd /d /s /c "" <path> " args" - the outer quotes are cmd's documented way of
                // handling a quoted program path, and /d skips AutoRun scripts that could otherwise
                // print junk onto our stdout.
                startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/d /s /c \"\"" + executablePath + "\" " + arguments + "\""
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments
                };
            }

            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = new UTF8Encoding(false);
            startInfo.StandardErrorEncoding = new UTF8Encoding(false);

            if (options != null && !string.IsNullOrEmpty(options.WorkingDirectory))
            {
                startInfo.WorkingDirectory = options.WorkingDirectory;
            }

            return startInfo;
        }

        private void WritePromptAndCloseStdin(Process process, string prompt)
        {
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(prompt ?? string.Empty);
                var raw = process.StandardInput.BaseStream;
                raw.Write(bytes, 0, bytes.Length);
                raw.Flush();
            }
            catch (Exception ex)
            {
                // A child that exited before reading gives a broken pipe here. Not fatal on its own;
                // the exit code and stderr below will say what actually happened.
                _log.Debug("could not write the whole prompt to the CLI's stdin: " + ex.Message);
            }
            finally
            {
                // Closing stdin is what tells the CLI the prompt is complete. Skip it and the timeout
                // becomes the only way the call ever ends.
                try { process.StandardInput.Close(); } catch { }
            }
        }

        private static int ClampTimeoutMs(int seconds)
        {
            if (seconds < 1) seconds = 1;
            if (seconds > 3600) seconds = 3600;
            return seconds * 1000;
        }

        private static void KillQuietly(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch { }

            // Give the OS a moment to reap it so the handle in `finally` is not disposed mid-teardown.
            try { process.WaitForExit(2000); } catch { }
        }

        private static int SafeExitCode(Process process)
        {
            try { return process.ExitCode; } catch { return -1; }
        }

        private static string Read(StringBuilder builder)
        {
            lock (builder) { return builder.ToString(); }
        }

        private static void TrySet(System.Threading.ManualResetEvent handle)
        {
            try { handle.Set(); } catch { }
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int newline = text.IndexOf('\n');
            string line = newline < 0 ? text : text.Substring(0, newline);
            return line.Length <= 300 ? line.Trim() : line.Substring(0, 300).Trim();
        }
    }
}
